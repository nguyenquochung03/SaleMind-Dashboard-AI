namespace Application.Interfaces;

/// <summary>
/// Interface for Tool Registry - manages available tools for AI
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Get all registered tool definitions for LLM
    /// </summary>
    List<ToolDefinition> GetToolDefinitions();

    /// <summary>
    /// Execute a tool by name with provided arguments
    /// </summary>
    /// <param name="toolName">Name of the tool to execute</param>
    /// <param name="arguments">JSON string of arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tool execution result as JSON</returns>
    Task<string> ExecuteToolAsync(string toolName, string arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a tool is registered
    /// </summary>
    bool IsToolRegistered(string toolName);
}

/// <summary>
/// Interface for individual tools
/// </summary>
public interface ITool
{
    /// <summary>
    /// Unique name of the tool
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what the tool does
    /// </summary>
    string Description { get; }

    /// <summary>
    /// JSON schema for the tool's parameters
    /// </summary>
    object GetParametersSchema();

    /// <summary>
    /// Execute the tool with provided arguments
    /// </summary>
    /// <param name="arguments">Parsed arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tool execution result</returns>
    Task<object> ExecuteAsync(Dictionary<string, object> arguments, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result from tool execution
/// </summary>
public class ToolResult
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }
}