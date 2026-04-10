using Infrastructure.Models.External;
using Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Infrastructure.Clients;

public class ExternalSalesApiClient : IExternalSalesApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExternalSalesApiClient> _logger;
    private readonly ExternalSalesApiOptions _options;

    public ExternalSalesApiClient(
        HttpClient httpClient,
        ILogger<ExternalSalesApiClient> logger,
        IOptions<ExternalSalesApiOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public Task<DummyJsonProductsResponse> GetProductsAsync(CancellationToken cancellationToken = default)
        => GetAsync<DummyJsonProductsResponse>(_options.ProductsEndpoint, "products", cancellationToken);

    public Task<DummyJsonCartsResponse> GetCartsAsync(CancellationToken cancellationToken = default)
        => GetAsync<DummyJsonCartsResponse>(_options.CartsEndpoint, "carts", cancellationToken);

    public Task<DummyJsonUsersResponse> GetUsersAsync(CancellationToken cancellationToken = default)
        => GetAsync<DummyJsonUsersResponse>(_options.UsersEndpoint, "users", cancellationToken);

    private async Task<TResponse> GetAsync<TResponse>(
        string endpoint,
        string resourceName,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException($"External sales API endpoint for '{resourceName}' is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

        try
        {
            _logger.LogInformation("Requesting external sales API resource {ResourceName} from {Endpoint}", resourceName, endpoint);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "External sales API returned non-success status code {StatusCode} for {ResourceName}. Response: {ResponseBody}",
                    (int)response.StatusCode,
                    resourceName,
                    responseContent);

                throw new HttpRequestException(
                    $"External sales API returned status code {(int)response.StatusCode} when requesting '{resourceName}'.");
            }

            var result = JsonConvert.DeserializeObject<TResponse>(responseContent);

            if (result is null)
            {
                _logger.LogError(
                    "Failed to deserialize external sales API response for {ResourceName}. Raw response: {ResponseBody}",
                    resourceName,
                    responseContent);

                throw new InvalidOperationException(
                    $"External sales API response for '{resourceName}' could not be deserialized.");
            }

            return result;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Timed out while requesting external sales API resource {ResourceName} from {Endpoint}",
                resourceName,
                endpoint);

            throw new TimeoutException(
                $"Timed out while requesting external sales API resource '{resourceName}'.",
                ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Invalid JSON returned from external sales API for {ResourceName} from {Endpoint}",
                resourceName,
                endpoint);

            throw new InvalidOperationException(
                $"External sales API returned invalid JSON for '{resourceName}'.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error while requesting external sales API resource {ResourceName} from {Endpoint}",
                resourceName,
                endpoint);

            throw;
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            _logger.LogError(
                ex,
                "Unexpected error while requesting external sales API resource {ResourceName} from {Endpoint}",
                resourceName,
                endpoint);

            throw new InvalidOperationException(
                $"Unexpected error occurred while requesting external sales API resource '{resourceName}'.",
                ex);
        }
    }
}