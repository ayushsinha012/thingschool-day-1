using MaintainXpert.SharedKernel;

namespace MaintainXpert.Maintenance.Domain.Events;

public sealed record WorkOrderCreated(
    WorkOrderId WorkOrderId,
    AssetId AssetId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
