using Domain.Models;

namespace Application.Interfaces;

/// <summary>
/// Service interface for sales data operations.
/// Implementations may use mock data or real external APIs.
/// </summary>
public interface ISalesService
{
    Task<KpiSummary> GetKpiSummaryAsync(string? range = null);
    Task<List<SalesData>> GetSalesDataAsync(string? range = null);
    Task<List<SalesData>> GetRegionPerformanceAsync(string? range = null);
    Task<List<SalesData>> GetPipelineDistributionAsync(string? range = null);
    Task<List<SalesData>> GetCategoryPerformanceAsync(string? range = null);
    Task<List<SalesData>> GetTopProductsAsync(string? range = null);
}
