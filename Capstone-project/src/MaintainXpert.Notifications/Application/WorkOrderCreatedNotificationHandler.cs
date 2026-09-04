using MaintainXpert.Maintenance.Domain.Events;
using MaintainXpert.Notifications.Domain;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Notifications.Application;

public sealed class WorkOrderCreatedNotificationHandler : IDomainEventHandler<WorkOrderCreated>
{
    private readonly INotificationSink _sink;

    public WorkOrderCreatedNotificationHandler(INotificationSink sink)
    {
        _sink = sink;
    }

    public Task HandleAsync(WorkOrderCreated domainEvent, CancellationToken cancellationToken = default)
    {
        var message = new NotificationMessage(
            Subject: "New maintenance work order",
            Body: $"Work order {domainEvent.WorkOrderId} was created for asset {domainEvent.AssetId}.",
            CreatedAt: domainEvent.OccurredAtUtc);

        return _sink.SendAsync(message, cancellationToken);
    }
}
