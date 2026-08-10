using OrderRefactor.Models;

namespace OrderRefactor.Strategies;

public interface IOrderPricingStrategy
{
    bool CanHandle(Order order);

    decimal Apply(decimal total, Order order);
}