using Microsoft.EntityFrameworkCore;
using OrderRefactor.Data;
using OrderRefactor.Models;

namespace OrderRefactor.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _db;

    public OrderRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Customer?> GetCustomerAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        return await _db.Customers
            .FirstOrDefaultAsync(
                customer => customer.Id == customerId,
                cancellationToken);
    }

    public async Task<List<Order>> GetCustomerOrdersAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        return await _db.Orders
            .Where(order => order.CustomerId == customerId)
            .ToListAsync(cancellationToken);
    }

    public async Task<Order> AddOrderAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        _db.Orders.Add(order);

        await _db.SaveChangesAsync(cancellationToken);

        return order;
    }

    public async Task<Order?> GetOrderAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        return await _db.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(
                order => order.Id == orderId,
                cancellationToken);
    }
}