/*
 * Lớp RateLimiter kiểm soát tần suất gửi yêu cầu lên hệ thống để ngăn chặn lạm dụng và quá tải:
 * 1. Giới hạn lượt truy cập: Thiết lập số lượng yêu cầu tối đa cho phép trong một khoảng thời gian ngắn (ví dụ: 1 phút) 
 *    và giới hạn tổng số trong một ngày (Daily Limit).
 * 2. Lưu trữ trạng thái: Sử dụng Distributed Cache để ghi nhớ số lần truy cập của từng người dùng, 
 *    đảm bảo tính nhất quán ngay cả khi hệ thống chạy trên nhiều máy chủ.
 * 3. Bảo vệ tài nguyên: Ngăn chặn các hành vi tấn công từ chối dịch vụ hoặc sử dụng quá mức hạn mức AI, 
 *    giúp duy trì sự ổn định cho toàn hệ thống.
 */
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// Rate limiting service to prevent abuse of AI endpoints
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Check if a request is allowed under rate limits
    /// </summary>
    /// <returns>True if allowed, false if rate limited</returns>
    Task<(bool Allowed, int? RetryAfterSeconds)> CheckRateLimitAsync(string clientId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Record a request for rate limiting
    /// </summary>
    Task RecordRequestAsync(string clientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for rate limiting
/// </summary>
public class RateLimiterOptions
{
    /// <summary>
    /// Maximum requests allowed per window
    /// </summary>
    public int MaxRequests { get; set; } = 30;
    
    /// <summary>
    /// Time window in minutes
    /// </summary>
    public int WindowMinutes { get; set; } = 1;
    
    /// <summary>
    /// Maximum requests per day (hard limit)
    /// </summary>
    public int MaxDailyRequests { get; set; } = 500;
}

/// <summary>
/// Redis-based rate limiter implementation using IDistributedCache
/// </summary>
public class RateLimiter : IRateLimiter
{
    private readonly IDistributedCache _cache;
    private readonly RateLimiterOptions _options;
    private readonly ILogger<RateLimiter> _logger;

    public RateLimiter(
        IDistributedCache cache,
        IOptions<RateLimiterOptions> options,
        ILogger<RateLimiter> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(bool Allowed, int? RetryAfterSeconds)> CheckRateLimitAsync(
        string clientId, 
        CancellationToken cancellationToken = default)
    {
        // Check daily limit
        var dailyKey = GetDailyCacheKey(clientId);
        var dailyCountStr = await _cache.GetStringAsync(dailyKey, cancellationToken);
        if (int.TryParse(dailyCountStr, out var dailyCount) && dailyCount >= _options.MaxDailyRequests)
        {
            _logger.LogWarning("Daily rate limit exceeded for client: {ClientId}", clientId);
            return (false, null); // No retry until tomorrow
        }
        
        // Check per-window limit
        var windowKey = GetWindowCacheKey(clientId);
        var windowCountStr = await _cache.GetStringAsync(windowKey, cancellationToken);
        if (int.TryParse(windowCountStr, out var windowCount) && windowCount >= _options.MaxRequests)
        {
            var remainingTime = (int)TimeSpan.FromMinutes(_options.WindowMinutes).TotalSeconds;
            _logger.LogWarning("Rate limit exceeded for client: {ClientId}. Retry after {Seconds}s", 
                clientId, remainingTime);
            return (false, remainingTime);
        }
        
        return (true, null);
    }

    public async Task RecordRequestAsync(string clientId, CancellationToken cancellationToken = default)
    {
        // Increment daily count
        await IncrementCacheValueAsync(GetDailyCacheKey(clientId), TimeSpan.FromDays(1), cancellationToken);
        
        // Increment window count
        await IncrementCacheValueAsync(GetWindowCacheKey(clientId), TimeSpan.FromMinutes(_options.WindowMinutes), cancellationToken);
    }

    private string GetDailyCacheKey(string clientId) => 
        $"rate_limit:daily:{clientId}:{DateTime.UtcNow:yyyyMMdd}";

    private string GetWindowCacheKey(string clientId) => 
        $"rate_limit:window:{clientId}";

    private async Task IncrementCacheValueAsync(string key, TimeSpan expiration, CancellationToken cancellationToken)
    {
        var currentValStr = await _cache.GetStringAsync(key, cancellationToken);
        int newVal = 1;
        
        if (int.TryParse(currentValStr, out var currentVal))
        {
            newVal = currentVal + 1;
        }

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration
        };

        await _cache.SetStringAsync(key, newVal.ToString(), options, cancellationToken);
    }
}
