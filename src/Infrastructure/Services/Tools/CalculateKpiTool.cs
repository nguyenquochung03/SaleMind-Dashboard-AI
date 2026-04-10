using Application.Interfaces;
using Domain.Models;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Tools;

/// <summary>
/// Tool to calculate and retrieve KPI summary
/// </summary>
public class CalculateKpiTool : ITool
{
    private readonly ISalesService _salesService;
    private readonly ILogger<CalculateKpiTool> _logger;

    public string Name => "calculate_kpi";
    public string Description => "Calculate and retrieve key performance indicators (KPIs) including total revenue, orders, conversion rate, and average deal size";

    public CalculateKpiTool(
        ISalesService salesService,
        ILogger<CalculateKpiTool> logger)
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
                includeTrends = new
                {
                    type = "boolean",
                    description = "Mặc định luôn là true để bao gồm % tăng trưởng thay vì hỏi lại người dùng.",
                    @default = true
                }
            }
        };
    }

    public async Task<object> ExecuteAsync(Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var includeTrends = arguments.GetValueOrDefault("includeTrends")?.ToString() == "true";

            _logger.LogInformation("CalculateKpiTool executing with includeTrends: {IncludeTrends}", includeTrends);

            var kpi = await _salesService.GetKpiSummaryAsync();

            var result = new
            {
                success = true,
                data = new
                {
                    totalRevenue = Math.Round(kpi.TotalRevenue, 2),
                    formattedRevenue = kpi.FormattedRevenue,
                    totalOrders = kpi.TotalOrders,
                    conversionRate = Math.Round(kpi.ConversionRate * 100, 1),
                    avgDealSize = Math.Round(kpi.AvgDealSize, 2),
                    formattedAvgDealSize = kpi.FormattedAvgDealSize
                },
                trends = includeTrends ? new
                {
                    revenueChange = Math.Round(kpi.RevenueChange, 1),
                    ordersChange = Math.Round(kpi.OrdersChange, 1),
                    conversionChange = Math.Round(kpi.ConversionChange * 100, 1),
                    avgDealChange = Math.Round(kpi.AvgDealChange, 1)
                } : null,
                insights = GenerateInsights(kpi)
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CalculateKpiTool");
            return new { success = false, error = ex.Message };
        }
    }

    private string GenerateInsights(KpiSummary kpi)
    {
        var insights = new List<string>();

        if (kpi.RevenueChange > 10)
            insights.Add("Doanh thu tăng trưởng mạnh");
        else if (kpi.RevenueChange > 0)
            insights.Add("Doanh thu có xu hướng tăng");
        else if (kpi.RevenueChange < -10)
            insights.Add("Doanh thu giảm đáng kể");
        else
            insights.Add("Doanh thu ổn định");

        if (kpi.ConversionRate > 0.15)
            insights.Add("Tỷ lệ chuyển đổi tốt");
        else if (kpi.ConversionRate < 0.05)
            insights.Add("Cần cải thiện tỷ lệ chuyển đổi");

        return string.Join("; ", insights);
    }
}