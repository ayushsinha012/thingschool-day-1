namespace QuotesApi.Models;

public class RefreshToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public Guid FamilyId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public string? ReplacedByTokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
