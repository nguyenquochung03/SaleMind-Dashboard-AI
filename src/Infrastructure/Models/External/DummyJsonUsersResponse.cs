using Newtonsoft.Json;

namespace Infrastructure.Models.External;

public class DummyJsonUsersResponse
{
    [JsonProperty("users")]
    public List<DummyJsonUserDto> Users { get; set; } = [];

    [JsonProperty("total")]
    public int Total { get; set; }

    [JsonProperty("skip")]
    public int Skip { get; set; }

    [JsonProperty("limit")]
    public int Limit { get; set; }
}