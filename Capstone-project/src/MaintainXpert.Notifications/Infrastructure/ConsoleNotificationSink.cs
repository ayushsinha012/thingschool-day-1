using MaintainXpert.Notifications.Application;
using MaintainXpert.Notifications.Domain;

namespace MaintainXpert.Notifications.Infrastructure;

public sealed class ConsoleNotificationSink : INotificationSink
{
    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[notification] {message.CreatedAt:O} {message.Subject} - {message.Body}");
        return Task.CompletedTask;
    }
}
