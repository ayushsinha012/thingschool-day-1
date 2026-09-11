using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MaintainXpert.Api.Infrastructure;

public sealed class TokenService
{
    private readonly JwtOptions _jwtOptions;
    private readonly ClientCredentialsOptions _clientOptions;

    public TokenService(IOptions<JwtOptions> jwtOptions, IOptions<ClientCredentialsOptions> clientOptions)
    {
        _jwtOptions = jwtOptions.Value;
        _clientOptions = clientOptions.Value;
    }

    public bool ValidateClientCredentials(string clientId, string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return false;
        }

        if (!string.Equals(clientId, _clientOptions.ClientId, StringComparison.Ordinal))
        {
            return false;
        }

        var computedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(clientSecret)));
        var expectedHash = _clientOptions.ClientSecretHash;

        if (computedHash.Length != expectedHash.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHash),
            Encoding.UTF8.GetBytes(expectedHash));
    }

    public (string AccessToken, int ExpiresInSeconds) CreateAccessToken(string clientId)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_jwtOptions.Key);
        var securityKey = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, clientId),
            new Claim("scope", "workorders.write")
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenMinutes),
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        return (accessToken, _jwtOptions.AccessTokenMinutes * 60);
    }
}
