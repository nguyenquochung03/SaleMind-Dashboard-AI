namespace Infrastructure.Options;

public class ExternalSalesApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ProductsEndpoint { get; set; } = string.Empty;
    public string CartsEndpoint { get; set; } = string.Empty;
    public string UsersEndpoint { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; }
}