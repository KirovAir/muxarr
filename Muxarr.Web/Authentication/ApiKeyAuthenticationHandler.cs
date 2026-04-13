using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Muxarr.Core.Config;
using Muxarr.Data;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Authentication;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDbContextFactory<AppDbContext> contextFactory)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var config = await db.Configs.GetAsync<WebhookConfig>();

        if (string.IsNullOrEmpty(config?.ApiKey))
            // No API key configured - allow all requests
        {
            return AuthenticateResult.Success(CreateTicket("anonymous"));
        }

        var key = Request.Headers[Options.HeaderName].FirstOrDefault()
                  ?? Request.Query[Options.QueryName].FirstOrDefault();

        if (string.IsNullOrEmpty(key))
        {
            return AuthenticateResult.NoResult();
        }

        if (!string.Equals(key, config.ApiKey, StringComparison.Ordinal))
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        return AuthenticateResult.Success(CreateTicket("api"));
    }

    private AuthenticationTicket CreateTicket(string name)
    {
        var identity = new ClaimsIdentity(Scheme.Name);
        identity.AddClaim(new Claim(ClaimTypes.Name, name));
        return new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
    }
}
