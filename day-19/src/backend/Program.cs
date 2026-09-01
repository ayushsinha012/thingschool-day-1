using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Endpoints;
using QuotesApi.Extensions;
using Serilog;
using Serilog.Context;

// The Sqlite ADO.NET provider (Microsoft.Data.Sqlite) executes commands
// synchronously under the hood - "async" EF calls just run the query on
// the calling thread pool thread. On this 4-logical-CPU box, the runtime's
// default ThreadPool minimum (== ProcessorCount) is smaller than the
// concurrency this endpoint is load-tested at (10), so a burst of
// concurrent requests can outrun the default thread-injection rate and
// queue waiting for a new pool thread, inflating tail latency (p95/p99)
// even though each request's own DB work takes single-digit milliseconds.
// Evidence: EF command logging showed ~9ms of total SQLite time per
// request, but ab's p99 stayed >100ms with a max well above the mean -
// the classic thread-pool-starvation tail, not a slow query. Raising the
// minimum removes that ramp-up delay for this endpoint's load profile.
ThreadPool.SetMinThreads(Environment.ProcessorCount * 4, Environment.ProcessorCount * 4);

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext());

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    // Prefer the W3C trace ID from the current OpenTelemetry Activity - the same ID
    // that the ASP.NET Core/HttpClient instrumentation attaches to spans exported to
    // Azure Application Insights - so Serilog's "TraceId" property, this request's
    // trace, and its Application Insights telemetry all share one identifier. Falls
    // back to the ASP.NET Core request identifier if no Activity is present (e.g. no
    // listener is currently sampling), so correlation never breaks.
    var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

    using (LogContext.PushProperty("TraceId", traceId))
    {
        await next();
    }
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    db.Database.Migrate();

    await DbSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseCors(InfrastructureExtensions.DevCorsPolicyName);
}
else
{
    // Explicit-origin allow-list read from Cors:ProductionOrigins (see
    // InfrastructureExtensions.AddInfrastructure) - empty, and therefore blocking all
    // cross-origin browser calls, until the deployed frontend's real origin is known
    // and configured. Never a wildcard.
    app.UseCors(InfrastructureExtensions.ProdCorsPolicyName);
}

app.UseAuthentication();

app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapQuoteEndpoints();

app.MapJobEndpoints();

app.MapMessagingEndpoints();

app.MapControllers();

// Day 18: Hangfire dashboard + the one recurring cleanup job. See
// Extensions/BackgroundJobsExtensions.cs for what each does and why.
app.MapBackgroundJobsDashboard();
app.MapBackgroundJobsRecurringJobs();

app.Run();

public partial class Program
{
}
