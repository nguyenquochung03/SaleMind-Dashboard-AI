namespace Application.DTOs;

/// <summary>
/// Chart configuration sent from backend to frontend for Chart.js rendering.
/// </summary>
public class ChartConfig
{
    public string Type { get; set; } = string.Empty; // "line", "bar", "doughnut"
    public string XField { get; set; } = string.Empty;
    public string YField { get; set; } = string.Empty;
    public string? Title { get; set; }
}
