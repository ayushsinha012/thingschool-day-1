using OrderRefactor.DTOs;
using OrderRefactor.Models;
using OrderRefactor.Repositories;

namespace OrderRefactor.Services;

public class OrderService : IOrderService
{
    private const decimal MaximumOrderAmount = 100000m;
    private const decimal ManagerApprovalAmount = 50000m;
    private const decimal HighValueCustomerLimit = 50000m;

    private readonly IOrderRepository _repository;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository repository,
        ILogger<OrderService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<OrderResponse> CreateOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var customer = await _repository.GetCustomerAsync(
            request.CustomerId,
            cancellationToken);

        if (customer is null)
        {
            throw new InvalidOperationException("Customer not found.");
        }

        if (customer.IsBlocked)
        {
            throw new InvalidOperationException("Customer is blocked.");
        }

        var total = CalculateItemsTotal(request.Items);

        var previousOrders =
            await _repository.GetCustomerOrdersAsync(
                request.CustomerId,
                cancellationToken);

        if (previousOrders.Sum(order => order.TotalAmount) > HighValueCustomerLimit)
        {
            total *= 0.98m;
        }

        if (customer.CreditLimit > 0 &&
            total > customer.CreditLimit)
        {
            throw new InvalidOperationException(
                "Order exceeds customer credit limit.");
        }

        total = ApplyCoupon(total, request.CouponCode);

        if (customer.IsPremium)
        {
            total *= 0.95m;
        }

        if (total > MaximumOrderAmount)
        {
            throw new InvalidOperationException(
                "Order exceeds maximum allowed amount.");
        }

        var order = new Order
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            TotalAmount = total,
            CreatedAt = DateTime.UtcNow,
            Status = total > ManagerApprovalAmount
                ? "ManagerApproval"
                : "Pending"
        };

        foreach (var item in request.Items)
        {
            var orderItem = new OrderItem
            {
                ProductName = item.ProductName,
                Price = item.Price,
                Quantity = item.Quantity,
                Total = CalculateItemTotal(item)
            };

            order.Items.Add(orderItem);
        }

        var savedOrder = await _repository.AddOrderAsync(
            order,
            cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} created for customer {CustomerId}",
            savedOrder.Id,
            customer.Id);

        return MapToResponse(savedOrder);
    }

    private static void ValidateRequest(CreateOrderRequest request)
    {
        if (request.CustomerId <= 0)
        {
            throw new ArgumentException("Customer ID must be greater than zero.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ArgumentException(
                "At least one order item is required.");
        }

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.ProductName))
            {
                throw new ArgumentException(
                    "Product name is required.");
            }

            if (item.Quantity <= 0)
            {
                throw new ArgumentException(
                    "Quantity must be greater than zero.");
            }

            if (item.Price < 0)
            {
                throw new ArgumentException(
                    "Price cannot be negative.");
            }
        }
    }

    private static decimal CalculateItemsTotal(
        IEnumerable<CreateOrderItemRequest> items)
    {
        return items.Sum(CalculateItemTotal);
    }

    private static decimal CalculateItemTotal(
        CreateOrderItemRequest item)
    {
        var total = item.Price * item.Quantity;

        if (item.Quantity > 10)
        {
            total *= 0.90m;
        }

        if (item.Price > 10000)
        {
            total *= 0.95m;
        }

        if (item.ProductName == "VIP")
        {
            total *= 0.90m;
        }

        return total;
    }

    private static decimal ApplyCoupon(
        decimal total,
        string? couponCode)
    {
        return couponCode switch
        {
            "SAVE10" => total * 0.90m,
            "SAVE20" => total * 0.80m,
            _ => total
        };
    }

    private static OrderResponse MapToResponse(Order order)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            CustomerName = order.CustomerName,
            Total = order.TotalAmount,
            Status = order.Status,
            Items = order.Items.Select(item => new OrderItemResponse
            {
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                Price = item.Price,
                Total = item.Total
            }).ToList()
        };
    }
}