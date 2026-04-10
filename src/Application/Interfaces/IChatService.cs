using Application.DTOs;

namespace Application.Interfaces;

/// <summary>
/// Service interface for chat/AI interactions.
/// Backend orchestrates the flow: User → LLM → Tools → Response.
/// </summary>
public interface IChatService
{
    Task<ChatResponse> ProcessMessageAsync(string message);
}
