using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Authentication;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.Middleware;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddControllers();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(
                configuration.GetConnectionString("DefaultConnection")
                ?? "Data Source=quotes.db"));

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddTransient<QuoteFormatter>();
        services.AddSingleton<JwtTokenService>();

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        services.AddDualJwtAuthentication(configuration);

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                PermissionClaims.CanEditQuotes,
                policy => policy.RequireClaim(
                    PermissionClaims.ClaimType,
                    PermissionClaims.CanEditQuotes));
        });

        services.AddScoped<
            IAuthorizationHandler,
            CollectionOwnershipAuthorizationHandler>();

        return services;
    }
}
