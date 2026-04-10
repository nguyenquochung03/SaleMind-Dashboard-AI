using Newtonsoft.Json;

namespace Infrastructure.Models.External;

public class DummyJsonCartsResponse
{
    [JsonProperty("carts")]
    public List<DummyJsonCartDto> Carts { get; set; } = [];

    [JsonProperty("total")]
    public int Total { get; set; }

    [JsonProperty("skip")]
    public int Skip { get; set; }

    [JsonProperty("limit")]
    public int Limit { get; set; }
}