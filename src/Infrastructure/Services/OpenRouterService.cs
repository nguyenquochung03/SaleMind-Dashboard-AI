/*
 * Lớp OpenRouterService triển khai giao diện ILLMService để kết nối với API OpenRouter:
 * 1. Giao tiếp AI: Chuyển đổi yêu cầu từ hệ thống (tin nhắn, công cụ) sang định dạng API của OpenRouter 
 *    và gửi đi để nhận phản hồi từ các mô hình ngôn ngữ lớn (LLM).
 * 2. Xử lý lỗi & Thử lại (Retry): Tự động thử lại khi gặp lỗi giới hạn tốc độ (Rate Limit) và 
 *    phối hợp với OpenRouterModelService để chuyển sang mô hình dự phòng nếu mô hình chính bị lỗi.
 * 3. Hỗ trợ Streaming: Cung cấp khả năng truyền tải dữ liệu dạng dòng (Stream) giúp hiển thị câu trả lời 
 *    của AI ngay khi nó đang được tạo ra, mang lại trải nghiệm mượt mà cho người dùng.
 */
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Application.Interfaces;
using Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// OpenRouter LLM service implementation with dynamic model selection
/// </summary>
public class OpenRouterService : ILLMService
{
    private readonly HttpClient _httpClient;
    private readonly OpenRouterOptions _options;
    private readonly IOpenRouterModelService _modelService;
    private readonly ILogger<OpenRouterService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private string? _selectedModel;

    public OpenRouterService(
        HttpClient httpClient,
        IOptions<OpenRouterOptions> options,
        IOpenRouterModelService modelService,
        ILogger<OpenRouterService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _modelService = modelService;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<string> GetCompletionAsync(
        List<ChatMessage> messages,
        string systemPrompt,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestWithRetryAsync(messages, systemPrompt, null, cancellationToken);
        return response.Content;
    }

    public async Task<LLMResponse> GetCompletionWithToolsAsync(
        List<ChatMessage> messages,
        string systemPrompt,
        List<ToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        return await SendRequestWithRetryAsync(messages, systemPrompt, tools, cancellationToken);
    }

    public async IAsyncEnumerable<string> GetStreamCompletionWithToolsAsync(
        List<ChatMessage> messages,
        string systemPrompt,
        List<ToolDefinition> tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var candidateModels = await GetCandidateModelsAsync(cancellationToken);
        Exception? lastException = null;
        var cooldown = TimeSpan.FromMinutes(Math.Max(1, _options.ModelCooldownMinutes));

        foreach (var model in candidateModels)
        {
            var request = BuildRequest(messages, systemPrompt, tools, model);
            request.Stream = true;
            var requestJson = JsonSerializer.Serialize(request, _jsonOptions);

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/completions")
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            HttpResponseMessage? response = null;
            Stream? stream = null;
            StreamReader? reader = null;
            bool connectSuccess = false;
            try
            {
                response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                reader = new StreamReader(stream);
                connectSuccess = true;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _modelService.MarkModelFailure(model, cooldown, "streaming-connection-error");
                response?.Dispose();
                continue;
            }

            if (connectSuccess)
            {
                try
                {
                    while (!reader!.EndOfStream)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var line = await reader.ReadLineAsync();
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        if (line.StartsWith("data: "))
                        {
                            var data = line.Substring(6);
                            if (data == "[DONE]") break;

                            var chunk = JsonSerializer.Deserialize<OpenRouterResponse>(data, _jsonOptions);
                            var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content?.ToString();
                            if (!string.IsNullOrEmpty(content))
                            {
                                yield return content;
                            }
                        }
                    }
                    _modelService.MarkModelSuccess(model);
                }
                finally
                {
                    reader?.Dispose();
                    stream?.Dispose();
                    response?.Dispose();
                }
                yield break;
            }
        }

        throw lastException ?? new InvalidOperationException("Không thể kết nối API AI với các model hiện tại.");
    }

    private async Task<LLMResponse> SendRequestWithRetryAsync(
        List<ChatMessage> messages,
        string systemPrompt,
        List<ToolDefinition>? tools,
        CancellationToken cancellationToken)
    {
        var candidateModels = await GetCandidateModelsAsync(cancellationToken);
        Exception? lastException = null;
        var cooldown = TimeSpan.FromMinutes(Math.Max(1, _options.ModelCooldownMinutes));
        var quotaCooldown = TimeSpan.FromHours(24); // Account-level quota resets daily

        foreach (var model in candidateModels)
        {
            try
            {
                var request = BuildRequest(messages, systemPrompt, tools, model);
                var requestJson = JsonSerializer.Serialize(request, _jsonOptions);

                _logger.LogDebug("Sending request to OpenRouter with model {Model}: {Request}", model, requestJson);

                HttpResponseMessage response = null!;
                string responseJson = string.Empty;
                
                // --- Retry Logic for 429 ---
                int maxRetries = 3;
                TimeSpan delay = TimeSpan.FromSeconds(2);
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/completions")
                    {
                        Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                    };
                    requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    response = await _httpClient.SendAsync(requestMessage, cancellationToken);
                    responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var error = ParseErrorResponse(response, responseJson);
                        
                        // Account-level daily quota exhausted → no point retrying any model
                        if (IsDailyQuotaExhausted(response.StatusCode, error))
                        {
                            _logger.LogWarning(
                                "Daily free-model quota exhausted (account-level). Model: {Model}. Skipping all retries.",
                                model);
                            
                            // Mark ALL candidate models with 24h cooldown
                            foreach (var m in candidateModels)
                                _modelService.MarkModelFailure(m, quotaCooldown, "daily-quota-exhausted");
                            
                            throw new InvalidOperationException(
                                "Đã hết giới hạn miễn phí OpenRouter trong ngày (50 request/ngày). " +
                                "Vui lòng thêm credits tại https://openrouter.ai/credits hoặc đợi đến ngày mai.");
                        }
                        
                        if (attempt == maxRetries) break; // Exhausted retries, proceed to error handling
                        
                        if (IsQuotaExhaustedError(response.StatusCode, error) && _options.FailFastOnQuotaExceeded)
                            break; // Fail fast without retry if per-minute limit reached
                            
                        var retryAfter = response.Headers.RetryAfter?.Delta ?? delay;
                        _logger.LogWarning("Rate limited with 429. Retrying after {Delay}s (Attempt {Attempt}/{Max})", retryAfter.TotalSeconds, attempt, maxRetries);
                        
                        await Task.Delay(retryAfter, cancellationToken);
                        delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
                        continue; // try again
                    }
                    else
                    {
                        break; // Success or non-retryable error
                    }
                }
                // ---------------------------

                var contentType = response.Content.Headers.ContentType?.MediaType;

                _logger.LogDebug("OpenRouter response content-type: {ContentType}", contentType);
                _logger.LogDebug("OpenRouter response (first 500 chars): {Response}",
                    responseJson.Length > 500 ? responseJson.Substring(0, 500) + "..." : responseJson);

                if (!string.IsNullOrEmpty(responseJson) && responseJson.TrimStart().StartsWith("<"))
                {
                    _logger.LogError(
                        "OpenRouter returned HTML instead of JSON. Endpoint: {Endpoint}. Status: {StatusCode}. Content-Type: {ContentType}. Response: {Response}",
                        "/api/v1/chat/completions",
                        response.StatusCode,
                        contentType,
                        responseJson.Length > 200 ? responseJson.Substring(0, 200) : responseJson);

                    _modelService.MarkModelFailure(model, cooldown, "html-response");

                    throw new InvalidOperationException(
                        $"OpenRouter API returned HTML instead of JSON from endpoint '/api/v1/chat/completions' (Status: {response.StatusCode}, Content-Type: {contentType ?? "unknown"}). " +
                        "Kiểm tra lại BaseUrl/OpenRouter endpoint hoặc reverse proxy đang trả về trang HTML thay vì JSON.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = ParseErrorResponse(response, responseJson);

                    _logger.LogError(
                        "OpenRouter API error with model {Model}: {StatusCode} - {Response}",
                        model,
                        response.StatusCode,
                        responseJson);

                    var failFastOnQuotaExceeded = _options.FailFastOnQuotaExceeded &&
                                                  IsQuotaExhaustedError(response.StatusCode, error);

                        _modelService.MarkModelFailure(
                            model,
                            cooldown,
                            $"{(int)response.StatusCode}:{error.Code}:{error.Message}");

                    if (failFastOnQuotaExceeded)
                    {
                        _modelService.MarkModelFailure(
                            model,
                            cooldown,
                            $"{(int)response.StatusCode}:{error.Code}:{error.Message}");

                        _logger.LogWarning(
                            "Detected account-level quota exhaustion on model {Model}. Stop probing more models for this request.",
                            model);

                        throw new InvalidOperationException(error.Message);
                    }

                    if (ShouldTryNextModel(response.StatusCode, error))
                    {
                        _modelService.MarkModelFailure(
                            model,
                            cooldown,
                            $"{(int)response.StatusCode}:{error.Code}:{error.Message}");

                        _logger.LogWarning(
                            "Model {Model} is temporarily unavailable or rate limited. Trying next model.",
                            model);

                        lastException = new InvalidOperationException(error.Message);
                        continue;
                    }

                    throw new InvalidOperationException(error.Message);
                }

                var openRouterResponse = JsonSerializer.Deserialize<OpenRouterResponse>(responseJson, _jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize OpenRouter response");

                _selectedModel = model;
                _modelService.MarkModelSuccess(model);
                return ParseResponse(openRouterResponse);
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, "HTTP error calling OpenRouter API with model {Model}", model);
                _modelService.MarkModelFailure(model, cooldown, "http-request-exception");
                lastException = new InvalidOperationException(
                    "Không thể kết nối đến dịch vụ AI. Vui lòng thử lại sau.",
                    httpEx);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "JSON parsing error from OpenRouter API with model {Model}", model);
                _modelService.MarkModelFailure(model, cooldown, "json-parse-error");
                lastException = new InvalidOperationException(
                    "Phản hồi từ AI không hợp lệ. Vui lòng thử lại.",
                    jsonEx);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling OpenRouter API with model {Model}", model);
                lastException = ex;

                if (ex is InvalidOperationException && model != candidateModels.Last())
                {
                    _modelService.MarkModelFailure(model, cooldown, "invalid-operation");
                    continue;
                }

                break;
            }
        }

        throw lastException ?? new InvalidOperationException("Không thể xử lý yêu cầu AI với các model hiện có.");
    }

    private async Task<List<string>> GetCandidateModelsAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<string>();
        var maxCandidates = Math.Max(3, _options.ProbeTopFreeModels);

        void AddCandidate(string? model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return;
            }

            if (!candidates.Contains(model, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(model);
            }
        }

        AddCandidate(_selectedModel);
        AddCandidate(_options.PreferredModel);

        if (_options.AutoSelectModel)
        {
            var freeModels = await _modelService.GetRankedFreeModelsAsync(cancellationToken);

            foreach (var model in freeModels
                         .Where(m => !m.Contains("vision", StringComparison.OrdinalIgnoreCase))
                         .Take(maxCandidates))
            {
                AddCandidate(model);
            }
        }

        if (candidates.Count == 0)
        {
            candidates.Add("stepfun/step-3-5-flash:free");
        }

        var finalCandidates = candidates.Take(maxCandidates).ToList();
        _logger.LogInformation(
            "OpenRouter candidate models (probing sequentially until one succeeds): {Models}",
            string.Join(", ", finalCandidates));

        return finalCandidates;
    }

    private OpenRouterRequest BuildRequest(
        List<ChatMessage> messages,
        string systemPrompt,
        List<ToolDefinition>? tools,
        string model)
    {
        var openRouterMessages = new List<OpenRouterMessage>
        {
            new OpenRouterMessage { Role = "system", Content = systemPrompt }
        };

        openRouterMessages.AddRange(messages.Select(m => 
        {
            // Case 1: Already an OpenRouterMessage (from a previous assistant turn)
            if (m.ContentObject is OpenRouterMessage orMsg)
            {
                // Gemini/strict compliance: Ensure content is never null
                if (orMsg.Content == null)
                {
                    orMsg.Content = "";
                }
                return orMsg;
            }

            // Case 2: Raw assistant turn as JsonElement
            if (m.Role == "assistant" && m.ContentObject is JsonElement jsonElement)
            {
                var orMsgConverted = JsonSerializer.Deserialize<OpenRouterMessage>(jsonElement.GetRawText(), _jsonOptions);
                if (orMsgConverted != null)
                {
                    // Gemini/strict compliance: assistant messages with tool_calls should have a text part even if empty
                    if (orMsgConverted.ToolCalls != null && orMsgConverted.ToolCalls.Any() && string.IsNullOrEmpty(orMsgConverted.Content?.ToString()))
                    {
                        orMsgConverted.Content = " ";
                    }
                    return orMsgConverted;
                }
                return new OpenRouterMessage { Role = "assistant", Content = m.Content };
            }

            var msg = new OpenRouterMessage { Role = m.Role };

            // Case 3: Explicit tool result message (OpenAI format)
            if (m.Role == "tool")
            {
                try
                {
                    var doc = JsonDocument.Parse(m.Content ?? "{}");
                    if (doc.RootElement.TryGetProperty("tool_call_id", out var toolCallIdProp))
                        msg.ToolCallId = toolCallIdProp.GetString();
                    if (doc.RootElement.TryGetProperty("name", out var nameProp))
                        msg.Name = nameProp.GetString();
                    if (doc.RootElement.TryGetProperty("content", out var contentProp))
                        msg.Content = contentProp.ValueKind == JsonValueKind.String ? contentProp.GetString() : contentProp.GetRawText();
                    return msg;
                }
                catch
                {
                    msg.Content = m.Content;
                }
            }

            // Case 4: Content already provides structured object (e.g., tool_result array for Anthropic)
            if (m.ContentObject != null)
            {
                msg.Content = m.ContentObject;
            }
            // Case 5: Content string is JSON (fallback for history stored as strings)
            else if (m.Content?.StartsWith("[") == true || m.Content?.StartsWith("{") == true)
            {
                try
                {
                    msg.Content = JsonSerializer.Deserialize<object>(m.Content, _jsonOptions);
                }
                catch
                {
                    msg.Content = m.Content;
                }
            }
            // Case 6: Plain text content (ensure never empty for Gemini/Llama compliance)
            else
            {
                msg.Content = string.IsNullOrEmpty(m.Content) ? " " : m.Content;
            }
            
            return msg;
        }));

        var request = new OpenRouterRequest
        {
            Model = model,
            Messages = openRouterMessages,
            MaxTokens = _options.MaxTokens,
            Temperature = _options.Temperature,
            SiteUrl = _options.SiteUrl,
            SiteName = _options.SiteName
        };

        if (tools != null && tools.Count > 0)
        {
            request.Tools = tools.Select(t => new OpenRouterTool
            {
                Type = t.Type,
                Function = new OpenRouterToolFunction
                {
                    Name = t.Function.Name,
                    Description = t.Function.Description,
                    Parameters = t.Function.Parameters
                }
            }).ToList();
            request.ToolChoice = "auto";
        }

        return request;
    }

    private OpenRouterParsedError ParseErrorResponse(HttpResponseMessage response, string responseJson)
    {
        var defaultMessage = $"OpenRouter API error: {(int)response.StatusCode} {response.StatusCode}";
        string? errorCode = null;
        string? rawMetadata = null;

        try
        {
            if (!string.IsNullOrEmpty(responseJson))
            {
                var errorResponse = JsonSerializer.Deserialize<OpenRouterErrorResponse>(responseJson, _jsonOptions);
                if (errorResponse?.Error != null)
                {
                    defaultMessage = !string.IsNullOrWhiteSpace(errorResponse.Error.Message)
                        ? $"OpenRouter API error: {errorResponse.Error.Message}"
                        : defaultMessage;
                    errorCode = errorResponse.Error.Code;
                    rawMetadata = errorResponse.Error.Metadata?.Raw;
                }
            }
        }
        catch
        {
            // Ignore parsing errors for error responses
        }

        return new OpenRouterParsedError
        {
            Message = defaultMessage,
            Code = errorCode,
            RawMetadata = rawMetadata
        };
    }

    private static bool ShouldTryNextModel(HttpStatusCode statusCode, OpenRouterParsedError error)
    {
        if (statusCode == HttpStatusCode.TooManyRequests ||
            statusCode == HttpStatusCode.Forbidden)
        {
            return true;
        }

        if (statusCode == HttpStatusCode.NotFound ||
            statusCode == HttpStatusCode.BadGateway ||
            statusCode == HttpStatusCode.ServiceUnavailable ||
            statusCode == HttpStatusCode.GatewayTimeout)
        {
            return true;
        }

        var combinedError = $"{error.Message} {error.RawMetadata} {error.Code}";
        return combinedError.Contains("rate-limited", StringComparison.OrdinalIgnoreCase) ||
               combinedError.Contains("temporarily", StringComparison.OrdinalIgnoreCase) ||
               combinedError.Contains("provider returned error", StringComparison.OrdinalIgnoreCase) ||
               combinedError.Contains("no endpoints found", StringComparison.OrdinalIgnoreCase) ||
               combinedError.Contains("busy", StringComparison.OrdinalIgnoreCase) ||
               combinedError.Contains("forbidden", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Detects account-level DAILY quota exhaustion (free-models-per-day).
    /// This means ALL free models are blocked until the next day — no point trying other models.
    /// </summary>
    private static bool IsDailyQuotaExhausted(HttpStatusCode statusCode, OpenRouterParsedError error)
    {
        if (statusCode != HttpStatusCode.TooManyRequests) return false;

        var combinedError = $"{error.Message} {error.RawMetadata} {error.Code}";
        return combinedError.Contains("free-models-per-day", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Detects any rate-limit / quota error (per-minute, per-day, etc).
    /// </summary>
    private static bool IsQuotaExhaustedError(HttpStatusCode statusCode, OpenRouterParsedError error)
    {
        if (statusCode != HttpStatusCode.TooManyRequests)
        {
            return false;
        }

        var combinedError = $"{error.Message} {error.RawMetadata} {error.Code}";
        return combinedError.Contains("free-models-per-day", StringComparison.OrdinalIgnoreCase) ||
               combinedError.Contains("free-models-per-min", StringComparison.OrdinalIgnoreCase) ||
               combinedError.Contains("rate limit exceeded", StringComparison.OrdinalIgnoreCase) ||
               combinedError.Contains("retry_after_seconds", StringComparison.OrdinalIgnoreCase) ||
               combinedError.Contains("x-ratelimit-remaining\":\"0", StringComparison.OrdinalIgnoreCase) ||
               combinedError.Contains("x-ratelimit-remaining\":\"0\"", StringComparison.OrdinalIgnoreCase);
    }

    private LLMResponse ParseResponse(OpenRouterResponse response)
    {
        var choice = response.Choices?.FirstOrDefault()
            ?? throw new InvalidOperationException("No choices in OpenRouter response");

        var llmResponse = new LLMResponse
        {
            Content = choice.Message?.Content?.ToString() ?? string.Empty,
            Model = response.Model,
            TotalTokens = response.Usage?.TotalTokens ?? 0,
            StopReason = choice.FinishReason == "tool_calls" ? "tool_use" : "end_turn",
            RawAssistantContent = choice.Message // Allows agent loop to send it back
        };

        if (choice.Message?.ToolCalls != null && choice.Message.ToolCalls.Count > 0)
        {
            llmResponse.ToolCalls = choice.Message.ToolCalls.Select(tc => new Application.Interfaces.ToolCall
            {
                Id = tc.Id,
                Type = tc.Type,
                Function = new Application.Interfaces.ToolCallFunction
                {
                    Name = tc.Function?.Name ?? string.Empty,
                    Arguments = tc.Function?.Arguments ?? string.Empty
                }
            }).ToList();
        }

        return llmResponse;
    }
}

#region Request/Response Models

/**
 * HỆ THỐNG DỮ LIỆU OPENROUTER - Cấu trúc gồm 3 nhóm chính:
 * 
 * NHÓM A: YÊU CẦU (Request) - "Phiếu gửi hàng"
 * - OpenRouterRequest: Tờ đơn tổng quát chứa thông tin gửi đi (Model, Messages, Tools...).
 * - OpenRouterMessage: Nội dung lá thư (User hoặc Assistant).
 * - OpenRouterTool: Bản mô tả kỹ năng/công cụ AI có thể sử dụng.
 * 
 * NHÓM B: PHẢN HỒI (Response) - "Hàng nhận về"
 * - OpenRouterResponse: Thùng hàng AI gửi lại (chứa Id, Model...).
 * - OpenRouterChoice: Các phương án trả lời trong thùng hàng.
 * - OpenRouterToolCall: Lệnh từ AI yêu cầu thực thi công cụ.
 * - OpenRouterUsage: Hóa đơn tiền cước (Token usage).
 * 
 * NHÓM C: SỰ CỐ (Error) - "Biên bản thất lạc"
 * - OpenRouterError: Chi tiết lỗi từ API.
 * - OpenRouterParsedError: Bản tóm tắt lỗi đã dịch sang ngôn ngữ dễ hiểu.
 * 
 * ví dụ thực tế tại Ecotech:
 * 1. Hùng gửi Request: "Doanh thu miền Nam tháng này?" + Tool [get_revenue].
 * 2. AI gửi Response có ToolCall: "Hãy chạy get_revenue(region='South')".
 * 3. Hùng chạy SQL (ra 5 tỷ) và gửi Message (Role: tool) lại cho AI.
 * 4. AI gửi Response cuối cùng: "Doanh thu miền Nam là 5 tỷ VND, thưa anh Hùng!".
 */

/// <summary>
/// NHÓM A - Request: "Phiếu gửi hàng" - Tờ đơn tổng quát chứa mọi thông tin gửi cho AI
/// </summary>
internal class OpenRouterRequest
{
    public string Model { get; set; } = string.Empty;
    public List<OpenRouterMessage> Messages { get; set; } = new();
    public int MaxTokens { get; set; }
    public double Temperature { get; set; }
    public string? SiteUrl { get; set; }
    public string? SiteName { get; set; }
    public List<OpenRouterTool>? Tools { get; set; }
    public string? ToolChoice { get; set; }
    public bool? Stream { get; set; }
}

/// <summary>
/// NHÓM A - Message: Nội dung lá thư. Có thể là lời của User hoặc lời của Assistant (AI)
/// </summary>
internal class OpenRouterMessage
{
    public string Role { get; set; } = string.Empty;
    public object? Content { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }
    
    public string? Name { get; set; }
    
    public List<OpenRouterToolCall>? ToolCalls { get; set; }
}

/// <summary>
/// NHÓM A - Tool: "Bản mô tả kỹ năng". Thông báo cho AI những công cụ/hàm mà nó có thể mượn dùng
/// </summary>
internal class OpenRouterTool
{
    public string Type { get; set; } = "function";
    public OpenRouterToolFunction Function { get; set; } = new();
}

internal class OpenRouterToolFunction
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public object? Parameters { get; set; }
}

/// <summary>
/// NHÓM B - Response: "Hàng nhận về" - Thùng hàng AI gửi lại sau khi xử lý
/// </summary>
internal class OpenRouterResponse
{
    public string Id { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public List<OpenRouterChoice>? Choices { get; set; }
    public OpenRouterUsage? Usage { get; set; }
}

/// <summary>
/// NHÓM B - Choice: Các phương án trả lời. AI có thể gửi nhiều phương án nhưng thường chỉ lấy 1
/// </summary>
internal class OpenRouterChoice
{
    public int Index { get; set; }
    public OpenRouterMessage? Message { get; set; }
    public OpenRouterMessage? Delta { get; set; }
    public string? FinishReason { get; set; }
}

/// <summary>
/// NHÓM B - ToolCall: Tờ lệnh từ AI - "Tôi không làm được, bạn hãy chạy công cụ này giúp tôi"
/// </summary>
internal class OpenRouterToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "function";
    public OpenRouterToolCallFunction? Function { get; set; }
}

internal class OpenRouterToolCallFunction
{
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}

/// <summary>
/// NHÓM B - Usage: "Hóa đơn tiền cước" - Cho biết số lượng Token (giấy mực) đã tiêu tốn
/// </summary>
internal class OpenRouterUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

internal class OpenRouterErrorResponse
{
    public OpenRouterError? Error { get; set; }
}

/// <summary>
/// NHÓM C - Error: "Biên bản thất lạc" - Mô tả chi tiết tại sao yêu cầu thất bại (hết tiền, sai địa chỉ...)
/// </summary>
internal class OpenRouterError
{
    public string? Message { get; set; }
    public string? Type { get; set; }
    public string? Code { get; set; }
    public OpenRouterErrorMetadata? Metadata { get; set; }
}

internal class OpenRouterErrorMetadata
{
    public string? Raw { get; set; }
}

/// <summary>
/// NHÓM C - ParsedError: Bản tóm tắt lỗi đã dịch sang tiếng người cho dễ hiểu
/// </summary>
internal class OpenRouterParsedError
{
    public string Message { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? RawMetadata { get; set; }
}

#endregion
