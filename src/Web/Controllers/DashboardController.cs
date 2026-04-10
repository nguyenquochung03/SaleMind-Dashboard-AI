using Application.Interfaces;
using Application.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Web.Controllers;

public class DashboardController : Controller
{
    private readonly ISalesService _salesService;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(ISalesService salesService, ILogger<DashboardController> logger)
    {
        _salesService = salesService;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        try
        {
            var viewModel = new DashboardViewModel
            {
                Kpi = await _salesService.GetKpiSummaryAsync(),
                SalesTrend = await _salesService.GetSalesDataAsync(),
                RegionPerformance = await _salesService.GetRegionPerformanceAsync(),
                PipelineDistribution = await _salesService.GetPipelineDistributionAsync()
            };
            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dashboard data");
            return View(new DashboardViewModel());
        }
    }

    [HttpGet("api/dashboard/kpi")]
    public async Task<IActionResult> GetKpiSummaryData([FromQuery] string? range)
    {
        try
        {
            var kpi = await _salesService.GetKpiSummaryAsync(range);
            return Json(kpi);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching KPI data");
            return StatusCode(500, new { error = "Không thể tải dữ liệu KPI" });
        }
    }

    [HttpGet("api/dashboard/revenue")]
    public async Task<IActionResult> GetSalesData([FromQuery] string? range)
    {
        try
        {
            var data = await _salesService.GetSalesDataAsync(range);
            return Json(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching sales data");
            return StatusCode(500, new { error = "Không thể tải dữ liệu doanh thu" });
        }
    }

    [HttpGet("api/dashboard/region")]
    public async Task<IActionResult> GetRegionPerformance([FromQuery] string? range)
    {
        try
        {
            var data = await _salesService.GetRegionPerformanceAsync(range);
            return Json(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching region data");
            return StatusCode(500, new { error = "Không thể tải dữ liệu khu vực" });
        }
    }

    [HttpGet("api/dashboard/pipeline")]
    public async Task<IActionResult> GetPipelineDistribution([FromQuery] string? range)
    {
        try
        {
            var data = await _salesService.GetPipelineDistributionAsync(range);
            return Json(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pipeline data");
            return StatusCode(500, new { error = "Không thể tải dữ liệu pipeline" });
        }
    }

    [HttpGet("api/dashboard/categories")]
    public async Task<IActionResult> GetCategoryPerformance([FromQuery] string? range)
    {
        try
        {
            var data = await _salesService.GetCategoryPerformanceAsync(range);
            return Json(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching category data");
            return StatusCode(500, new { error = "Không thể tải dữ liệu danh mục" });
        }
    }

    [HttpGet("api/dashboard/products")]
    public async Task<IActionResult> GetTopProducts([FromQuery] string? range)
    {
        try
        {
            var data = await _salesService.GetTopProductsAsync(range);
            return Json(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching top products data");
            return StatusCode(500, new { error = "Không thể tải dữ liệu sản phẩm" });
        }
    }
}
