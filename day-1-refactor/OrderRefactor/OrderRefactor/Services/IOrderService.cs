using OrderRefactor.DTOs;

namespace OrderRefactor.Services;

public interface IOrderService
{
    Task<OrderResponse> CreateOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken);
}