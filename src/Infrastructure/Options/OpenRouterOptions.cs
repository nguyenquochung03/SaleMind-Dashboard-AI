namespace Infrastructure.Options;

/// <summary>
/// Configuration options for OpenRouter API
/// </summary>
public class OpenRouterOptions
{
    public const string SectionName = "AI:OpenRouter";

    /// <summary>
    /// OpenRouter API Key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for OpenRouter API
    /// </summary>
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// Preferred model to use (if empty, will auto-select from available free models)
    /// </summary>
    public string? PreferredModel { get; set; }

    /// <summary>
    /// Whether to auto-select the best available free model
    /// </summary>
    public bool AutoSelectModel { get; set; } = true;

    /// <summary>
    /// Maximum number of retries
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum tokens in response
    /// </summary>
    public int MaxTokens { get; set; } = 2000;

    /// <summary>
    /// Temperature for response generation (0-2)
    /// </summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>
    /// Site URL for OpenRouter ranking
    /// </summary>
    public string? SiteUrl { get; set; }

    /// <summary>
    /// Site name for OpenRouter ranking
    /// </summary>
    public string? SiteName { get; set; } = "SalesMind AI";

    /// <summary>
    /// Maximum number of free models to probe sequentially per request when auto-select is enabled.
    /// </summary>
    public int ProbeTopFreeModels { get; set; } = 12;

    /// <summary>
    /// Minutes to temporarily deprioritize a model after upstream/provider failure such as 429/403/404.
    /// </summary>
    public int ModelCooldownMinutes { get; set; } = 10;

    /// <summary>
    /// If enabled, stop probing additional models immediately when the account has exhausted free-tier quota
    /// (for example free-models-per-day / free-models-per-min) to avoid repeated slow failures in one request.
    /// </summary>
    public bool FailFastOnQuotaExceeded { get; set; } = true;
}
