using Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Tools;

/// <summary>
/// Tool to analyze pipeline distribution and order status
/// </summary>
public class AnalyzePipelineTool : ITool
{
    private readonly ISalesService _salesService;
    private readonly ILogger<AnalyzePipelineTool> _logger;

    public string Name => "analyze_pipeline";
    public string Description => "Analyze the distribution of orders by status (success, processing, pending, failed) in the sales pipeline";

    public AnalyzePipelineTool(
        ISalesService salesService,
        ILogger<AnalyzePipelineTool> logger)
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
                includeRevenue = new
                {
                    type = "boolean",
                    description = "Mặc định luôn là true để bao gồm dữ liệu doanh thu thay vì hỏi lại người dùng.",
                    @default = true
                }
            }
        };
    }

    public async Task<object> ExecuteAsync(Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var includeRevenue = arguments.GetValueOrDefault("includeRevenue")?.ToString() == "true";

            _logger.LogInformation("AnalyzePipelineTool executing with includeRevenue: {IncludeRevenue}", includeRevenue);

            var pipelineData = await _salesService.GetPipelineDistributionAsync();

            // Transform data for LLM consumption
            var result = pipelineData
                .OrderByDescending(p => p.OrderCount)
                .Select(p => new
                {
                    status = p.Status,
                    orderCount = p.OrderCount,
                    revenue = includeRevenue ? Math.Round(p.Revenue, 2) : (decimal?)null
                })
                .ToList();

            var totalOrders = result.Sum(r => r.orderCount);
            
            return new
            {
                success = true,
                data = result,
                count = result.Count,
                summary = new
                {
                    totalOrders = totalOrders,
                    successRate = totalOrders > 0 
                        ? Math.Round((result.FirstOrDefault(r => r.status == "Thành công")?.orderCount ?? 0) * 100.0 / totalOrders, 1)
                        : 0,
                    failureRate = totalOrders > 0 
                        ? Math.Round((result.FirstOrDefault(r => r.status == "Thất bại")?.orderCount ?? 0) * 100.0 / totalOrders, 1)
                        : 0,
                    statuses = result.Select(r => new
                    {
                        name = r.status,
                        count = r.orderCount,
                        percentage = totalOrders > 0 ? Math.Round(r.orderCount * 100.0 / totalOrders, 1) : 0
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AnalyzePipelineTool");
            return new { success = false, error = ex.Message };
        }
    }
}