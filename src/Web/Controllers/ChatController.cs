using Application.DTOs;
using Application.Interfaces;
using Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly IChatOrchestrator _orchestrator;
    private readonly IInputSanitizer _sanitizer;
    private readonly IRateLimiter _rateLimiter;
    private readonly ILogger<ChatController> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ChatController(
        IChatOrchestrator orchestrator,
        IInputSanitizer sanitizer,
        IRateLimiter rateLimiter,
        ILogger<ChatController> logger)
    {
        _orchestrator = orchestrator;
        _sanitizer    = sanitizer;
        _rateLimiter  = rateLimiter;
        _logger       = logger;
    }

    // =========================================================================
    // POST /api/chat/send  — standard JSON response
    // =========================================================================

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] ChatMessageRequest request, CancellationToken ct)
    {
        // Validate
        if (!_sanitizer.IsValidInput(request?.Message ?? "", out var validationError))
        {
            return BadRequest(new ChatResponse
            {
                Type = "error",
                Text = $"❌ {validationError}",
                Suggestions = new List<string> { "Thử câu hỏi khác", "Xem doanh thu", "Tổng quan KPI" }
            });
        }

        // Rate limit
        var clientId = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var (allowed, retryAfterSeconds) = await _rateLimiter.CheckRateLimitAsync(clientId, ct);

        if (!allowed)
        {
            if (retryAfterSeconds.HasValue)
            {
                Response.Headers["Retry-After"] = retryAfterSeconds.Value.ToString();
                return StatusCode(429, new ChatResponse
                {
                    Type = "error",
                    Text = "⏱️ Bạn đã gửi quá nhiều yêu cầu. Vui lòng đợi trước khi thử lại.",
                    Suggestions = new List<string> { "Thử lại sau ít phút" }
                });
            }

            return StatusCode(429, new ChatResponse
            {
                Type = "error",
                Text = "📅 Bạn đã đạt giới hạn yêu cầu trong ngày. Vui lòng thử lại vào ngày mai.",
                Suggestions = new List<string> { "Thử lại ngày mai" }
            });
        }

        await _rateLimiter.RecordRequestAsync(clientId, ct);

        var sanitized = _sanitizer.Sanitize(request!.Message);
        _logger.LogInformation("Chat [non-stream]: {Message}", sanitized);

        try
        {
            var response = await _orchestrator.ProcessMessageAsync(sanitized, request.History, ct);
            return Ok(response);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new ChatResponse { Type = "error", Text = "Yêu cầu bị huỷ." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in ChatController.Send");
            return StatusCode(500, new ChatResponse
            {
                Type = "error",
                Text = "❌ Xin lỗi, đã xảy ra lỗi. Vui lòng thử lại.",
                Suggestions = new List<string> { "Thử lại", "Xem doanh thu", "So sánh khu vực" }
            });
        }
    }

    // =========================================================================
    // GET /api/chat/stream?message=...  — SSE streaming
    // =========================================================================

    [HttpGet("stream")]
    public async Task Stream([FromQuery] string message, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";  // Nginx: disable buffering

        if (!_sanitizer.IsValidInput(message ?? "", out var validationError))
        {
            var errJson = JsonSerializer.Serialize(new ChatResponse
            {
                Type = "error",
                Text = $"❌ {validationError}"
            }, _jsonOpts);
            await Response.WriteAsync($"data: {errJson}\n\n", ct);
            return;
        }

        var clientId = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var (allowed, retryAfterSeconds) = await _rateLimiter.CheckRateLimitAsync(clientId, ct);

        if (!allowed)
        {
            var errJson = JsonSerializer.Serialize(new ChatResponse
            {
                Type = "error",
                Text = retryAfterSeconds.HasValue
                    ? "⏱️ Bạn đã gửi quá nhiều yêu cầu. Vui lòng đợi trước khi thử lại."
                    : "📅 Bạn đã đạt giới hạn yêu cầu trong ngày."
            }, _jsonOpts);
            await Response.WriteAsync($"data: {errJson}\n\n", ct);
            return;
        }

        await _rateLimiter.RecordRequestAsync(clientId, ct);

        var sanitized = _sanitizer.Sanitize(message!);
        _logger.LogInformation("Chat [stream]: {Message}", sanitized);

        await foreach (var chunk in _orchestrator.StreamMessageAsync(sanitized, null, ct))
        {
            await Response.WriteAsync(chunk, ct);
            await Response.Body.FlushAsync(ct);
        }

        await Response.WriteAsync("data: [DONE]\n\n", ct);
    }
}