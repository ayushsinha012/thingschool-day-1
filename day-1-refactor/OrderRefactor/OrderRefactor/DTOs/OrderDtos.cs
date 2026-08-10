namespace OrderRefactor.DTOs;

public class CreateOrderRequest
{
    public int CustomerId { get; set; }

    public List<CreateOrderItemRequest> Items { get; set; } = [];

    public string? CouponCode { get; set; }
}

public class CreateOrderItemRequest
{
    public string ProductName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int Quantity { get; set; }
}

public class OrderResponse
{
    public int OrderId { get; set; }

    public int CustomerId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public string Status { get; set; } = string.Empty;

    public List<OrderItemResponse> Items { get; set; } = [];
}

public class OrderItemResponse
{
    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal Price { get; set; }

    public decimal Total { get; set; }
}

public class ErrorResponse
{
    public string Message { get; set; } = string.Empty;
}