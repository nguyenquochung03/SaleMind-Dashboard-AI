using Application.Interfaces;
using Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.Tools;

/// <summary>
/// Tool to get sales data for analysis
/// </summary>
public class GetSalesDataTool : ITool
{
    private readonly ISalesService _salesService;
    private readonly ILogger<GetSalesDataTool> _logger;

    public string Name => "get_sales_data";
    public string Description => "Get sales data including revenue, orders, and trends over a specified time period";

    public GetSalesDataTool(
        ISalesService salesService,
        ILogger<GetSalesDataTool> logger)
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
                    description = "Thời gian báo cáo (VD: '7days', '30days', '6months', '12months'). Nếu không rõ, hãy mặc định là '12months' để có cái nhìn tổng quát.",
                    @default = "12months"
                },
                region = new
                {
                    type = "string",
                    description = "Optional: Filter by specific region"
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

            var region = arguments.GetValueOrDefault("region")?.ToString();

            _logger.LogInformation("GetSalesDataTool executing with normalized range: {Range}, region: {Region}", range, region);

            var salesData = await _salesService.GetSalesDataAsync(range);

            // Transform data for LLM consumption
            var result = salesData.Select(s => new
            {
                date = s.Date,
                revenue = Math.Round(s.Revenue, 2),
                orderCount = s.OrderCount,
                region = s.Region,
                category = s.ProductCategory
            }).ToList();

            return new
            {
                success = true,
                data = result,
                count = result.Count,
                summary = new
                {
                    totalRevenue = Math.Round(salesData.Sum(s => s.Revenue), 2),
                    totalOrders = salesData.Sum(s => s.OrderCount),
                    avgRevenue = salesData.Count > 0 
                        ? Math.Round(salesData.Average(s => s.Revenue), 2) 
                        : 0
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetSalesDataTool");
            return new { success = false, error = ex.Message };
        }
    }
}