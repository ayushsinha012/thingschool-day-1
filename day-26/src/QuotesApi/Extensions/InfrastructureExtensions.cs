using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi.Application.Quotes;
using QuotesApi.Authentication;
using QuotesApi.Authorization;
using QuotesApi.Caching;
using QuotesApi.Data;
using QuotesApi.Messaging;
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

        // Idle-session timeout, login lockout, and auth rate-limit
        // thresholds - see AuthSecurityOptions for what each one does.
        services.Configure<AuthSecurityOptions>(
            configuration.GetSection(AuthSecurityOptions.SectionName));

        services.AddAuthRateLimiting(configuration);

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

        // Day 19: Service Bus publisher + two competing-consumer subscription
        // workers - see MessagingExtensions for what each piece is for.
        services.AddMessaging(configuration);

        // Day 20: transactional outbox relay - see OutboxExtensions for
        // what each piece is for.
        services.AddOutbox(configuration);

        return services;
    }

    /// <summary>
    /// Per-IP throttling for the auth endpoints (login/register/refresh/
    /// logout) - the "auth" policy referenced by
    /// <c>[EnableRateLimiting("auth")]</c> on AuthController. This is the
    /// first line of defense against brute-force/credential-stuffing: it
    /// caps request volume from one IP before a single request even
    /// reaches the per-account lockout logic in AuthController, so an
    /// attacker can't dodge that lockout by spraying many different email
    /// addresses. Read once at startup from AuthSecurityOptions -
    /// unlike the options pattern used elsewhere, the rate limiter's
    /// partition factory captures these as plain values rather than
    /// IOptions, since the limiter is wired up before the DI container
    /// that would resolve them exists.
    /// </summary>
    private static IServiceCollection AddAuthRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(AuthSecurityOptions.SectionName)
            .Get<AuthSecurityOptions>() ?? new AuthSecurityOptions();

        services.AddRateLimiter(limiterOptions =>
        {
            limiterOptions.OnRejected = (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    options.RateLimitWindowSeconds.ToString();

                return ValueTask.CompletedTask;
            };

            limiterOptions.AddPolicy("auth", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    // Partition key: the caller's IP - so the limit is
                    // per-attacker, not one shared bucket for every user.
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString()
                        ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.RateLimitPermitsPerWindow,
                        Window = TimeSpan.FromSeconds(options.RateLimitWindowSeconds),
                        QueueLimit = 0
                    }));
        });

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
        var serviceName = configuration["ApplicationInsights:CloudRoleName"] ?? "quotes-api";

        var openTelemetry = services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddSource(MessagingTelemetry.SourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation())
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

    public static bool IsAzureMonitorConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(ResolveAppInsightsConnectionString(configuration));

    /// <summary>
    /// Resolves the Application Insights connection string from configuration first
    /// (so it can be sourced from Key Vault or any other configuration provider wired
    /// into IConfiguration), then falls back to the conventional
    /// APPLICATIONINSIGHTS_CONNECTION_STRING environment variable that Azure App
    /// Service / Azure Monitor tooling sets automatically. Returns null/empty when
    /// unset - callers must treat that as "Azure Monitor disabled", never as an error.
    /// </summary>
    private static string? ResolveAppInsightsConnectionString(IConfiguration configuration)
    {
        var fromConfigKey = configuration[AppInsightsConnectionStringKey];

        if (!string.IsNullOrWhiteSpace(fromConfigKey))
        {
            return fromConfigKey;
        }

        var fromEnvKey = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

        if (!string.IsNullOrWhiteSpace(fromEnvKey))
        {
            return fromEnvKey;
        }

        return Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
    }
}
