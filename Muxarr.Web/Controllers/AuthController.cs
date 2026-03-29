using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Api;
using Muxarr.Data;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Controllers;

public class AuthController(IDbContextFactory<AppDbContext> contextFactory) : Controller
{
    public const string LoginRateLimitPolicy = nameof(LoginRateLimitPolicy);

    private static readonly PasswordHasher<string> Hasher = new();

    [HttpPost]
    [Route("~/api/auth/login")]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    public async Task<IActionResult> Login([FromForm] string username, [FromForm] string password)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var config = await context.Configs.GetAsync<AuthConfig>(AuthConfig.Key);

        if (config == null)
        {
            return Redirect("/");
        }

        var isValid = string.Equals(config.Username, username, StringComparison.OrdinalIgnoreCase);
        if (isValid)
        {
            var result = Hasher.VerifyHashedPassword(username, config.PasswordHash, password);
            isValid = result != PasswordVerificationResult.Failed;
        }

        if (!isValid)
        {
            return Redirect("/login?error=1");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, config.Username)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true });

        return Redirect("/");
    }

    [HttpPost]
    [Route("~/api/auth/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/login");
    }
}
