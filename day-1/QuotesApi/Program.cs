using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Endpoints;
using QuotesApi.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IQuoteRepository, QuoteRepository>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.MapQuoteEndpoints();

app.Run();