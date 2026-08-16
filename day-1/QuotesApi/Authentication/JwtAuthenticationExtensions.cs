using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Services;

namespace QuotesApi.Authentication;

public static class JwtAuthenticationExtensions
{
    private const string LocalScheme = JwtBearerDefaults.AuthenticationScheme;
    private const string EntraScheme = "Entra";
    private const string SmartScheme = "smart";

    /// <summary>
    /// Wires two JWT schemes behind a policy scheme that forwards each request
    /// based on the token issuer: self-issued tokens go to the local "Bearer"
    /// handler, Entra ID (Azure AD) tokens go to the "Entra" handler. Fill in
    /// the "Entra" config section (TenantId/ClientId/Audience) from an actual
    /// Azure AD app registration to activate Entra-issued tokens.
    /// </summary>
    public static IServiceCollection AddDualJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register the typed options so any DI-resolved consumer (e.g.
        // JwtTokenService) gets them via IOptions<T>/IOptionsSnapshot<T>/
        // IOptionsMonitor<T> - bound from IConfiguration, so environment
        // variables and appsettings.{Environment}.json still take
        // precedence over appsettings.json exactly as before.
        services.Configure<JwtOptions>(
            configuration.GetSection(JwtOptions.SectionName));

        services.Configure<EntraSettings>(
            configuration.GetSection("Entra"));

        // This method itself runs during service registration, before the
        // DI container exists, so IOptions<T> isn't resolvable yet here -
        // bind directly from IConfiguration for the fail-fast startup
        // checks below (same source, same precedence as the Configure<T>
        // registrations above).
        var jwtOptions = configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? new JwtOptions();

        var jwtKey = jwtOptions.Key;

        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            throw new InvalidOperationException(
                "JWT signing key is not configured. Set Jwt:Key.");
        }

        var jwtKeyBytes = Encoding.UTF8.GetBytes(jwtKey);

        if (jwtKeyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT signing key must be at least 256 bits.");
        }

        var entraSettings = configuration
            .GetSection("Entra")
            .Get<EntraSettings>() ?? new EntraSettings();

        services
            .AddAuthentication(SmartScheme)
            .AddPolicyScheme(SmartScheme, "Local or Entra JWT", options =>
            {
                options.ForwardDefaultSelector = context =>
                    IsEntraIssuedToken(context) ? EntraScheme : LocalScheme;
            })
            .AddJwtBearer(LocalScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(jwtKeyBytes),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            })
            .AddJwtBearer(EntraScheme, options =>
            {
                options.Authority =
                    $"https://login.microsoftonline.com/{entraSettings.TenantId}/v2.0";

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = !string.IsNullOrWhiteSpace(entraSettings.Audience),
                    ValidAudience = entraSettings.Audience
                };
            });

        return services;
    }

    private static bool IsEntraIssuedToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawToken = header["Bearer ".Length..];

        try
        {
            var issuer = new JwtSecurityTokenHandler()
                .ReadJwtToken(rawToken)
                .Issuer;

            return issuer.Contains(
                "login.microsoftonline.com",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
