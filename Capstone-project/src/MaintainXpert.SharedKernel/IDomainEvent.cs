namespace MaintainXpert.SharedKernel;

public interface IDomainEvent
{
    DateTimeOffset OccurredAtUtc { get; }
}
