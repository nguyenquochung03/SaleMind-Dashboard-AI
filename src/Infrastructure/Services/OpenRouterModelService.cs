/*
 * Lớp OpenRouterModelService quản lý danh sách và chất lượng của các mô hình AI miễn phí từ OpenRouter:
 * 1. Khám phá (Discovery): Tự động lấy danh sách các mô hình miễn phí mới nhất từ API OpenRouter.
 * 2. Đánh giá & Xếp hạng (Ranking): Phân loại và ưu tiên các mô hình dựa trên chất lượng (ví dụ: Gemini, Llama) 
 *    và lịch sử hoạt động ổn định.
 * 3. Theo dõi sức khỏe (Health Check): Ghi nhận các mô hình bị lỗi để tạm thời đưa vào danh sách chờ (cooldown) 
 *    và ưu tiên các mô hình hoạt động tốt, đảm bảo hệ thống luôn có AI sẵn sàng phục vụ.
 */
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Service to fetch, cache, and prioritize available free models from OpenRouter API
/// </summary>
public interface IOpenRouterModelService
{
    /// <summary>
    /// Get list of available free models
    /// </summary>
    Task<List<string>> GetFreeModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get free models ordered by priority and recent health signals
    /// </summary>
    Task<List<string>> GetRankedFreeModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the best available free model
    /// </summary>
    Task<string?> GetBestFreeModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a specific model is available
    /// </summary>
    Task<bool> IsModelAvailableAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark model as healthy after a successful request
    /// </summary>
    void MarkModelSuccess(string modelId);

    /// <summary>
    /// Mark model as temporarily unavailable after upstream/provider failure
    /// </summary>
    void MarkModelFailure(string modelId, TimeSpan cooldown, string? reason = null);
}

public class OpenRouterModelService : IOpenRouterModelService
{
    private const string FreeModelsCacheKey = "openrouter_free_models";
    private const string ModelFailurePrefix = "openrouter_model_failure_";
    private const string ModelSuccessPrefix = "openrouter_model_success_";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OpenRouterModelService> _logger;

    public OpenRouterModelService(
        HttpClient httpClient,
        IMemoryCache cache,
        ILogger<OpenRouterModelService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<string>> GetFreeModelsAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(FreeModelsCacheKey, out List<string>? cachedModels) && cachedModels != null)
        {
            _logger.LogDebug("Returning cached free models (count: {Count})", cachedModels.Count);
            return cachedModels;
        }

        try
        {
            _logger.LogInformation("Fetching free models from OpenRouter API...");

            var response = await _httpClient.GetAsync("https://openrouter.ai/api/v1/models", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch models from OpenRouter: {StatusCode}", response.StatusCode);
                return GetDefaultFreeModels();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var modelsResponse = JsonSerializer.Deserialize<OpenRouterModelsResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (modelsResponse?.Data == null)
            {
                _logger.LogWarning("No models data in OpenRouter response");
                return GetDefaultFreeModels();
            }

            var freeModels = modelsResponse.Data
                .Where(m => m.Id != null && m.Id.EndsWith(":free", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation("Found {Count} free models on OpenRouter", freeModels.Count);

            _cache.Set(FreeModelsCacheKey, freeModels, CacheDuration);

            return freeModels;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching free models from OpenRouter");
            return GetDefaultFreeModels();
        }
    }

    public async Task<List<string>> GetRankedFreeModelsAsync(CancellationToken cancellationToken = default)
    {
        var freeModels = await GetFreeModelsAsync(cancellationToken);

        if (freeModels.Count == 0)
        {
            return freeModels;
        }

        var priorityModels = new[]
        {
            "google/gemini-2.0-flash-001",
            "google/gemini-2.0-flash-lite-001",
            "meta-llama/llama-3.1-70b-instruct:free",
            "google/gemini-flash-1.5:free",
            "qwen/qwen-2.5-72b-instruct:free",
            "mistralai/mistral-nemo:free"
        };

        var preferredPatterns = new[]
        {
            "step",
            "qwen",
            "llama",
            "gemma",
            "mistral",
            "deepseek",
            "minimax",
            "nemotron"
        };

        var activeFailures = freeModels
            .Where(IsModelCoolingDown)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var successScores = freeModels.ToDictionary(
            model => model,
            model => GetSuccessScore(model),
            StringComparer.OrdinalIgnoreCase);

        var ranked = freeModels
            .OrderBy(model => activeFailures.Contains(model) ? 1 : 0)
            .ThenBy(model => GetPriorityIndex(model, priorityModels))
            .ThenBy(model => GetPatternIndex(model, preferredPatterns))
            .ThenByDescending(model => successScores[model])
            .ThenBy(model => model, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "Ranked free models prepared. Active cooldowns: {CooldownCount}. Top candidates: {Candidates}",
            activeFailures.Count,
            string.Join(", ", ranked.Take(10)));

        return ranked;
    }

    public async Task<string?> GetBestFreeModelAsync(CancellationToken cancellationToken = default)
    {
        var rankedModels = await GetRankedFreeModelsAsync(cancellationToken);
        var bestModel = rankedModels.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(bestModel))
        {
            _logger.LogWarning("No free models available");
            return null;
        }

        _logger.LogInformation("Selected best free model: {Model}", bestModel);
        return bestModel;
    }

    public async Task<bool> IsModelAvailableAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var freeModels = await GetFreeModelsAsync(cancellationToken);
        return freeModels.Contains(modelId, StringComparer.OrdinalIgnoreCase);
    }

    public void MarkModelSuccess(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return;
        }

        var successKey = GetSuccessCacheKey(modelId);
        var current = _cache.TryGetValue(successKey, out int count) ? count : 0;
        _cache.Set(successKey, current + 1, TimeSpan.FromHours(6));
        _cache.Remove(GetFailureCacheKey(modelId));

        _logger.LogDebug("Marked model success: {Model} (success count: {Count})", modelId, current + 1);
    }

    public void MarkModelFailure(string modelId, TimeSpan cooldown, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return;
        }

        _cache.Set(GetFailureCacheKey(modelId), reason ?? "unknown", cooldown);

        _logger.LogWarning(
            "Marked model failure: {Model}. Cooldown: {Cooldown}. Reason: {Reason}",
            modelId,
            cooldown,
            reason ?? "unknown");
    }

    private bool IsModelCoolingDown(string modelId)
    {
        return _cache.TryGetValue(GetFailureCacheKey(modelId), out _);
    }

    private int GetSuccessScore(string modelId)
    {
        return _cache.TryGetValue(GetSuccessCacheKey(modelId), out int count) ? count : 0;
    }

    private static int GetPriorityIndex(string modelId, IReadOnlyList<string> priorityModels)
    {
        for (var i = 0; i < priorityModels.Count; i++)
        {
            if (string.Equals(modelId, priorityModels[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static int GetPatternIndex(string modelId, IReadOnlyList<string> preferredPatterns)
    {
        for (var i = 0; i < preferredPatterns.Count; i++)
        {
            if (modelId.Contains(preferredPatterns[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static string GetFailureCacheKey(string modelId) => $"{ModelFailurePrefix}{modelId}";
    private static string GetSuccessCacheKey(string modelId) => $"{ModelSuccessPrefix}{modelId}";

    private static List<string> GetDefaultFreeModels()
    {
        return new List<string>
        {
            "stepfun/step-3.5-flash:free",
            "qwen/qwen3-coder:free",
            "meta-llama/llama-3.2-3b-instruct:free",
            "google/gemma-3-4b-it:free",
            "qwen/qwen-2-7b-instruct:free",
            "google/gemma-3-12b-it:free"
        };
    }
}

internal class OpenRouterModelsResponse
{
    public List<OpenRouterModel>? Data { get; set; }
}

internal class OpenRouterModel
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? ContextLength { get; set; }
}
