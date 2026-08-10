using OrderRefactor.Models;

namespace OrderRefactor.Strategies;

public class BulkOrderPricingStrategy : IOrderPricingStrategy
{
    public bool CanHandle(Order order)
    {
        return order.Items.Any(item => item.Quantity > 10);
    }

    public decimal Apply(decimal total, Order order)
    {
        return total * 0.90m;
    }
}