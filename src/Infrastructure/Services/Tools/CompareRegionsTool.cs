using Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Tools;

/// <summary>
/// Tool to compare sales performance across regions
/// </summary>
public class CompareRegionsTool : ITool
{
    private readonly ISalesService _salesService;
    private readonly ILogger<CompareRegionsTool> _logger;

    public string Name => "compare_regions";
    public string Description => "Compare sales performance and revenue across different geographic regions";

    public CompareRegionsTool(
        ISalesService salesService,
        ILogger<CompareRegionsTool> logger)
    {
        _salesService = salesService;
        _logger = logger;
    }

    public object GetParametersSchema()
    {
        return new
        {
            type = "object",
            properties = new
            {
                range = new
                {
                    type = "string",
                    description = "Thời gian so sánh (VD: '7days', '30days', '6months', '12months'). Nếu không rõ, mặc định là '12months' để có cái nhìn tổng quát.",
                    @default = "12months"
                },
                metrics = new
                {
                    type = "array",
                    description = "Metrics to compare (revenue, orders, conversion)",
                    items = new { type = "string" }
                }
            }
        };
    }

    public async Task<object> ExecuteAsync(Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var rawRange = arguments.GetValueOrDefault("range")?.ToString() ?? "30days";
            
            // Normalize the range string to handle variations
            var range = rawRange.ToLower().Replace(" ", "").Replace("_", "");
            if (range.Contains("ngày")) range = range.Replace("ngày", "days");
            if (range.Contains("tháng") || range.Contains("thang")) range = range.Replace("tháng", "months").Replace("thang", "months");
            
            if (range.Contains("1months") || range == "1month") range = "30days";
            if (!new[] { "7days", "30days", "6months", "12months" }.Contains(range))
            {
                range = "12months"; // Default fallback
            }

            _logger.LogInformation("CompareRegionsTool executing with range: {Range}", range);

            var regionData = await _salesService.GetRegionPerformanceAsync();

            // Transform data for LLM consumption
            var result = regionData
                .OrderByDescending(r => r.Revenue)
                .Select(r => new
                {
                    region = r.Region,
                    revenue = Math.Round(r.Revenue, 2),
                    orderCount = r.OrderCount,
                    category = r.ProductCategory
                })
                .ToList();

            var totalRevenue = result.Sum(r => r.revenue);
            
            return new
            {
                success = true,
                data = result,
                count = result.Count,
                summary = new
                {
                    totalRevenue = Math.Round(totalRevenue, 2),
                    topRegion = result.FirstOrDefault()?.region,
                    topRegionRevenue = result.FirstOrDefault()?.revenue,
                    regions = result.Select(r => new
                    {
                        name = r.region,
                        percentage = totalRevenue > 0 ? Math.Round((r.revenue / totalRevenue) * 100, 1) : 0
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CompareRegionsTool");
            return new { success = false, error = ex.Message };
        }
    }
}