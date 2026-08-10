using OrderRefactor.Models;

namespace OrderRefactor.Repositories;

public interface IOrderRepository
{
    Task<Customer?> GetCustomerAsync(
        int customerId,
        CancellationToken cancellationToken);

    Task<List<Order>> GetCustomerOrdersAsync(
        int customerId,
        CancellationToken cancellationToken);

    Task<Order> AddOrderAsync(
        Order order,
        CancellationToken cancellationToken);

    Task<Order?> GetOrderAsync(
        int orderId,
        CancellationToken cancellationToken);
}
