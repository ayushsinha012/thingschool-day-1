using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Models;

namespace QuotesApi.Services;

public sealed class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreateAccessToken(User user)
    {
        var key = _configuration["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "JWT signing key is not configured.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(key);

        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT signing key must be at least 256 bits.");
        }

        var expiresInMinutes =
            _configuration.GetValue<int?>(
                "Jwt:AccessTokenMinutes") ?? 15;

        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                user.Id.ToString()),

            new Claim(
                ClaimTypes.Email,
                user.Email)
        };

        var securityKey =
            new SymmetricSecurityKey(keyBytes);

        var credentials =
            new SigningCredentials(
                securityKey,
                SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(
                expiresInMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }

    public int GetAccessTokenLifetimeSeconds()
    {
        var minutes =
            _configuration.GetValue<int?>(
                "Jwt:AccessTokenMinutes") ?? 15;

        return minutes * 60;
    }
}
