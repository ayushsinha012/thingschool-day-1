using OrderRefactor.Models;
using OrderRefactor.Repositories;

namespace OrderRefactor.Tests.TestDoubles;

/// <summary>
/// Hand-written in-memory test double for <see cref="IOrderRepository"/>.
/// Holds only what OrderService tests need: the ability to seed a customer
/// and their prior orders, and a record of orders that were actually
/// persisted via AddOrderAsync. No database.
/// </summary>
public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly Dictionary<int, Customer> _customersById = new();
    private readonly Dictionary<int, List<Order>> _ordersByCustomerId = new();
    private int _nextOrderId = 1;

    public List<Order> AddedOrders { get; } = new();

    public void SeedCustomer(Customer customer)
    {
        _customersById[customer.Id] = customer;
    }

    public void SeedCustomerOrders(int customerId, List<Order> orders)
    {
        _ordersByCustomerId[customerId] = orders;
    }

    public Task<Customer?> GetCustomerAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        _customersById.TryGetValue(customerId, out var customer);

        return Task.FromResult(customer);
    }

    public Task<List<Order>> GetCustomerOrdersAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        _ordersByCustomerId.TryGetValue(customerId, out var orders);

        return Task.FromResult(orders ?? new List<Order>());
    }

    public Task<Order> AddOrderAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        order.Id = _nextOrderId++;

        AddedOrders.Add(order);

        return Task.FromResult(order);
    }

    public Task<Order?> GetOrderAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = AddedOrders.FirstOrDefault(o => o.Id == orderId);

        return Task.FromResult(order);
    }
}
