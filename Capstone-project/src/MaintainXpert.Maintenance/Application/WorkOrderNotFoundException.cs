using MaintainXpert.Maintenance.Domain;

namespace MaintainXpert.Maintenance.Application;

public sealed class WorkOrderNotFoundException : Exception
{
    public WorkOrderNotFoundException(WorkOrderId id) : base($"Work order '{id}' was not found.")
    {
    }
}
