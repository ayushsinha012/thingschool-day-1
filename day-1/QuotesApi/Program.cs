using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Endpoints;
using QuotesApi.Repositories;
using QuotesApi.Services;

var builder = WebApplication.CreateBuilder(args);

// MVC Controllers
builder.Services.AddControllers();

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=quotes.db"));

// Scoped services
builder.Services.AddScoped<IQuoteRepository, QuoteRepository>();
builder.Services.AddScoped<ICollectionRepository, CollectionRepository>();

// Singleton service
builder.Services.AddSingleton<IClock, SystemClock>();

// Transient service
builder.Services.AddTransient<QuoteFormatter>();

var app = builder.Build();

// Apply pending EF Core migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    db.Database.Migrate();
}

// Existing Quote endpoints
app.MapQuoteEndpoints();

// Collection controller endpoints
app.MapControllers();

app.Run();

// Allows WebApplicationFactory integration tests later.
public partial class Program
{
}