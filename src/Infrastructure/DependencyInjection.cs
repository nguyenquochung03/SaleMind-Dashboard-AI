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
        var redisConfig = configuration.GetSection("Redis:Configuration").Value;
        var useMemoryCache = string.IsNullOrEmpty(redisConfig) || 
                             redisConfig.Contains("REPLACE_WITH_REAL_CONFIG") ||
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
