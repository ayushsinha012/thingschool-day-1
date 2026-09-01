using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Options;
using QuotesApi.Messaging;

namespace QuotesApi.Extensions;

public static class MessagingExtensions
{
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ServiceBusOptions>(configuration.GetSection(ServiceBusOptions.SectionName));

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            var environment = sp.GetRequiredService<IHostEnvironment>();
            return new ServiceBusClient(options.FullyQualifiedNamespace, BuildCredential(environment));
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            var environment = sp.GetRequiredService<IHostEnvironment>();
            return new ServiceBusAdministrationClient(options.FullyQualifiedNamespace, BuildCredential(environment));
        });

        services.AddSingleton<IMessagingActivityLog, MessagingActivityLog>();
        services.AddSingleton<IQuoteEventPublisher, QuoteEventPublisher>();
        services.AddScoped<IQuoteEventProcessor, QuoteEventProcessor>();

        services.AddHostedService<SubscriptionAWorker>();
        services.AddHostedService<SubscriptionBWorker>();

        return services;
    }

    private static TokenCredential BuildCredential(IHostEnvironment environment) =>
        environment.IsDevelopment()
            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = true })
            : new DefaultAzureCredential();
}
