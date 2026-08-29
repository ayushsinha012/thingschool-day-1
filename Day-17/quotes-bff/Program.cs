using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;

// QuotesBff: a minimal, business-logic-free reverse proxy in front of the
// real Week-1 QuotesApi (day-1/QuotesApi). It exists for exactly one reason:
// so that a Managed-Identity-acquired Entra access token is obtained on an
// Azure-hosted server-side component - never in the browser - and QuotesApi
// is reached only through that server-side hop. See result.md (Day 17 Part
// 3) for the full reasoning and the Entra app registration this depends on.
//
// This process never stores a client secret, certificate, password, or any
// other credential. DefaultAzureCredential resolves to the Container App's
// system-assigned Managed Identity in Azure (see infra/resources.bicep) and,
// locally, to the developer's own `az login` session - either way, a token
// is requested fresh from Azure AD for each call that needs one; nothing is
// ever written to disk or configuration.
var builder = WebApplication.CreateBuilder(args);

var quotesApiBaseUrl = builder.Configuration["QuotesApi:BaseUrl"]
    ?? throw new InvalidOperationException("QuotesApi:BaseUrl is not configured.");

var quotesApiScope = builder.Configuration["QuotesApi:Scope"];

const string CorsPolicyName = "AllowFrontend";

// Same shape, and same reasoning, as QuotesApi's own CORS setup (see
// day-1/QuotesApi/Extensions/InfrastructureExtensions.cs): any localhost
// origin in Development (each exercise's `ng serve` picks its own port), an
// explicit origin allow-list from configuration in production - never a
// wildcard - which stays empty until the deployed Static Web App's real
// origin is known.
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy
                .SetIsOriginAllowed(origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                    (uri.Host == "localhost" || uri.Host == "127.0.0.1"))
                .AllowAnyHeader()
                .AllowAnyMethod();
            return;
        }

        var productionOrigins = builder.Configuration
            .GetSection("Cors:ProductionOrigins")
            .Get<string[]>() ?? [];

        policy
            .WithOrigins(productionOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient("QuotesApi", client =>
{
    client.BaseAddress = new Uri(quotesApiBaseUrl);
});

builder.Services.AddSingleton(new DefaultAzureCredential());

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCors(CorsPolicyName);

app.MapHealthChecks("/health");

// Pure pass-through - no quote/auth business logic is duplicated here (that
// stays exactly where it already lives, in QuotesApi's own
// Endpoints/QuoteEndpoints.cs and Controllers/AuthController.cs). Every
// method, path, query string, and body this receives goes to QuotesApi
// unchanged; the only decision made here is which Authorization header rides
// along:
//
//   - A request that already carries one (the browser's own bearer token
//     from a real /api/auth/login - see auth.service.ts) is forwarded as-is.
//     That token already carries whatever claims QuotesApi's existing
//     authorization policies check (e.g. PermissionClaims.CanEditQuotes for
//     POST /api/quotes) - forwarding it unchanged preserves that
//     authorization exactly as it already works today, with no change to
//     QuotesApi's authorization code.
//   - A request with no Authorization header at all (anonymous reads) gets
//     this service's own Managed-Identity-acquired Entra token instead, so
//     QuotesApi's existing Entra JWT scheme
//     (day-1/QuotesApi/Authentication/JwtAuthenticationExtensions.cs) has a
//     real, validated token to see end-to-end even on endpoints that don't
//     themselves require one.
app.Map(
    "/{**path}",
    async (
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        DefaultAzureCredential credential,
        string? path) =>
    {
        var targetPath = string.IsNullOrEmpty(path) ? string.Empty : $"/{path}";
        var requestUri = targetPath + context.Request.QueryString;

        using var forwardRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), requestUri);

        var hasBody =
            HttpMethods.IsPost(context.Request.Method) ||
            HttpMethods.IsPut(context.Request.Method) ||
            HttpMethods.IsPatch(context.Request.Method);

        if (hasBody)
        {
            forwardRequest.Content = new StreamContent(context.Request.Body);

            if (!string.IsNullOrEmpty(context.Request.ContentType))
            {
                forwardRequest.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
            }
        }

        if (context.Request.Headers.TryGetValue("Authorization", out var incomingAuth))
        {
            forwardRequest.Headers.TryAddWithoutValidation("Authorization", incomingAuth.ToString());
        }
        else if (!string.IsNullOrWhiteSpace(quotesApiScope))
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext([quotesApiScope]),
                context.RequestAborted);

            forwardRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        var client = httpClientFactory.CreateClient("QuotesApi");

        using var response = await client.SendAsync(
            forwardRequest,
            HttpCompletionOption.ResponseHeadersRead,
            context.RequestAborted);

        context.Response.StatusCode = (int)response.StatusCode;

        if (response.Content.Headers.ContentType is not null)
        {
            context.Response.ContentType = response.Content.Headers.ContentType.ToString();
        }

        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    });

app.Run();

public partial class Program
{
}
