namespace MaintainXpert.Notifications.Domain;

public sealed record NotificationMessage(string Subject, string Body, DateTimeOffset CreatedAt);
