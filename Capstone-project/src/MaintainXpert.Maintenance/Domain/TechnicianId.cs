namespace MaintainXpert.Maintenance.Domain;

public readonly record struct TechnicianId(Guid Value)
{
    public static TechnicianId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
