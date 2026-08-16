namespace QuotesApi.Services;

public enum RefreshTokenOutcome
{
    Success,
    NotFound,
    Expired,
    ReuseDetected
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
}
