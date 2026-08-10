using Microsoft.EntityFrameworkCore;
using OrderRefactor.Data;
using OrderRefactor.Repositories;
using OrderRefactor.Services;
using OrderRefactor.Strategies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=orders.db"));
    builder.Services.AddScoped<IOrderPricingStrategy, PremiumCustomerPricingStrategy>();
builder.Services.AddScoped<IOrderPricingStrategy, BulkOrderPricingStrategy>();
builder.Services.AddScoped<OrderPricingStrategyProcessor>();

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapControllers();

app.Run();

public partial class Program
{
}
