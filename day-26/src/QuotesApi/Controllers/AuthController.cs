using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotesApi.Data;
using QuotesApi.DTOs;
using QuotesApi.Models;
using QuotesApi.Services;

namespace QuotesApi.Controllers;

// Applies the "auth" fixed-window limiter (see
// InfrastructureExtensions.AddAuthRateLimiting) to every action on this
// controller, so login/register/refresh can't be hammered from one IP
// regardless of which account is targeted.
[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwtTokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IClock _clock;
    private readonly AuthSecurityOptions _securityOptions;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext db,
        JwtTokenService jwtTokenService,
        IRefreshTokenService refreshTokenService,
        IClock clock,
        IOptions<AuthSecurityOptions> securityOptions,
        ILogger<AuthController> logger)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _refreshTokenService = refreshTokenService;
        _clock = clock;
        _securityOptions = securityOptions.Value;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();

        var existing = await _db.Users
            .FirstOrDefaultAsync(
                user => user.Email.ToLower() == email.ToLower(),
                cancellationToken);

        if (existing is not null)
        {
            _logger.LogWarning("Registration rejected: email already registered");

            return Conflict(new ProblemDetails
            {
                Title = "Email already registered",
                Detail = "An account with this email address already exists."
            });
        }

        var user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        };

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("Registration rejected: concurrent duplicate email insert");

            return Conflict(new ProblemDetails
            {
                Title = "Email already registered",
                Detail = "An account with this email address already exists."
            });
        }

        _logger.LogInformation("User {UserId} registered", user.Id);

        return Created(
            $"/api/auth/users/{user.Id}",
            new { id = user.Id, email = user.Email });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();

        var user = await _db.Users
            .FirstOrDefaultAsync(
                user => user.Email == email,
                cancellationToken);

        // Checked before verifying the password: a locked-out account
        // should reject every attempt - even a correct password - until
        // the lockout window expires. This is what stops a brute-force
        // (or credential-stuffing) attacker from grinding through
        // passwords for one account no matter how many they try.
        if (user is not null &&
            user.LockoutEnd is { } lockoutEnd &&
            lockoutEnd > _clock.UtcNow)
        {
            _logger.LogWarning(
                "Login rejected for user {UserId}: account locked out until {LockoutEnd}",
                user.Id,
                lockoutEnd);

            return Problem(
                title: "Account temporarily locked",
                detail: $"Too many failed login attempts. Try again after {lockoutEnd:O}.",
                statusCode: StatusCodes.Status423Locked);
        }

        var passwordIsValid =
            user is not null &&
            BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

        if (user is null || !passwordIsValid)
        {
            if (user is not null)
            {
                await RegisterFailedLoginAsync(user, cancellationToken);
            }

            _logger.LogWarning("Login failed: invalid credentials");

            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid credentials",
                Detail = "Email or password is incorrect."
            });
        }

        if (user.FailedLoginAttempts > 0 || user.LockoutEnd is not null)
        {
            // Successful login clears the slate - past failed attempts
            // shouldn't count against a future, unrelated lockout window.
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;

            await _db.SaveChangesAsync(cancellationToken);
        }

        var accessToken = _jwtTokenService.CreateAccessToken(user);

        var refreshToken = await _refreshTokenService.IssueAsync(
            user.Id,
            Guid.NewGuid(),
            cancellationToken);

        var expiresIn = _jwtTokenService.GetAccessTokenLifetimeSeconds();

        _logger.LogInformation("User {UserId} logged in", user.Id);

        return Ok(new
        {
            access_token = accessToken,
            refresh_token = refreshToken,
            expires_in = expiresIn
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _refreshTokenService.RotateAsync(
            request.RefreshToken,
            cancellationToken);

        switch (result.Outcome)
        {
            case RefreshTokenOutcome.ReuseDetected:
                _logger.LogWarning(
                    "Refresh token reuse detected for user {UserId}; all sessions revoked",
                    result.UserId);

                return Unauthorized(new ProblemDetails
                {
                    Title = "Refresh token reuse detected",
                    Detail = "All sessions for this account have been revoked. Please log in again."
                });

            case RefreshTokenOutcome.NotFound:
            case RefreshTokenOutcome.Expired:
                _logger.LogWarning(
                    "Refresh token rejected: {Outcome}",
                    result.Outcome);

                return Unauthorized(new ProblemDetails
                {
                    Title = "Invalid refresh token",
                    Detail = "The refresh token is invalid or has expired."
                });

            case RefreshTokenOutcome.IdleTimeoutExceeded:
                _logger.LogInformation(
                    "Refresh token rejected for user {UserId}: idle timeout exceeded",
                    result.UserId);

                return Unauthorized(new ProblemDetails
                {
                    Title = "Session expired",
                    Detail = "You were signed out due to inactivity. Please log in again."
                });
        }

        var user = await _db.Users.FindAsync(
            [result.UserId],
            cancellationToken);

        if (user is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid refresh token",
                Detail = "The refresh token is invalid or has expired."
            });
        }

        var accessToken = _jwtTokenService.CreateAccessToken(user);
        var expiresIn = _jwtTokenService.GetAccessTokenLifetimeSeconds();

        _logger.LogInformation("Refreshed tokens for user {UserId}", user.Id);

        return Ok(new
        {
            access_token = accessToken,
            refresh_token = result.NewRawToken,
            expires_in = expiresIn
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        await _refreshTokenService.RevokeAsync(
            request.RefreshToken,
            cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// "Log out everywhere" - revokes every refresh token for the caller,
    /// on every device. This is the endpoint to call the moment a user
    /// suspects their credentials are compromised: it can't undo a leaked
    /// password by itself (pair it with a password change - not yet
    /// implemented here), but it immediately kills any session an attacker
    /// is currently holding, including ones your own client never
    /// presented and so reuse-detection would otherwise never see.
    /// Requires a currently-valid access token, so it's exposed to anyone
    /// still signed in on at least one device.
    /// </summary>
    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return Unauthorized();
        }

        await _refreshTokenService.RevokeAllForUserAsync(userId.Value, cancellationToken);

        _logger.LogInformation("User {UserId} revoked all sessions", userId);

        return NoContent();
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(claim, out var userId) ? userId : null;
    }

    /// <summary>
    /// Records one failed password attempt and, once
    /// AuthSecurityOptions.MaxFailedLoginAttempts is reached, locks the
    /// account out for AuthSecurityOptions.LockoutMinutes. This is the
    /// per-account half of the brute-force defense - the per-IP half is
    /// the "auth" rate limiter applied to this whole controller.
    /// </summary>
    private async Task RegisterFailedLoginAsync(
        User user,
        CancellationToken cancellationToken)
    {
        user.FailedLoginAttempts++;

        if (user.FailedLoginAttempts >= _securityOptions.MaxFailedLoginAttempts)
        {
            user.LockoutEnd = _clock.UtcNow.AddMinutes(_securityOptions.LockoutMinutes);
            user.FailedLoginAttempts = 0;

            _logger.LogWarning(
                "User {UserId} locked out until {LockoutEnd} after {MaxAttempts} failed login attempts",
                user.Id,
                user.LockoutEnd,
                _securityOptions.MaxFailedLoginAttempts);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
