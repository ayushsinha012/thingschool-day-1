using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.DTOs;
using QuotesApi.Services;

namespace QuotesApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwtTokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext db,
        JwtTokenService jwtTokenService,
        IRefreshTokenService refreshTokenService,
        ILogger<AuthController> logger)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _refreshTokenService = refreshTokenService;
        _logger = logger;
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

        var passwordIsValid =
            user is not null &&
            BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

        if (user is null || !passwordIsValid)
        {
            _logger.LogWarning("Login failed: invalid credentials");

            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid credentials",
                Detail = "Email or password is incorrect."
            });
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
}
