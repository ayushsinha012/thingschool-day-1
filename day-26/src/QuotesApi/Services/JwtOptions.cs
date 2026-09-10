namespace QuotesApi.Services;

/// <summary>
/// Typed configuration for the "Jwt" section, bound via
/// <c>services.Configure&lt;JwtOptions&gt;(...)</c>. <see cref="Key"/> is a
/// secret and must never be set in appsettings.json - it is supplied via
/// dotnet user-secrets locally and an environment variable/Key Vault
/// reference in production.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
}
