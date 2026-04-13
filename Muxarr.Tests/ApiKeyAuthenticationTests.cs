using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Muxarr.Core.Config;
using Muxarr.Data;
using Muxarr.Data.Extensions;
using Muxarr.Web.Authentication;

namespace Muxarr.Tests;

[TestClass]
public class ApiKeyAuthenticationTests
{
    private const string TestApiKey = "test-api-key-12345";
    private const string SchemeName = AuthSchemes.ApiKey;

    private DbContextOptions<AppDbContext> _dbOptions = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"muxarr_auth_test_{Guid.NewGuid():N}.db")}")
            .Options;

        using var context = new AppDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        using var context = new AppDbContext(_dbOptions);
        context.Database.EnsureDeleted();
    }

    [TestMethod]
    public async Task NoApiKeyConfigured_AllowsAnonymousAccess()
    {
        // No WebhookConfig saved - API key is empty
        var result = await Authenticate(new DefaultHttpContext());

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("anonymous", result.Principal?.Identity?.Name);
    }

    [TestMethod]
    public async Task ValidHeaderKey_Succeeds()
    {
        SetApiKey(TestApiKey);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = TestApiKey;

        var result = await Authenticate(context);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("api", result.Principal?.Identity?.Name);
    }

    [TestMethod]
    public async Task ValidQueryParam_Succeeds()
    {
        SetApiKey(TestApiKey);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?apikey={TestApiKey}");

        var result = await Authenticate(context);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("api", result.Principal?.Identity?.Name);
    }

    [TestMethod]
    public async Task HeaderTakesPrecedenceOverQueryParam()
    {
        SetApiKey(TestApiKey);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = TestApiKey;
        context.Request.QueryString = new QueryString("?apikey=wrong-key");

        var result = await Authenticate(context);

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public async Task InvalidKey_Fails()
    {
        SetApiKey(TestApiKey);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "wrong-key";

        var result = await Authenticate(context);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNotNull(result.Failure);
    }

    [TestMethod]
    public async Task MissingKey_WhenRequired_ReturnsNoResult()
    {
        SetApiKey(TestApiKey);

        var result = await Authenticate(new DefaultHttpContext());

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.None);
    }

    [TestMethod]
    public async Task EmptyHeaderValue_WhenRequired_ReturnsNoResult()
    {
        SetApiKey(TestApiKey);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "";

        var result = await Authenticate(context);

        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task ApiKeyIsCaseSensitive()
    {
        SetApiKey(TestApiKey);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = TestApiKey.ToUpperInvariant();

        var result = await Authenticate(context);

        Assert.IsFalse(result.Succeeded);
    }

    private void SetApiKey(string apiKey)
    {
        using var context = new AppDbContext(_dbOptions);
        context.Configs.Set(new WebhookConfig { ApiKey = apiKey });
        context.SaveChanges();
    }

    private async Task<AuthenticateResult> Authenticate(HttpContext httpContext)
    {
        var factory = new TestDbContextFactory(_dbOptions);
        var options = new ApiKeyAuthenticationOptions();
        var optionsMonitor = new TestOptionsMonitor(options);

        var handler = new ApiKeyAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            factory);

        var scheme = new AuthenticationScheme(SchemeName, null, typeof(ApiKeyAuthenticationHandler));
        await handler.InitializeAsync(scheme, httpContext);

        return await handler.AuthenticateAsync();
    }

    private class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            return new AppDbContext(options);
        }
    }

    private class TestOptionsMonitor(ApiKeyAuthenticationOptions options)
        : IOptionsMonitor<ApiKeyAuthenticationOptions>
    {
        public ApiKeyAuthenticationOptions CurrentValue => options;

        public ApiKeyAuthenticationOptions Get(string? name)
        {
            return options;
        }

        public IDisposable? OnChange(Action<ApiKeyAuthenticationOptions, string?> listener)
        {
            return null;
        }
    }
}
