using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using QuotesApi.Application.Quotes;
using QuotesApi.Authentication;
using QuotesApi.Authorization;
using QuotesApi.Caching;
using QuotesApi.Data;
using QuotesApi.Middleware;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    /// <summary>
    /// Configuration key (and, as a fallback, environment variable) that supplies the
    /// Azure Application Insights connection string. It is intentionally read from
    /// configuration only - never hard-coded - and is treated as optional: when it is
    /// absent, Azure Monitor export is simply not attached, and the app starts and runs
    /// normally with no Azure dependency.
    /// </summary>
    private const string AppInsightsConnectionStringKey = "ApplicationInsights:ConnectionString";

    public const string DevCorsPolicyName = "AllowFrontendDev";

    public const string ProdCorsPolicyName = "AllowFrontendProd";

    /// <summary>
    /// Configuration key for the deployed frontend's origin(s) (e.g. the Azure Static
    /// Web App URL and/or its custom domain). Read as an explicit origin allow-list -
    /// never a wildcard - so it is empty (nothing cross-origin allowed) until the real
    /// Static Web App URL is known and configured, at which point it is added here
    /// (via appsettings/environment variable, not hard-coded in source).
    /// </summary>
    private const string CorsProductionOriginsKey = "Cors:ProductionOrigins";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddControllers();

        // Local Angular dev servers (ng serve) run on their own origin, so the
        // browser blocks their requests to this API without an explicit CORS
        // policy. Each exercise's app picks its own port (4200, 4201, 4202, ...)
        // when run alongside others, so any localhost origin is allowed rather
        // than a fixed list. Scoped to Development only in Program.cs - never
        // applied to the deployed container.
        services.AddCors(options =>
        {
            options.AddPolicy(DevCorsPolicyName, policy =>
                policy
                    .SetIsOriginAllowed(origin =>
                        Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                        (uri.Host == "localhost" || uri.Host == "127.0.0.1"))
                    .AllowAnyHeader()
                    .AllowAnyMethod());

            // Production policy for the deployed container: an explicit origin
            // allow-list sourced from configuration (Cors:ProductionOrigins), not a
            // wildcard. Until the Static Web App is created and its URL/custom domain
            // added there, this list is empty and no cross-origin browser call is
            // allowed - which is the correct, honest state to be in, not a fake origin.
            var productionOrigins = configuration
                .GetSection(CorsProductionOriginsKey)
                .Get<string[]>() ?? [];

            options.AddPolicy(ProdCorsPolicyName, policy =>
                policy
                    .WithOrigins(productionOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod());
        });

        // Day 21: registers CacheMetrics/QueryCountingInterceptor used
        // below, plus HybridCache (L1 in-memory + L2 Redis) - see
        // CacheExtensions.
        services.AddQuoteCaching(configuration);

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
            options
                .UseSqlite(
                    configuration.GetConnectionString("DefaultConnection")
                    ?? "Data Source=quotes.db")
                // Counts every DB command actually sent, for Day 21's DB
                // load measurements - see QueryCountingInterceptor.
                .AddInterceptors(
                    serviceProvider.GetRequiredService<QueryCountingInterceptor>()));

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        services.AddMediatR(mediatrConfiguration =>
            mediatrConfiguration.RegisterServicesFromAssemblyContaining<CreateQuoteCommand>());

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

        // Backs the /health endpoint mapped in Program.cs. Checks the real
        // dependency (can we reach the database?) rather than always
        // returning healthy - a DB outage should show up here, not just as
        // 500s on the quote endpoints.
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>();

        services.AddObservability(configuration);

        // Day 18: queued BackgroundService + Hangfire - see
        // BackgroundJobsExtensions for what each piece is for.
        services.AddBackgroundJobs();

        // Day 19: Service Bus topic publisher + competing-consumer
        // subscription workers - see MessagingExtensions.
        services.AddMessaging(configuration);

        // Day 20: transactional outbox relay - see OutboxExtensions.
        services.AddOutbox(configuration);

        return services;
    }

    /// <summary>
    /// Wires up the OpenTelemetry tracing/metrics pipeline (ASP.NET Core + HttpClient
    /// instrumentation) that feeds both the existing Serilog TraceId correlation and,
    /// when configured, Azure Application Insights.
    ///
    /// Azure Monitor export is attached only when a connection string is actually
    /// present in configuration/environment. With no connection string:
    ///   - the OpenTelemetry pipeline still runs (so Activity/TraceId correlation with
    ///     Serilog keeps working locally and in CI),
    ///   - no Azure Monitor exporter is registered, so nothing attempts to reach Azure,
    ///   - startup, console logging, and existing behavior are unaffected.
    /// </summary>
    private static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = ResolveAppInsightsConnectionString(configuration);

        var openTelemetry = services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            openTelemetry.UseAzureMonitor(options =>
                options.ConnectionString = connectionString);
        }

        return services;
    }

    /// <summary>
    /// Resolves the Application Insights connection string from configuration first
    /// (so it can be sourced from Key Vault or any other configuration provider wired
    /// into IConfiguration), then falls back to the conventional
    /// APPLICATIONINSIGHTS_CONNECTION_STRING environment variable that Azure App
    /// Service / Azure Monitor tooling sets automatically. Returns null/empty when
    /// unset - callers must treat that as "Azure Monitor disabled", never as an error.
    /// </summary>
    private static string? ResolveAppInsightsConnectionString(IConfiguration configuration) =>
        configuration[AppInsightsConnectionStringKey]
        ?? configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
        ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
}
