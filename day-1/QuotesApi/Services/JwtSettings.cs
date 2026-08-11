namespace QuotesApi.Services;

public sealed class JwtSettings
{
    public string Key { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
}
