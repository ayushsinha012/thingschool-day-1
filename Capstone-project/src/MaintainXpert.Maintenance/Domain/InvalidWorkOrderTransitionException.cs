namespace MaintainXpert.Maintenance.Domain;

public sealed class InvalidWorkOrderTransitionException : Exception
{
    public InvalidWorkOrderTransitionException(string message) : base(message)
    {
    }
}
