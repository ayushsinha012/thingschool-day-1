namespace MaintainXpert.Api.Infrastructure;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
}
