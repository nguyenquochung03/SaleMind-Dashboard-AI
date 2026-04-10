using Newtonsoft.Json;

namespace Infrastructure.Models.External;

public class DummyJsonProductDto
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("category")]
    public string Category { get; set; } = string.Empty;

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("discountPercentage")]
    public decimal DiscountPercentage { get; set; }

    [JsonProperty("rating")]
    public decimal Rating { get; set; }

    [JsonProperty("stock")]
    public int Stock { get; set; }

    [JsonProperty("brand")]
    public string Brand { get; set; } = string.Empty;
}