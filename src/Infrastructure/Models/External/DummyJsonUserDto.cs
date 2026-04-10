using Newtonsoft.Json;

namespace Infrastructure.Models.External;

public class DummyJsonUserDto
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [JsonProperty("lastName")]
    public string LastName { get; set; } = string.Empty;

    [JsonProperty("email")]
    public string Email { get; set; } = string.Empty;

    [JsonProperty("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonProperty("address")]
    public DummyJsonAddressDto? Address { get; set; }
}

public class DummyJsonAddressDto
{
    [JsonProperty("address")]
    public string Address { get; set; } = string.Empty;

    [JsonProperty("city")]
    public string City { get; set; } = string.Empty;

    [JsonProperty("state")]
    public string State { get; set; } = string.Empty;

    [JsonProperty("stateCode")]
    public string StateCode { get; set; } = string.Empty;

    [JsonProperty("postalCode")]
    public string PostalCode { get; set; } = string.Empty;

    [JsonProperty("country")]
    public string Country { get; set; } = string.Empty;
}