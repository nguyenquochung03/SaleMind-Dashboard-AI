/*
 * Lớp ExternalSalesService triển khai giao diện ISalesService để xử lý dữ liệu bán hàng từ nguồn bên ngoài:
 * 1. Truy vấn: Kết nối với API bên ngoài (ví dụ: DummyJSON) để lấy thông tin về sản phẩm, đơn hàng và người dùng.
 * 2. Phân tích & Hợp nhất: Tổng hợp các dữ liệu thô thành các chỉ số KPI như doanh thu, số lượng đơn hàng, 
 *    hiệu suất theo khu vực và danh mục sản phẩm.
 * 3. Tối ưu hóa: Sử dụng bộ nhớ đệm (Distributed Cache) để lưu trữ kết quả phân tích, giúp tăng tốc độ phản hồi 
 *    và giảm tải cho API bên ngoài.
 */
using Application.Interfaces;
using Domain.Models;
using Infrastructure.Clients;
using Infrastructure.Models.External;
using Infrastructure.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Infrastructure.Services;

public class ExternalSalesService : ISalesService
{
    private const string AnalyticsCacheKey = "external-sales-analytics-v5";
    private static readonly TimeSpan AnalyticsCacheDuration = TimeSpan.FromMinutes(30);

    private readonly IExternalSalesApiClient _externalSalesApiClient;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ExternalSalesService> _logger;
    private readonly ExternalSalesApiOptions _options;

    public ExternalSalesService(
        IExternalSalesApiClient externalSalesApiClient,
        IDistributedCache cache,
        ILogger<ExternalSalesService> logger,
        IOptions<ExternalSalesApiOptions> options)
    {
        _externalSalesApiClient = externalSalesApiClient;
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<KpiSummary> GetKpiSummaryAsync(string? range = null)
    {
        try
        {
            var analytics = await GetAnalyticsAsync();
            var filteredSales = FilterByDateRange(analytics.MonthlySales, range);
            
            // Calculate totals for the current range
            var currentRevenue = filteredSales.Sum(x => x.Revenue);
            var currentOrders = filteredSales.Sum(x => x.OrderCount);
            
            // For changes, we compare against the previous period of same length if available
            int periodLength = filteredSales.Count;
            var allSales = analytics.MonthlySales;
            var previousSales = allSales.Count > periodLength 
                ? allSales.Take(allSales.Count - periodLength).TakeLast(periodLength).ToList()
                : new List<MonthlySalesAggregate>();

            var previousRevenue = previousSales.Sum(x => x.Revenue);
            var previousOrders = previousSales.Sum(x => x.OrderCount);

            // Basic calculation for conversion and deal size based on range
            // (Mocking user count scaling with range)
            var userFactor = (double)periodLength / 12.0;
            var scaledUsers = Math.Max(1, (int)(analytics.TotalUsers * userFactor));
            var conversionRate = scaledUsers == 0 ? 0 : (double)currentOrders / (scaledUsers * 1.5); // Simplified
            var previousConversionRate = scaledUsers == 0 ? 0 : (double)previousOrders / (scaledUsers * 1.5);
            
            var avgDealSize = currentOrders == 0 ? 0m : currentRevenue / currentOrders;
            var previousAvgDealSize = previousOrders == 0 ? 0m : previousRevenue / previousOrders;

            return new KpiSummary
            {
                TotalRevenue = currentRevenue,
                FormattedRevenue = FormatUsd(currentRevenue),
                TotalOrders = currentOrders,
                ConversionRate = Math.Min(0.15, conversionRate), // Cap at 15% for realism
                AvgDealSize = avgDealSize,
                FormattedAvgDealSize = FormatUsd(avgDealSize),
                RevenueChange = CalculatePercentageChange(previousRevenue, currentRevenue),
                OrdersChange = CalculatePercentageChange((decimal)previousOrders, (decimal)currentOrders),
                ConversionChange = CalculatePercentageChange(previousConversionRate, conversionRate),
                AvgDealChange = CalculatePercentageChange(previousAvgDealSize, avgDealSize)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get KPI summary from external API.");
            throw;
        }
    }

    public async Task<List<SalesData>> GetSalesDataAsync(string? dateRange = null)
    {
        try
        {
            var analytics = await GetAnalyticsAsync();
            var filteredData = FilterByDateRange(analytics.MonthlySales, dateRange);

            return filteredData
                .Select(x => new SalesData
                {
                    Date = x.Period,
                    Revenue = x.Revenue,
                    OrderCount = x.OrderCount,
                    Status = "Thành công",
                    Region = "Toàn quốc",
                    ProductCategory = x.TopCategory
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sales data from external API.");
            throw;
        }
    }

    public async Task<List<SalesData>> GetRegionPerformanceAsync(string? range = null)
    {
        try
        {
            var analytics = await GetAnalyticsAsync();
            var filteredItems = FilterRawByRange(analytics.RawItems, range);
            
            return filteredItems
                .GroupBy(x => x.Region)
                .Select(group => new SalesData
                {
                    Region = group.Key,
                    Revenue = group.Sum(x => x.Revenue),
                    OrderCount = group.Count(),
                    Status = "Hoàn thành"
                })
                .OrderByDescending(x => x.Revenue)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get region performance from external API.");
            throw;
        }
    }

    public async Task<List<SalesData>> GetPipelineDistributionAsync(string? range = null)
    {
        try
        {
            var analytics = await GetAnalyticsAsync();
            
            return analytics.PipelineDistribution
                .OrderByDescending(x => x.OrderCount)
                .Select(x => new SalesData
                {
                    Status = x.Status,
                    OrderCount = x.OrderCount,
                    Revenue = x.Revenue,
                    Region = "Toàn quốc"
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pipeline distribution from external API.");
            throw;
        }
    }

    public async Task<List<SalesData>> GetCategoryPerformanceAsync(string? range = null)
    {
        try
        {
            var analytics = await GetAnalyticsAsync();
            var filteredItems = FilterRawByRange(analytics.RawItems, range);

            return filteredItems
                .GroupBy(x => x.PrimaryCategory)
                .Select(group => new SalesData
                {
                    ProductCategory = group.Key,
                    Revenue = group.Sum(x => x.Revenue),
                    OrderCount = group.Count()
                })
                .OrderByDescending(x => x.Revenue)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get category performance from external API.");
            throw;
        }
    }

    public async Task<List<SalesData>> GetTopProductsAsync(string? range = null)
    {
        try
        {
            var analytics = await GetAnalyticsAsync();
            
            // Filter product sales by range
            var latestDate = analytics.ProductSales.Any() ? analytics.ProductSales.Max(x => x.SaleDate) : DateTime.UtcNow;
            var cutoff = range == "byyear" ? DateTime.MinValue : range switch
            {
                "7days" => latestDate.AddDays(-7),
                "30days" => latestDate.AddDays(-30),
                "6months" => latestDate.AddMonths(-6),
                "12months" => latestDate.AddMonths(-12),
                _ => latestDate.AddMonths(-12)
            };

            return analytics.ProductSales
                .Where(x => x.SaleDate >= cutoff)
                .GroupBy(x => x.ProductName)
                .Select(group => new SalesData
                {
                    ProductName = group.Key,
                    Revenue = group.Sum(x => x.Revenue),
                    OrderCount = group.Sum(x => x.OrderCount),
                    ProductCategory = group.First().Category
                })
                .OrderByDescending(x => x.Revenue)
                .Take(5)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get top products from external API.");
            throw;
        }
    }

    private async Task<SalesAnalyticsSnapshot> GetAnalyticsAsync()
    {
        var cachedJson = await _cache.GetStringAsync(AnalyticsCacheKey);
        if (!string.IsNullOrEmpty(cachedJson))
        {
            try 
            {
                var cachedAnalytics = JsonSerializer.Deserialize<SalesAnalyticsSnapshot>(cachedJson);
                if (cachedAnalytics is not null) return cachedAnalytics;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize cached analytics. Refreshing from API.");
            }
        }

        var productsTask = _externalSalesApiClient.GetProductsAsync();
        var cartsTask = _externalSalesApiClient.GetCartsAsync();
        var usersTask = _externalSalesApiClient.GetUsersAsync();

        await Task.WhenAll(productsTask, cartsTask, usersTask);

        var analytics = BuildAnalytics(
            productsTask.Result.Products ?? new List<DummyJsonProductDto>(),
            cartsTask.Result.Carts ?? new List<DummyJsonCartDto>(),
            usersTask.Result.Users ?? new List<DummyJsonUserDto>());

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = AnalyticsCacheDuration
        };

        var json = JsonSerializer.Serialize(analytics);
        await _cache.SetStringAsync(AnalyticsCacheKey, json, options);

        return analytics;
    }

    private SalesAnalyticsSnapshot BuildAnalytics(
        List<DummyJsonProductDto> products,
        List<DummyJsonCartDto> carts,
        List<DummyJsonUserDto> users)
    {
        var productLookup = products.ToDictionary(x => x.Id, x => x);
        var userLookup = users.ToDictionary(x => x.Id, x => x);

        // Generate more carts by creating variations from existing carts
        var expandedCarts = ExpandCarts(carts, products, users);

        var monthlyGroups = expandedCarts
            .Select(cart => CreateCartAnalytics(cart, productLookup, userLookup))
            .ToList();

        var monthlySales = monthlyGroups
            .GroupBy(x => x.CartDate.ToString("yyyy-MM"))
            .OrderBy(x => x.Key)
            .Select(group => new MonthlySalesAggregate
            {
                Period = group.Key,
                Revenue = group.Sum(x => x.Revenue),
                OrderCount = group.Count(),
                TopCategory = group
                    .GroupBy(x => x.PrimaryCategory)
                    .OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key)
                    .Select(x => x.Key)
                    .FirstOrDefault() ?? "Tổng hợp"
            })
            .ToList();

        var regionPerformance = monthlyGroups
            .GroupBy(x => x.Region)
            .Select(group => new RegionSalesAggregate
            {
                Region = group.Key,
                Revenue = group.Sum(x => x.Revenue),
                OrderCount = group.Count(),
                TopCategory = group
                    .GroupBy(x => x.PrimaryCategory)
                    .OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key)
                    .Select(x => x.Key)
                    .FirstOrDefault() ?? "Tổng hợp"
            })
            .ToList();

        var pipelineDistribution = monthlyGroups
            .GroupBy(x => x.Status)
            .Select(group => new PipelineAggregate
            {
                Status = group.Key,
                Revenue = group.Sum(x => x.Revenue),
                OrderCount = group.Count()
            })
            .ToList();

        var categoryPerformance = monthlyGroups
            .GroupBy(x => x.PrimaryCategory)
            .Select(group => new CategorySalesAggregate
            {
                Category = group.Key,
                Revenue = group.Sum(x => x.Revenue),
                OrderCount = group.Count()
            })
            .ToList();

        var productSales = expandedCarts
            .SelectMany(c => (c.Products ?? new List<DummyJsonCartProductDto>()).Select(p => new ProductSaleRecord
            {
                SaleDate = ResolveCartDate(c.Id),
                ProductName = p.Title,
                Revenue = p.Total,
                OrderCount = 1,
                Category = productLookup.TryGetValue(p.Id, out var prod) ? NormalizeCategory(prod.Category) : "Khác"
            }))
            .ToList();

        var topProducts = productSales
            .GroupBy(p => p.ProductName)
            .Select(group => new ProductSalesAggregate
            {
                ProductName = group.Key,
                Revenue = group.Sum(x => x.Revenue),
                OrderCount = group.Sum(x => x.OrderCount),
                Category = group.First().Category
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var totalRevenue = monthlyGroups.Sum(x => x.Revenue);
        var totalOrders = monthlyGroups.Count;
        var completedOrders = monthlyGroups.Count(x => x.Status == "Thành công");
        var latestMonth = monthlySales.LastOrDefault()?.Period;
        var latestMonthOrders = latestMonth is null ? 0 : monthlyGroups.Count(x => x.CartDate.ToString("yyyy-MM") == latestMonth);
        var latestMonthCompletedOrders = latestMonth is null ? 0 : monthlyGroups.Count(x => x.CartDate.ToString("yyyy-MM") == latestMonth && x.Status == "Thành công");

        return new SalesAnalyticsSnapshot
        {
            TotalRevenue = totalRevenue,
            TotalOrders = totalOrders,
            CompletedOrders = completedOrders,
            TotalUsers = users.Count,
            LatestMonthOrders = latestMonthOrders,
            LatestMonthCompletedOrders = latestMonthCompletedOrders,
            RawItems = monthlyGroups, 
            ProductSales = productSales,
            MonthlySales = monthlySales,
            RegionPerformance = regionPerformance,
            PipelineDistribution = pipelineDistribution,
            CategoryPerformance = categoryPerformance,
            TopProducts = topProducts
        };
    }

    private List<DummyJsonCartDto> ExpandCarts(
        List<DummyJsonCartDto> originalCarts,
        List<DummyJsonProductDto> products,
        List<DummyJsonUserDto> users)
    {
        var expanded = new List<DummyJsonCartDto>(originalCarts);
        var random = new Random(42); // Fixed seed for consistent data

        // Generate additional carts by creating variations
        int cartIdCounter = originalCarts.Count + 1;
        
        // For each original cart, create 3-5 variations with different users and products
        foreach (var cart in originalCarts)
        {
            int variations = random.Next(3, 6); // 3-5 variations per cart
            
            for (int i = 0; i < variations; i++)
            {
                var newCart = new DummyJsonCartDto
                {
                    Id = cartIdCounter++,
                    UserId = users[random.Next(0, users.Count)].Id,
                    Products = GetRandomProducts(products, random, 1, 4),
                    Total = CalculateRandomTotal(cart.Total, random),
                    DiscountedTotal = 0,
                    TotalProducts = random.Next(1, 5),
                    TotalQuantity = random.Next(1, 8)
                };
                expanded.Add(newCart);
            }
        }

        return expanded;
    }

    private List<DummyJsonCartProductDto> GetRandomProducts(
        List<DummyJsonProductDto> products, Random random, int minCount, int maxCount)
    {
        var count = random.Next(minCount, maxCount + 1);
        var selectedProducts = new List<DummyJsonCartProductDto>();
        
        for (int i = 0; i < count; i++)
        {
            var product = products[random.Next(0, products.Count)];
            var quantity = random.Next(1, 4);
            selectedProducts.Add(new DummyJsonCartProductDto
            {
                Id = product.Id,
                Title = product.Title,
                Price = product.Price,
                Quantity = quantity,
                Total = product.Price * quantity,
                DiscountPercentage = 0,
                DiscountedTotal = product.Price * quantity
            });
        }
        
        return selectedProducts;
    }

    private decimal CalculateRandomTotal(decimal baseTotal, Random random)
    {
        // Vary the total by -30% to +50%
        var factor = 0.7m + (decimal)(random.NextDouble() * 0.8);
        return Math.Round(baseTotal * factor, 2);
    }

    private CartAnalyticsItem CreateCartAnalytics(
        DummyJsonCartDto cart,
        IReadOnlyDictionary<int, DummyJsonProductDto> productLookup,
        IReadOnlyDictionary<int, DummyJsonUserDto> userLookup)
    {
        var cartDate = ResolveCartDate(cart.Id);
        var items = cart.Products ?? new List<DummyJsonCartProductDto>();

        var revenue = cart.DiscountedTotal > 0
            ? cart.DiscountedTotal
            : items.Sum(item => item.DiscountedTotal > 0 ? item.DiscountedTotal : item.Total);

        var primaryCategory = items
            .Select(item => productLookup.TryGetValue(item.Id, out var product) ? NormalizeCategory(product.Category) : "Tổng hợp")
            .GroupBy(x => x)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key)
            .Select(x => x.Key)
            .FirstOrDefault() ?? "Tổng hợp";

        var productCount = items.Sum(x => x.Quantity);
        var region = userLookup.TryGetValue(cart.UserId, out var user)
            ? MapVietnameseRegion(user.Address?.State)
            : "Khác";

        return new CartAnalyticsItem
        {
            CartDate = cartDate,
            Revenue = revenue,
            Region = region,
            PrimaryCategory = primaryCategory,
            Status = DeterminePipelineStatus(revenue, productCount, primaryCategory, cartDate)
        };
    }

    private static string DeterminePipelineStatus(decimal revenue, int productCount, string category, DateTime cartDate)
    {
        var categorySeed = Math.Abs((category ?? string.Empty).GetHashCode());
        var score = ((int)Math.Round(revenue) + productCount * 17 + cartDate.Day * 11 + cartDate.Month * 7 + categorySeed) % 100;

        return score switch
        {
            >= 75 => "Thành công",
            >= 45 => "Đang xử lý",
            >= 20 => "Chờ duyệt",
            _ => "Thất bại"
        };
    }

    private static List<CartAnalyticsItem> FilterRawByRange(List<CartAnalyticsItem> source, string? range)
    {
        if (string.IsNullOrEmpty(range) || range == "byyear") return source;

        // Use the latest date in the snapshot as "today" for consistent mock results
        var latestDate = source.Any() ? source.Max(x => x.CartDate) : DateTime.UtcNow;
        var cutoff = range switch
        {
            "7days" => latestDate.AddDays(-7),
            "30days" => latestDate.AddDays(-30),
            "6months" => latestDate.AddMonths(-6),
            "12months" => latestDate.AddMonths(-12),
            _ => latestDate.AddMonths(-12)
        };

        return source.Where(x => x.CartDate >= cutoff).ToList();
    }

    private static List<MonthlySalesAggregate> FilterByDateRange(List<MonthlySalesAggregate> source, string? dateRange)
    {
        var months = dateRange switch
        {
            "7days" => 1,
            "30days" => 1,
            "6months" => 6,
            "12months" => 12,
            "byyear" => source.Count, // Show all data for yearly view in this mock
            _ => 12
        };

        return source.TakeLast(Math.Min(months, source.Count)).ToList();
    }


    private static DateTime ResolveCartDate(int cartId)
    {
        var baseMonth = new DateTime(DateTime.UtcNow.Year - 1, DateTime.UtcNow.Month, 1);
        var monthOffset = Math.Max(cartId - 1, 0) % 12;
        var day = Math.Max((cartId % 28) + 1, 1);

        return baseMonth.AddMonths(monthOffset).AddDays(day - 1);
    }

    private static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return "Tổng hợp";
        }

        var normalized = category.Trim().ToLowerInvariant();

        return normalized switch
        {
            "smartphones" or "mobile-accessories" => "Thiết bị di động",
            "laptops" => "Máy tính",
            "groceries" => "Hàng tiêu dùng",
            "fragrances" or "beauty" or "skin-care" => "Chăm sóc cá nhân",
            "furniture" or "home-decoration" or "kitchen-accessories" => "Gia dụng",
            "mens-shirts" or "mens-shoes" or "mens-watches" or "womens-dresses" or "womens-shoes" or "womens-bags" or "womens-jewellery" or "tops" or "sunglasses" => "Thời trang",
            "automotive" or "motorcycle" or "vehicle" => "Phương tiện",
            _ => "Khác"
        };
    }

    private static string MapVietnameseRegion(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return "Khác";
        }

        var normalized = state.Trim().ToLowerInvariant();

        if (normalized.Contains("california") || normalized.Contains("washington") || normalized.Contains("oregon") || normalized.Contains("montana") || normalized.Contains("idaho"))
        {
            return "Miền Bắc";
        }

        if (normalized.Contains("texas") || normalized.Contains("florida") || normalized.Contains("georgia") || normalized.Contains("alabama") || normalized.Contains("louisiana"))
        {
            return "Miền Nam";
        }

        if (normalized.Contains("colorado") || normalized.Contains("utah") || normalized.Contains("arizona") || normalized.Contains("new mexico") || normalized.Contains("nevada"))
        {
            return "Miền Trung";
        }

        if (normalized.Contains("kansas") || normalized.Contains("oklahoma") || normalized.Contains("nebraska") || normalized.Contains("wyoming"))
        {
            return "Tây Nguyên";
        }

        if (normalized.Contains("minnesota") || normalized.Contains("iowa") || normalized.Contains("missouri") || normalized.Contains("arkansas") || normalized.Contains("mississippi"))
        {
            return "Đồng bằng SCL";
        }

        return "Khác";
    }

    private static string FormatUsd(decimal value)
    {
        return string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N0}", value);
    }

    private static double CalculatePercentageChange(decimal previous, decimal current)
    {
        if (previous == 0)
        {
            return current == 0 ? 0 : 100;
        }

        return (double)((current - previous) / previous * 100);
    }

    private static double CalculatePercentageChange(double previous, double current)
    {
        if (Math.Abs(previous) < double.Epsilon)
        {
            return Math.Abs(current) < double.Epsilon ? 0 : 100;
        }

        return (current - previous) / previous * 100;
    }

    private sealed class SalesAnalyticsSnapshot
    {
        public decimal TotalRevenue { get; init; }
        public int TotalOrders { get; init; }
        public int CompletedOrders { get; init; }
        public int TotalUsers { get; init; }
        public int LatestMonthOrders { get; init; }
        public int LatestMonthCompletedOrders { get; init; }
        public List<CartAnalyticsItem> RawItems { get; init; } = new();
        public List<ProductSaleRecord> ProductSales { get; init; } = new();
        public List<MonthlySalesAggregate> MonthlySales { get; init; } = new();
        public List<RegionSalesAggregate> RegionPerformance { get; init; } = new();
        public List<PipelineAggregate> PipelineDistribution { get; init; } = new();
        public List<CategorySalesAggregate> CategoryPerformance { get; init; } = new();
        public List<ProductSalesAggregate> TopProducts { get; init; } = new();
    }

    private sealed class CategorySalesAggregate
    {
        public string Category { get; init; } = string.Empty;
        public decimal Revenue { get; init; }
        public int OrderCount { get; init; }
    }

    private sealed class ProductSalesAggregate
    {
        public string ProductName { get; init; } = string.Empty;
        public decimal Revenue { get; init; }
        public int OrderCount { get; init; }
        public string Category { get; init; } = string.Empty;
    }

    private sealed class MonthlySalesAggregate
    {
        public string Period { get; init; } = string.Empty;
        public decimal Revenue { get; init; }
        public int OrderCount { get; init; }
        public string TopCategory { get; init; } = string.Empty;
    }

    private sealed class RegionSalesAggregate
    {
        public string Region { get; init; } = string.Empty;
        public decimal Revenue { get; init; }
        public int OrderCount { get; init; }
        public string TopCategory { get; init; } = string.Empty;
    }

    private sealed class PipelineAggregate
    {
        public string Status { get; init; } = string.Empty;
        public decimal Revenue { get; init; }
        public int OrderCount { get; init; }
    }

    private sealed class CartAnalyticsItem
    {
        public DateTime CartDate { get; init; }
        public decimal Revenue { get; init; }
        public string Region { get; init; } = string.Empty;
        public string PrimaryCategory { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
    }

    private sealed class ProductSaleRecord
    {
        public DateTime SaleDate { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public decimal Revenue { get; init; }
        public int OrderCount { get; init; }
        public string Category { get; init; } = string.Empty;
    }
}
