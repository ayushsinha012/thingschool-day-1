using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Services;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly AuthSecurityOptions _options;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(
        AppDbContext db,
        IClock clock,
        IOptions<AuthSecurityOptions> options,
        ILogger<RefreshTokenService> logger)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> IssueAsync(
        int userId,
        Guid familyId,
        CancellationToken cancellationToken)
    {
        var rawToken = GenerateRawToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Hash(rawToken),
            ExpiresAt = _clock.UtcNow.Add(RefreshTokenLifetime),
            CreatedAt = _clock.UtcNow,
            LastUsedAt = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        return rawToken;
    }

    public async Task<RefreshTokenRotationResult> RotateAsync(
        string presentedToken,
        CancellationToken cancellationToken)
    {
        var hash = Hash(presentedToken);

        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null)
        {
            return new RefreshTokenRotationResult(
                RefreshTokenOutcome.NotFound, 0, null);
        }

        if (token.RevokedAt is not null)
        {
            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId} in family {FamilyId}; revoking chain",
                token.UserId,
                token.FamilyId);

            await RevokeFamilyAsync(token.FamilyId, cancellationToken);

            return new RefreshTokenRotationResult(
                RefreshTokenOutcome.ReuseDetected, token.UserId, null);
        }

        if (token.ExpiresAt <= _clock.UtcNow)
        {
            return new RefreshTokenRotationResult(
                RefreshTokenOutcome.Expired, token.UserId, null);
        }

        var idleTimeout = TimeSpan.FromMinutes(_options.IdleTimeoutMinutes);

        if (_clock.UtcNow - token.LastUsedAt > idleTimeout)
        {
            _logger.LogInformation(
                "Refresh token family {FamilyId} for user {UserId} idle for longer than {IdleTimeoutMinutes} minutes; ending session",
                token.FamilyId,
                token.UserId,
                _options.IdleTimeoutMinutes);

            // The token is still technically valid, but nobody has used it
            // in too long - end the whole family rather than just this
            // token, so a stale tab can't quietly keep the session alive.
            await RevokeFamilyAsync(token.FamilyId, cancellationToken);

            return new RefreshTokenRotationResult(
                RefreshTokenOutcome.IdleTimeoutExceeded, token.UserId, null);
        }

        var newRawToken = GenerateRawToken();
        var newHash = Hash(newRawToken);

        token.RevokedAt = _clock.UtcNow;
        token.ReplacedByTokenHash = newHash;

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = token.UserId,
            FamilyId = token.FamilyId,
            TokenHash = newHash,
            ExpiresAt = _clock.UtcNow.Add(RefreshTokenLifetime),
            CreatedAt = _clock.UtcNow,
            LastUsedAt = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new RefreshTokenRotationResult(
            RefreshTokenOutcome.Success, token.UserId, newRawToken);
    }

    public async Task RevokeAsync(
        string presentedToken,
        CancellationToken cancellationToken)
    {
        var hash = Hash(presentedToken);

        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null || token.RevokedAt is not null)
        {
            return;
        }

        token.RevokedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllForUserAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var activeToken in activeTokens)
        {
            activeToken.RevokedAt = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Revoked all {Count} active refresh token(s) for user {UserId}",
            activeTokens.Count,
            userId);
    }

    private async Task RevokeFamilyAsync(
        Guid familyId,
        CancellationToken cancellationToken)
    {
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var activeToken in activeTokens)
        {
            activeToken.RevokedAt = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string GenerateRawToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string rawToken) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
