using Domain.Models;

namespace Application.ViewModels;

/// <summary>
/// Aggregate ViewModel for the Dashboard page.
/// Combines KPI, sales trend, region, and pipeline data.
/// </summary>
public class DashboardViewModel
{
    public KpiSummary Kpi { get; set; } = new();
    public List<SalesData> SalesTrend { get; set; } = new();
    public List<SalesData> RegionPerformance { get; set; } = new();
    public List<SalesData> PipelineDistribution { get; set; } = new();
}
