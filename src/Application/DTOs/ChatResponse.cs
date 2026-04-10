namespace Application.DTOs;

/// <summary>
/// Structured response from AI chatbot.
/// Follows the JSON schema defined in AGENTS.md: type, text, data, chart.
/// </summary>
public class ChatResponse
{
    public string Type { get; set; } = "analysis";
    public string Text { get; set; } = string.Empty;
    public List<Dictionary<string, object>>? Data { get; set; }
    public ChartConfig? Chart { get; set; }
    public List<string>? Suggestions { get; set; }
    public string? Model { get; set; }
}
