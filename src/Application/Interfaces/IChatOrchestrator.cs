using Application.DTOs;

namespace Application.Interfaces;

/// <summary>
/// Interface for AI Chat Orchestrator - coordinates LLM and tools
/// </summary>
public interface IChatOrchestrator
{
    /// <summary>
    /// Process a user message and return a structured response
    /// </summary>
    /// <param name="userMessage">The user's message</param>
    /// <param name="conversationHistory">Previous conversation messages</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Structured chat response</returns>
    Task<ChatResponse> ProcessMessageAsync(
        string userMessage,
        List<ChatMessage>? conversationHistory = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamMessageAsync(
        string userMessage,
        List<ChatMessage>? conversationHistory = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for the AI Orchestrator
/// </summary>
public class OrchestratorConfig
{
    /// <summary>
    /// Maximum number of tool-calling iterations
    /// </summary>
    public int MaxSteps { get; set; } = 3;

    /// <summary>
    /// Timeout for each step in seconds
    /// </summary>
    public int StepTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Whether to use AI (true) or mock (false)
    /// </summary>
    public bool UseAI { get; set; } = false;
}