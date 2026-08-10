using OrderRefactor.Models;

namespace OrderRefactor.Strategies;

public class PremiumCustomerPricingStrategy : IOrderPricingStrategy
{
    public bool CanHandle(Order order)
    {
        return order.CustomerName.Contains("Premium", StringComparison.OrdinalIgnoreCase);
    }

    public decimal Apply(decimal total, Order order)
    {
        return total * 0.95m;
    }
}