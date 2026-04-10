/*
 * Lớp ChatOrchestrator đóng vai trò là "bộ não điều phối" (Orchestrator) cho hệ thống Chat AI:
 * 1. Tiếp nhận: Nhận tin nhắn từ người dùng và chuẩn bị ngữ cảnh (lịch sử trò chuyện, System Prompt).
 * 2. Vòng lặp Agent (Agent Loop): Điều phối quá trình suy luận của LLM. Nếu LLM yêu cầu sử dụng công cụ, 
 *    Orchestrator sẽ gọi ToolRegistry để thực thi và gửi kết quả ngược lại cho LLM.
 * 3. Tổng hợp: Sau khi có đầy đủ thông tin từ các công cụ, Orchestrator yêu cầu LLM tổng hợp lại 
 *    thành câu trả lời cuối cùng ở định dạng JSON chuẩn để hiển thị lên giao diện.
 */
using System.Text;
using System.Text.Json;
using Application.DTOs;
using Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// AI Chat Orchestrator - coordinates LLM and tools for intelligent responses.
///
/// Flow: User message → LLM (with tools) → if tool_use: execute → LLM synthesize → return JSON
///       Agent loop runs up to MaxAgentSteps, LLM decides when to call tools (not backend).
/// </summary>
public class ChatOrchestrator : IChatOrchestrator
{
    private readonly ILLMService _llmService;
    private readonly IToolRegistry _toolRegistry;
    private readonly OrchestratorConfig _config;
    private readonly ILogger<ChatOrchestrator> _logger;
    private readonly IDistributedCache _cache;
    private readonly string _systemPrompt;

    // Max turns in the agent loop to prevent infinite loops
    private const int MaxAgentSteps = 6;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ChatOrchestrator(
        ILLMService llmService,
        IToolRegistry toolRegistry,
        IOptions<OrchestratorConfig> config,
        IDistributedCache cache,
        ILogger<ChatOrchestrator> logger)
    {
        _llmService = llmService;
        _toolRegistry = toolRegistry;
        _config = config.Value;
        _cache = cache;
        _logger = logger;
        _systemPrompt = SystemPromptBuilder.BuildSystemPrompt();
    }

    // =========================================================================
    // Public entry points
    // =========================================================================

    public async Task<ChatResponse> ProcessMessageAsync(
        string userMessage,
        List<ChatMessage>? conversationHistory = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing message: {Message}", userMessage);

        if (!_config.UseAI)
        {
            _logger.LogInformation("AI is disabled (UseAI=false), returning fallback response");
            return GetAiDisabledResponse();
        }

        try
        {
            // Cache check
            var cacheKey = $"chat_{userMessage.GetHashCode()}";
            var cachedJson = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                var cachedResponse = JsonSerializer.Deserialize<ChatResponse>(cachedJson, _jsonOptions);
                if (cachedResponse != null)
                {
                    _logger.LogInformation("Returning cached response for message: {Message}", userMessage);
                    return cachedResponse;
                }
            }

            // Limit history to max 20 turns
            var history = conversationHistory ?? new List<ChatMessage>();
            if (history.Count > 20)
            {
                history = history.Skip(history.Count - 20).ToList();
            }

            // Build messages with history
            var messages = new List<ChatMessage>(history)
            {
                new() { Role = "user", Content = userMessage }
            };

            var response = await RunAgentLoopAsync(messages, cancellationToken);

            // Cache response for 5 minutes
            if (response.Type != "error")
            {
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                var json = JsonSerializer.Serialize(response, _jsonOptions);
                await _cache.SetStringAsync(cacheKey, json, options, cancellationToken);
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            return GetTimeoutResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ChatOrchestrator");
            return GetErrorResponse(ex);
        }
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        string userMessage,
        List<ChatMessage>? conversationHistory = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Currently returning a full response in one stream event.
        // We will integrate ILLMService.GetStreamCompletionWithToolsAsync later.
        var response = await ProcessMessageAsync(userMessage, conversationHistory, cancellationToken);
        yield return $"data: {JsonSerializer.Serialize(response, _jsonOptions)}\n\n";
    }

    // =========================================================================
    // Agent loop — LLM decides tool usage, backend executes, LLM synthesizes
    // =========================================================================

    private async Task<ChatResponse> RunAgentLoopAsync(
        List<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        for (int step = 1; step <= MaxAgentSteps; step++)
        {
            _logger.LogInformation("Agent step {Step}/{Max}", step, MaxAgentSteps);

            // Call LLM with tool definitions — LLM decides whether to call tools
            var llmResult = await _llmService.GetCompletionWithToolsAsync(
                messages, _systemPrompt, _toolRegistry.GetToolDefinitions(), cancellationToken);

            // Case 1: LLM produced a final text response (no tool calls)
            if (llmResult.StopReason == "end_turn" || llmResult.ToolCalls == null || !llmResult.ToolCalls.Any())
            {
                return ParseFinalResponse(llmResult.Content, llmResult.Model);
            }

            // Case 2: LLM wants to call tools — execute them (in parallel)
            _logger.LogInformation("LLM requested {Count} tool(s): {Names}",
                llmResult.ToolCalls!.Count,
                string.Join(", ", llmResult.ToolCalls.Select(t => t.Function.Name)));

            // Append LLM's assistant turn (with tool_use blocks) to history
            messages.Add(new ChatMessage { Role = "assistant", ContentObject = llmResult.RawAssistantContent });

            // Execute all tool calls in parallel
            var toolResults = await ExecuteToolCallsAsync(llmResult.ToolCalls, cancellationToken);

            // Append tool results in standard OpenAI format
            foreach (var r in toolResults)
            {
                var contentJson = JsonSerializer.Serialize(new
                {
                    tool_call_id = r.ToolUseId,
                    name = r.Name,
                    content = r.Result
                }, _jsonOptions);

                messages.Add(new ChatMessage
                {
                    Role = "tool",
                    Content = contentJson
                });
            }

            // Loop continues — LLM will now synthesize using tool results
        }

        _logger.LogWarning("Agent loop reached max steps ({Max}) without final response", MaxAgentSteps);
        return new ChatResponse
        {
            Type        = "error",
            Text        = "⚠️ AI không thể tạo phản hồi sau nhiều bước. Vui lòng thử lại với câu hỏi cụ thể hơn.",
            Suggestions = DefaultSuggestions()
        };
    }

    // =========================================================================
    // Tool execution
    // =========================================================================

    private async Task<List<ToolCallResult>> ExecuteToolCallsAsync(
        List<ToolCall> toolCalls,
        CancellationToken cancellationToken)
    {
        // Execute all tools in parallel for speed
        var tasks = toolCalls.Select(async call =>
        {
            _logger.LogInformation("Executing tool: {Name} with args: {Args}", call.Function.Name, call.Function.Arguments);

            try
            {
                var result = await _toolRegistry.ExecuteToolAsync(call.Function.Name, call.Function.Arguments, cancellationToken);
                return new ToolCallResult { ToolUseId = call.Id, Name = call.Function.Name, Result = result, IsError = false };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tool execution failed: {Name}", call.Function.Name);
                var errorJson = JsonSerializer.Serialize(new { error = $"Tool '{call.Function.Name}' thất bại: {ex.Message}" });
                return new ToolCallResult { ToolUseId = call.Id, Name = call.Function.Name, Result = errorJson, IsError = true };
            }
        });

        return (await Task.WhenAll(tasks)).ToList();
    }



    // =========================================================================
    // Response parsing
    // =========================================================================

    private ChatResponse ParseFinalResponse(string content, string? modelName = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ChatResponse
            {
                Type        = "error",
                Text        = "❌ AI không trả về nội dung hợp lệ. Vui lòng thử lại.",
                Suggestions = DefaultSuggestions()
            };
        }

        try
        {
            // Strip markdown fences that LLM sometimes adds despite instructions
            var cleaned = StripMarkdownFences(content);
            var jsonStr = ExtractJsonFromResponse(cleaned);

            if (jsonStr == null)
            {
                // Plain text response — wrap it gracefully into a response object
                _logger.LogInformation("LLM returned plain text instead of JSON. Wrapping response.");
                return new ChatResponse
                {
                    Type        = "info",
                    Text        = content.Trim(),
                    Suggestions = DefaultSuggestions(),
                    Model       = modelName
                };
            }

            var response = JsonSerializer.Deserialize<ChatResponse>(jsonStr, _jsonOptions);

            if (response == null)
            {
                return new ChatResponse
                {
                    Type        = "error",
                    Text        = "❌ AI trả về dữ liệu không hợp lệ. Vui lòng thử lại.",
                    Suggestions = DefaultSuggestions()
                };
            }

            // Set model info
            response.Model = modelName;

            // Ensure required fields
            if (string.IsNullOrWhiteSpace(response.Text))
                response.Text = "AI chưa trả về phần diễn giải.";

            if (response.Suggestions == null || !response.Suggestions.Any())
                response.Suggestions = DefaultSuggestions();

            return response;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON from LLM response. Treating as plain text.");

            // Graceful fallback: show text instead of an error
            return new ChatResponse
            {
                Type        = "info",
                Text        = content.Trim(),
                Suggestions = DefaultSuggestions()
            };
        }
    }

    private static string? ExtractJsonFromResponse(string content)
    {
        var jsonStart = content.IndexOf('{');
        var jsonEnd   = content.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd > jsonStart)
            return content.Substring(jsonStart, jsonEnd - jsonStart + 1);

        return null;
    }

    private static string StripMarkdownFences(string content)
    {
        var s = content.Trim();

        if (s.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            s = s["```json".Length..].TrimStart();
        else if (s.StartsWith("```"))
            s = s["```".Length..].TrimStart();

        if (s.EndsWith("```"))
            s = s[..^"```".Length].TrimEnd();

        return s;
    }

    // =========================================================================
    // Error responses
    // =========================================================================

    private static ChatResponse GetAiDisabledResponse() => new()
    {
        Type        = "error",
        Text        = "⚠️ AI hiện đang bị tắt trong cấu hình hệ thống nên chưa thể tạo phản hồi phân tích.",
        Suggestions = new List<string> { "Bật lại UseAI trong cấu hình" }
    };

    private static ChatResponse GetTimeoutResponse() => new()
    {
        Type        = "error",
        Text        = "⏱️ Yêu cầu bị huỷ hoặc hết thời gian chờ. Vui lòng thử lại.",
        Suggestions = DefaultSuggestions()
    };

    private ChatResponse GetErrorResponse(Exception ex)
    {
        var message = ex.Message ?? string.Empty;

        // Account-level daily quota exhausted — no models will work until tomorrow
        if (ContainsAny(message, "free-models-per-day", "giới hạn miễn phí", "daily-quota"))
        {
            return new ChatResponse
            {
                Type        = "error",
                Text        = "⚠️ Đã hết giới hạn miễn phí OpenRouter trong ngày (50 request/ngày). " +
                              "Vui lòng thêm credits tại https://openrouter.ai/credits hoặc đợi đến ngày mai.",
                Suggestions = new List<string> { "Thêm credits OpenRouter", "Đợi đến ngày mai", "Kiểm tra quota API" }
            };
        }

        // Per-model rate limit or transient 429
        if (ContainsAny(message, "429", "ratelimit", "rate limit", "rate-limited", "toomanyrequests"))
        {
            return new ChatResponse
            {
                Type        = "error",
                Text        = "⚠️ API đang bị giới hạn tốc độ. Hệ thống đã thử lại nhiều lần nhưng không thành công. Vui lòng thử lại sau ít phút.",
                Suggestions = new List<string> { "Thử lại sau ít phút", "Kiểm tra quota API", "Dùng model khác" }
            };
        }

        return new ChatResponse
        {
            Type        = "error",
            Text        = "❌ Xin lỗi, đã xảy ra lỗi khi xử lý yêu cầu. Vui lòng thử lại.",
            Suggestions = DefaultSuggestions()
        };
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static List<string> DefaultSuggestions() =>
        new() { "Xem xu hướng doanh thu", "So sánh khu vực", "Tổng quan KPI" };

    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}

// ─── Supporting types (add to DTOs if preferred) ─────────────────────────────

public class ToolCallResult
{
    public string ToolUseId { get; set; } = string.Empty;
    public string Name      { get; set; } = string.Empty;
    public string Result    { get; set; } = string.Empty;
    public bool   IsError   { get; set; }
}