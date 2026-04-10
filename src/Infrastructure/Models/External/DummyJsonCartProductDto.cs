using Newtonsoft.Json;

namespace Infrastructure.Models.External;

public class DummyJsonCartProductDto
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("quantity")]
    public int Quantity { get; set; }

    [JsonProperty("total")]
    public decimal Total { get; set; }

    [JsonProperty("discountPercentage")]
    public decimal DiscountPercentage { get; set; }

    [JsonProperty("discountedTotal")]
    public decimal DiscountedTotal { get; set; }
}