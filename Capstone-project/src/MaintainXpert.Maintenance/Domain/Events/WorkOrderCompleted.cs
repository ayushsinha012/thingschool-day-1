using MaintainXpert.SharedKernel;

namespace MaintainXpert.Maintenance.Domain.Events;

public sealed record WorkOrderCompleted(
    WorkOrderId WorkOrderId,
    AssetId AssetId,
    TechnicianId TechnicianId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
