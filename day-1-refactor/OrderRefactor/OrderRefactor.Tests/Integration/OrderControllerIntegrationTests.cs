using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrderRefactor.Data;
using OrderRefactor.DTOs;
using OrderRefactor.Models;

namespace OrderRefactor.Tests.Integration;

/// <summary>
/// End-to-end test that boots the real ASP.NET Core pipeline via
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// (using Program's exposed <c>public partial class Program {}</c>) against
/// an isolated in-memory SQLite database, exercising the full
/// Controller -> Service -> Repository -> Strategy -> EF Core path for a
/// single POST /api/orders request.
/// </summary>
public sealed class OrderControllerIntegrationTests : IAsyncLifetime
{
    private readonly OrderApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeDatabaseAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Customers.Add(new Customer
            {
                Id = 1,
                Name = "Test Customer",
                IsBlocked = false,
                IsPremium = false,
                CreditLimit = 100000m,
            });

            await db.SaveChangesAsync();
        }

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();

        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task PostOrder_WithValidRequest_Returns201CreatedWithOrderResponse()
    {
        // Arrange
        var request = new CreateOrderRequest
        {
            CustomerId = 1,
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductName = "Widget", Price = 25m, Quantity = 2 },
            },
        };

        // Act
        var httpResponse = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await httpResponse.Content.ReadFromJsonAsync<OrderResponse>();

        body.Should().NotBeNull();
        body!.OrderId.Should().BeGreaterThan(0);
        body.CustomerId.Should().Be(1);
        body.CustomerName.Should().Be("Test Customer");
        body.Status.Should().NotBeNullOrWhiteSpace();
        body.Total.Should().Be(50m);
        body.Items.Should().ContainSingle()
            .Which.ProductName.Should().Be("Widget");
    }
}
