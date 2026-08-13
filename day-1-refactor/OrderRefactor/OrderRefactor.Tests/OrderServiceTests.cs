using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OrderRefactor.DTOs;
using OrderRefactor.Models;
using OrderRefactor.Services;
using OrderRefactor.Strategies;
using OrderRefactor.Tests.TestDoubles;

namespace OrderRefactor.Tests;

/// <summary>
/// Unit tests for <see cref="OrderService"/> using a hand-written test
/// double for <see cref="OrderRefactor.Repositories.IOrderRepository"/>
/// (no database, no mocking framework). These target real regressions the
/// Controller -> Service -> Repository -> Strategy refactor fixed in the
/// original god-method controller (see REFACTOR-NOTES.md items 12 and 13).
/// </summary>
public class OrderServiceTests
{
    private static OrderService CreateService(InMemoryOrderRepository repository)
    {
        var pricingProcessor = new OrderPricingStrategyProcessor(
            new IOrderPricingStrategy[]
            {
                new PremiumCustomerPricingStrategy(),
                new BulkOrderPricingStrategy(),
            });

        return new OrderService(
            repository,
            pricingProcessor,
            NullLogger<OrderService>.Instance);
    }

    [Fact]
    public async Task CreateOrderAsync_WithBlockedCustomer_ThrowsInvalidOperationException()
    {
        // Arrange
        var repository = new InMemoryOrderRepository();

        repository.SeedCustomer(new Customer
        {
            Id = 1,
            Name = "Blocked Customer",
            IsBlocked = true,
        });

        var service = CreateService(repository);

        var request = new CreateOrderRequest
        {
            CustomerId = 1,
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductName = "Widget", Price = 10m, Quantity = 1 },
            },
        };

        // Act
        var act = async () => await service.CreateOrderAsync(
            request,
            CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Be("Customer is blocked.");
        repository.AddedOrders.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateOrderAsync_WithManyItems_ProcessesEveryItemWithoutIndexOutOfRange()
    {
        // Arrange: the original god-method looped with `i <= request.Items.Count`,
        // which threw when indexing the item collection for the last item.
        // The refactored service must process all N items correctly.
        var repository = new InMemoryOrderRepository();

        repository.SeedCustomer(new Customer
        {
            Id = 2,
            Name = "Regular Customer",
        });

        var service = CreateService(repository);

        var items = Enumerable.Range(1, 5)
            .Select(i => new CreateOrderItemRequest
            {
                ProductName = $"Product-{i}",
                Price = 5m,
                Quantity = 1,
            })
            .ToList();

        var request = new CreateOrderRequest
        {
            CustomerId = 2,
            Items = items,
        };

        // Act
        var response = await service.CreateOrderAsync(
            request,
            CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(5);
        repository.AddedOrders.Should().ContainSingle()
            .Which.Items.Should().HaveCount(5);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(1, -10)]
    public async Task CreateOrderAsync_WithNegativeQuantityOrPrice_ThrowsArgumentException(
        int quantity,
        decimal price)
    {
        // Arrange
        var repository = new InMemoryOrderRepository();

        repository.SeedCustomer(new Customer
        {
            Id = 3,
            Name = "Customer",
        });

        var service = CreateService(repository);

        var request = new CreateOrderRequest
        {
            CustomerId = 3,
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductName = "Widget", Price = price, Quantity = quantity },
            },
        };

        // Act
        var act = async () => await service.CreateOrderAsync(
            request,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        repository.AddedOrders.Should().BeEmpty();
    }
}
