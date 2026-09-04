using MaintainXpert.Notifications.Domain;

namespace MaintainXpert.Notifications.Application;

public interface INotificationSink
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}
