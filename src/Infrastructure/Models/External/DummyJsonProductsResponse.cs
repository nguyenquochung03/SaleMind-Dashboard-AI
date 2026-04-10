using Newtonsoft.Json;

namespace Infrastructure.Models.External;

public class DummyJsonProductsResponse
{
    [JsonProperty("products")]
    public List<DummyJsonProductDto> Products { get; set; } = [];

    [JsonProperty("total")]
    public int Total { get; set; }

    [JsonProperty("skip")]
    public int Skip { get; set; }

    [JsonProperty("limit")]
    public int Limit { get; set; }
}