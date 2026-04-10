namespace Domain.Models;

/// <summary>
/// Aggregated KPI summary for dashboard display.
/// </summary>
public class KpiSummary
{
    public decimal TotalRevenue { get; set; }
    public string FormattedRevenue { get; set; } = string.Empty;
    public int TotalOrders { get; set; }
    public double ConversionRate { get; set; }
    public decimal AvgDealSize { get; set; }
    public string FormattedAvgDealSize { get; set; } = string.Empty;

    /// <summary>
    /// Percentage change compared to previous period (e.g., +15.2 means 15.2% increase)
    /// </summary>
    public double RevenueChange { get; set; }
    public double OrdersChange { get; set; }
    public double ConversionChange { get; set; }
    public double AvgDealChange { get; set; }
}
