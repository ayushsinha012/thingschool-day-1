namespace QuotesApi.Services;

public enum RefreshTokenOutcome
{
    Success,
    NotFound,
    Expired,
    ReuseDetected,

    /// <summary>
    /// The token itself is still valid (not expired, not reused), but the
    /// family hasn't been used within AuthSecurityOptions.IdleTimeoutMinutes
    /// - the session is ended for inactivity, not for a security reason.
    /// </summary>
    IdleTimeoutExceeded
}

public sealed record RefreshTokenRotationResult(
    RefreshTokenOutcome Outcome,
    int UserId,
    string? NewRawToken);

public interface IRefreshTokenService
{
    Task<string> IssueAsync(
        int userId,
        Guid familyId,
        CancellationToken cancellationToken);

    Task<RefreshTokenRotationResult> RotateAsync(
        string presentedToken,
        CancellationToken cancellationToken);

    Task RevokeAsync(
        string presentedToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every active refresh token for this user, across every
    /// device/family - "log out everywhere". Use this when a user reports
    /// (or you detect) a compromised account: it doesn't change the
    /// password, but it kills every session the attacker (or the
    /// legitimate user, on an old device) currently holds, forcing a fresh
    /// login on all of them.
    /// </summary>
    Task RevokeAllForUserAsync(
        int userId,
        CancellationToken cancellationToken);
}
