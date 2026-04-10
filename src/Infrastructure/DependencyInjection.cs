using Application.DTOs;
using Application.Interfaces;
using Infrastructure.Clients;
using Infrastructure.Options;
using Infrastructure.Services;
using Infrastructure.Services.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Extensions.Http;

namespace Infrastructure;

/// <summary>
/// Extension methods for registering Infrastructure services into the DI container.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.AddMemoryCache();

        // ===== Cache Services (Redis for External APIs with Memory fallback) =====
        // Order of precedence:
        // 1. Environment Variable REDIS_URL (standard format on Render)
        // 2. Environment Variable REDIS_CONNECTIONSTRING
        // 3. IConfiguration (includes appsettings.json and optional Redis__Configuration env var)
        
        var redisConfig = configuration["REDIS_URL"] ?? 
                         configuration["REDIS_CONNECTIONSTRING"] ?? 
                         configuration["Redis:Configuration"];

        if (!string.IsNullOrEmpty(redisConfig))
        {
            // Robust parsing for URI formats like redis://... or rediss://...
            if (redisConfig.StartsWith("redis://", StringComparison.OrdinalIgnoreCase))
            {
                redisConfig = redisConfig.Substring(8);
            }
            else if (redisConfig.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
            {
                redisConfig = redisConfig.Substring(9);
            }

            // If it contains a '@', it's likely a URL format containing credentials: :password@host:port
            if (redisConfig.Contains('@') && !redisConfig.Contains('='))
            {
                var parts = redisConfig.Split('@');
                var userInfo = parts[0];
                var hostInfo = parts[1];

                if (userInfo.Contains(':'))
                {
                    var password = userInfo.Split(':').Last();
                    redisConfig = $"{hostInfo},password={password}";
                }
                else
                {
                    redisConfig = $"{hostInfo},password={userInfo}";
                }
            }
            
            // Ensure abortConnect=false so the application doesn't crash if Redis is briefly unavailable
            if (!redisConfig.Contains("abortConnect=", StringComparison.OrdinalIgnoreCase))
            {
                redisConfig = $"{redisConfig},abortConnect=false";
            }
        }

        var useMemoryCache = string.IsNullOrEmpty(redisConfig) || 
                             redisConfig.Contains("REPLACE_WITH_REAL_CONFIG", StringComparison.OrdinalIgnoreCase) ||
                             configuration.GetValue<bool>("Redis:UseMemoryCache", false);

        if (!useMemoryCache)
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConfig;
                options.InstanceName = configuration.GetSection("Redis:InstanceName").Value ?? "SalesMindAI_";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // ===== Security Services =====
        services.AddSingleton<IInputSanitizer, InputSanitizer>();
        
        // Rate Limiter Configuration
        services.AddOptions<RateLimiterOptions>()
            .BindConfiguration("Security:RateLimiter");
        services.AddSingleton<IRateLimiter, RateLimiter>();

        // External Sales API Configuration
        services.AddOptions<ExternalSalesApiOptions>()
            .BindConfiguration("ExternalApis:SalesData")
            .Validate(options => !string.IsNullOrWhiteSpace(options.BaseUrl), "ExternalApis:SalesData:BaseUrl is required.");

        // External Sales API Client with Polly retry
        services.AddHttpClient<IExternalSalesApiClient, ExternalSalesApiClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExternalSalesApiOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 10);
            })
            .AddPolicyHandler(GetRetryPolicy());

        // ===== Sales Services =====
        services.AddScoped<ISalesService, ExternalSalesService>();

        // ===== AI/LLM Services =====
        
        // OpenRouter Configuration
        services.AddOptions<OpenRouterOptions>()
            .BindConfiguration("AI:OpenRouter")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "AI:OpenRouter:ApiKey is required for AI features.");

        // OpenRouter Model Service (for dynamic model discovery)
        services.AddHttpClient<IOpenRouterModelService, OpenRouterModelService>();

        // OpenRouter HTTP Client for LLM
        services.AddHttpClient<ILLMService, OpenRouterService>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenRouterOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 30);
                
                // Set authorization header
                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    client.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
                }
                
                // OpenRouter specific headers
                var siteUrl = string.IsNullOrWhiteSpace(options.SiteUrl)
                    ? "http://localhost:5062"
                    : options.SiteUrl;
                var siteName = string.IsNullOrWhiteSpace(options.SiteName)
                    ? "SalesMind AI"
                    : options.SiteName;

                client.DefaultRequestHeaders.Add("HTTP-Referer", siteUrl);
                client.DefaultRequestHeaders.Add("X-Title", siteName);
            });

        // Orchestrator Configuration
        services.AddOptions<OrchestratorConfig>()
            .BindConfiguration("AI:Orchestrator");

        // Tool Registry with all tools
        services.AddScoped<IToolRegistry, ToolRegistry>();
        
        // Register individual tools
        services.AddScoped<ITool, GetSalesDataTool>();
        services.AddScoped<ITool, CompareRegionsTool>();
        services.AddScoped<ITool, AnalyzePipelineTool>();
        services.AddScoped<ITool, CalculateKpiTool>();

        // Chat Orchestrator
        services.AddScoped<IChatOrchestrator, ChatOrchestrator>();

        // ===== Chat Service =====
        // Use ChatOrchestrator as the main IChatService implementation
        services.AddScoped<IChatService>(serviceProvider =>
        {
            var orchestrator = serviceProvider.GetRequiredService<IChatOrchestrator>();
            return new OrchestratorChatService(orchestrator);
        });

        return services;
    }

    private static IAsyncPolicy<System.Net.Http.HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => (int)response.StatusCode == 408)
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }
}

/// <summary>
/// Chat service implementation that uses the AI Orchestrator
/// </summary>
public class OrchestratorChatService : IChatService
{
    private readonly IChatOrchestrator _orchestrator;

    public OrchestratorChatService(IChatOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<ChatResponse> ProcessMessageAsync(string message)
    {
        return await _orchestrator.ProcessMessageAsync(message);
    }
}
