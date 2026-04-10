namespace Application.Interfaces;

/// <summary>
/// Interface for LLM service (OpenRouter, OpenAI, etc.)
/// </summary>
public interface ILLMService
{
    /// <summary>
    /// Send a message to the LLM and get a response
    /// </summary>
    /// <param name="messages">List of conversation messages</param>
    /// <param name="systemPrompt">System prompt to guide the AI</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>LLM response text</returns>
    Task<string> GetCompletionAsync(
        List<ChatMessage> messages,
        string systemPrompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a message to the LLM with tool calling support
    /// </summary>
    /// <param name="messages">List of conversation messages</param>
    /// <param name="systemPrompt">System prompt to guide the AI</param>
    /// <param name="tools">Available tools for the AI to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>LLM response with potential tool calls</returns>
    Task<LLMResponse> GetCompletionWithToolsAsync(
        List<ChatMessage> messages,
        string systemPrompt,
        List<ToolDefinition> tools,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Stream a response with potential tool calls
    /// </summary>
    IAsyncEnumerable<string> GetStreamCompletionWithToolsAsync(
        List<ChatMessage> messages,
        string systemPrompt,
        List<ToolDefinition> tools,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a chat message in the conversation
/// </summary>
public class ChatMessage
{
    public string Role { get; set; } = "user"; // "system", "user", "assistant"
    public string Content { get; set; } = string.Empty;
    public object? ContentObject { get; set; }
}

/// <summary>
/// Response from LLM with potential tool calls
/// </summary>
public class LLMResponse
{
    public string Content { get; set; } = string.Empty;
    public List<ToolCall>? ToolCalls { get; set; }
    public string? Model { get; set; }
    public int TotalTokens { get; set; }
    public string StopReason { get; set; } = string.Empty;
    public object? RawAssistantContent { get; set; }
}

/// <summary>
/// Represents a tool call from the LLM
/// </summary>
public class ToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "function";
    public ToolCallFunction Function { get; set; } = new();
}

/// <summary>
/// Function details in a tool call
/// </summary>
public class ToolCallFunction
{
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}

/// <summary>
/// Definition of a tool available to the LLM
/// </summary>
public class ToolDefinition
{
    public string Type { get; set; } = "function";
    public FunctionDefinition Function { get; set; } = new();
}

/// <summary>
/// Function definition for a tool
/// </summary>
public class FunctionDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public object? Parameters { get; set; }
}