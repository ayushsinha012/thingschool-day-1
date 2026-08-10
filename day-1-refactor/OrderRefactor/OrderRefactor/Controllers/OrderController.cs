using Microsoft.AspNetCore.Mvc;
using OrderRefactor.DTOs;
using OrderRefactor.Services;

namespace OrderRefactor.Controllers;

[ApiController]
[Route("api/orders")]
public class OrderController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrderController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _orderService.CreateOrderAsync(
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetOrder),
                new { id = result.OrderId },
                result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Message = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message == "Customer not found.")
            {
                return NotFound(new ErrorResponse
                {
                    Message = ex.Message
                });
            }

            return BadRequest(new ErrorResponse
            {
                Message = ex.Message
            });
        }
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> GetOrder(
        int id,
        CancellationToken cancellationToken)
    {
        return NotFound(new ErrorResponse
        {
            Message = "Order lookup is not implemented in this exercise."
        });
    }
}