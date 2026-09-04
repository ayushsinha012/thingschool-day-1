namespace MaintainXpert.Maintenance.Domain;

public readonly record struct WorkOrderId(Guid Value)
{
    public static WorkOrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
