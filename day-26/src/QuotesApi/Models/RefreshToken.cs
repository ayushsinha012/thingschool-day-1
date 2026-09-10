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

    /// <summary>
    /// Last time this token (or the token it replaced, in the same family)
    /// was successfully used to obtain a new access token. Used as the
    /// "last activity" signal for idle-session timeout: a family that goes
    /// quiet for longer than AuthSecurityOptions.IdleTimeoutMinutes is
    /// treated as ended even though it hasn't hit its absolute
    /// <see cref="ExpiresAt"/> yet.
    /// </summary>
    public DateTimeOffset LastUsedAt { get; set; }
}
