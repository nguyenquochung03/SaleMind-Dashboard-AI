using Infrastructure.Models.External;

namespace Infrastructure.Clients;

public interface IExternalSalesApiClient
{
    Task<DummyJsonProductsResponse> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<DummyJsonCartsResponse> GetCartsAsync(CancellationToken cancellationToken = default);
    Task<DummyJsonUsersResponse> GetUsersAsync(CancellationToken cancellationToken = default);
}