using OrderRefactor.Models;

namespace OrderRefactor.Strategies;

public class OrderPricingStrategyProcessor
{
    private readonly IEnumerable<IOrderPricingStrategy> _strategies;

    public OrderPricingStrategyProcessor(
        IEnumerable<IOrderPricingStrategy> strategies)
    {
        _strategies = strategies;
    }

    public decimal ApplyStrategies(decimal total, Order order)
    {
        foreach (var strategy in _strategies)
        {
            if (strategy.CanHandle(order))
            {
                total = strategy.Apply(total, order);
            }
        }

        return total;
    }
}