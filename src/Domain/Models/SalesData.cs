namespace Domain.Models;

/// <summary>
/// Represents a single sales data record from external APIs.
/// </summary>
public class SalesData
{
    public string Date { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public string Region { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductCategory { get; set; } = string.Empty;
}
