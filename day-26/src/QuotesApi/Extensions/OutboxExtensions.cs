using QuotesApi.Outbox;

namespace QuotesApi.Extensions;

public static class OutboxExtensions
{
    public static IServiceCollection AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OutboxRelayOptions>(configuration.GetSection(OutboxRelayOptions.SectionName));

        services.AddSingleton<IOutboxRelayStatus, OutboxRelayStatus>();
        services.AddScoped<OutboxRelayProcessor>();
        services.AddHostedService<OutboxRelayWorker>();

        return services;
    }
}
