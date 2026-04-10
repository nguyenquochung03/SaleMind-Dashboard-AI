using Newtonsoft.Json;

namespace Infrastructure.Models.External;

public class DummyJsonCartDto
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("products")]
    public List<DummyJsonCartProductDto> Products { get; set; } = [];

    [JsonProperty("total")]
    public decimal Total { get; set; }

    [JsonProperty("discountedTotal")]
    public decimal DiscountedTotal { get; set; }

    [JsonProperty("userId")]
    public int UserId { get; set; }

    [JsonProperty("totalProducts")]
    public int TotalProducts { get; set; }

    [JsonProperty("totalQuantity")]
    public int TotalQuantity { get; set; }
}