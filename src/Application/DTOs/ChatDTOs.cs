using Application.Interfaces;

namespace Application.DTOs;

public class ChatMessageRequest
{
    public string Message { get; set; } = string.Empty;
    public List<ChatMessage>? History { get; set; }
}
