using BCrypt.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.DTOs;
using QuotesApi.Services;
using System.Security.Cryptography;

namespace QuotesApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwtTokenService;

    public AuthController(
        AppDbContext db,
        JwtTokenService jwtTokenService)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Email and password are required."
            });
        }

        var email = request.Email.Trim();

        var user = await _db.Users
            .FirstOrDefaultAsync(
                user => user.Email == email,
                cancellationToken);

        if (user is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid credentials",
                Detail = "Email or password is incorrect."
            });
        }

        var passwordIsValid =
            BCrypt.Net.BCrypt.Verify(
                request.Password,
                user.PasswordHash);

        if (!passwordIsValid)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid credentials",
                Detail = "Email or password is incorrect."
            });
        }

        var accessToken =
            _jwtTokenService.CreateAccessToken(user);

        var refreshToken =
            Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32));

        var expiresIn =
            _jwtTokenService
                .GetAccessTokenLifetimeSeconds();

        return Ok(new
        {
            access_token = accessToken,
            refresh_token = refreshToken,
            expires_in = expiresIn
        });
    }
}
