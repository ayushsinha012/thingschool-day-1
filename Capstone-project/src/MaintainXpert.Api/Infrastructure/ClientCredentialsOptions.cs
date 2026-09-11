namespace MaintainXpert.Api.Infrastructure;

public sealed class ClientCredentialsOptions
{
    public const string SectionName = "IntegrationClient";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecretHash { get; set; } = string.Empty;
}
