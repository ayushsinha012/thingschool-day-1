namespace QuotesApi.Services;

/// <summary>
/// Typed configuration for the "AuthSecurity" section - the tunable
/// thresholds for the three login/session defenses:
///   - idle-session timeout (LastUsedAt-based, see RefreshTokenService)
///   - per-account lockout after repeated failed logins (see AuthController)
///   - per-IP request throttling on the auth endpoints (see
///     InfrastructureExtensions.AddAuthRateLimiting)
/// None of these values are secret, so - unlike JwtOptions.Key - they live
/// directly in appsettings.json and can be overridden per environment.
/// </summary>
public sealed class AuthSecurityOptions
{
    public const string SectionName = "AuthSecurity";

    /// <summary>
    /// A refresh token family that hasn't been used to renew an access
    /// token for this many minutes is treated as ended (the user is signed
    /// out and must log in again), even if it's within its absolute
    /// 7-day lifetime. This is the "close the session after N minutes of
    /// no interaction" control.
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 20;

    /// <summary>
    /// Consecutive failed password attempts allowed before the account is
    /// locked out for <see cref="LockoutMinutes"/>.
    /// </summary>
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>
    /// How long an account stays locked out after hitting
    /// <see cref="MaxFailedLoginAttempts"/>.
    /// </summary>
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>
    /// Max /api/auth/login or /api/auth/register requests a single client
    /// IP may make per <see cref="RateLimitWindowSeconds"/> before getting
    /// 429s - independent of the per-account lockout above, so a single
    /// attacker can't spray many different email addresses to dodge it.
    /// </summary>
    public int RateLimitPermitsPerWindow { get; set; } = 10;

    public int RateLimitWindowSeconds { get; set; } = 60;
}
