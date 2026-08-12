using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.Models;

namespace Tests.Domain;

/// <summary>
/// Hosts the real QuotesApi pipeline against an isolated SQLite file so
/// authorization tests can exercise HTTP status codes end-to-end without
/// touching the developer's local quotes.db.
/// </summary>
public class AuthorizationTestFactory : WebApplicationFactory<Program>
{
    private const string TestJwtKey =
        "Tests-Domain-Authorization-Test-Signing-Key-0123456789";

    private readonly string _databasePath;

    public AuthorizationTestFactory()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"quotes-authz-tests-{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Key", TestJwtKey);

        builder.UseSetting(
            "ConnectionStrings:DefaultConnection",
            $"Data Source={_databasePath}");
    }

    public async Task<Collection> SeedCollectionAsync(
        int ownerId,
        string name = "Test Collection")
    {
        using var scope = Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var collection = new Collection(name, ownerId);

        db.Collections.Add(collection);

        await db.SaveChangesAsync();

        return collection;
    }

    /// <summary>
    /// Mints a JWT signed with the same test key the test host trusts, so
    /// each scenario can control exactly which claims the caller presents.
    /// </summary>
    public string CreateAccessToken(
        int userId,
        bool includeEditQuotesClaim)
    {
        var keyBytes = Encoding.UTF8.GetBytes(TestJwtKey);

        var securityKey = new SymmetricSecurityKey(keyBytes);

        var credentials = new SigningCredentials(
            securityKey,
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                userId.ToString())
        };

        if (includeEditQuotesClaim)
        {
            claims.Add(
                new Claim(
                    PermissionClaims.ClaimType,
                    PermissionClaims.CanEditQuotes));
        }

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
