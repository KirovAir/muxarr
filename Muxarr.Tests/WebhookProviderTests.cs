using System.Net;
using System.Text.Json;
using Muxarr.Core.Config;
using Muxarr.Web.Services.Notifications;
using Muxarr.Web.Services.Notifications.Providers;

namespace Muxarr.Tests;

[TestClass]
public class WebhookProviderTests
{
    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public string? CapturedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            if (request.Content != null)
            {
                CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [TestMethod]
    public async Task SendAsync_WithFilePath_IncludesFilePathInJsonPayload()
    {
        var handler = new CapturingHttpMessageHandler();
        using var client = new HttpClient(handler);
        var provider = new WebhookProvider();
        var config = new NotificationConfig
        {
            Provider = "Webhook",
            Settings = new Dictionary<string, string>
            {
                ["Url"] = "https://example.com/webhook"
            }
        };

        var now = DateTime.UtcNow;
        var payload = new NotificationPayload
        {
            Title = "Conversion Completed",
            Body = "movie.mkv - saved 500 MB",
            EventType = NotificationEventType.Completed,
            FileName = "movie.mkv",
            FilePath = "/data/media/movie.mkv",
            SizeBefore = 2000000000,
            SizeAfter = 1500000000,
            SizeSaved = 500000000,
            Error = null,
            Timestamp = now
        };

        await provider.SendAsync(client, config, payload);

        Assert.IsNotNull(handler.CapturedRequest);
        Assert.AreEqual(HttpMethod.Post, handler.CapturedRequest.Method);
        Assert.AreEqual("https://example.com/webhook", handler.CapturedRequest.RequestUri?.ToString());
        Assert.IsNotNull(handler.CapturedBody);

        using var doc = JsonDocument.Parse(handler.CapturedBody);
        var root = doc.RootElement;

        Assert.AreEqual("Completed", root.GetProperty("event").GetString());
        Assert.AreEqual("Conversion Completed", root.GetProperty("title").GetString());
        Assert.AreEqual("movie.mkv - saved 500 MB", root.GetProperty("body").GetString());
        Assert.AreEqual("movie.mkv", root.GetProperty("fileName").GetString());
        Assert.AreEqual("/data/media/movie.mkv", root.GetProperty("filePath").GetString());
        Assert.AreEqual(2000000000, root.GetProperty("sizeBefore").GetInt64());
        Assert.AreEqual(1500000000, root.GetProperty("sizeAfter").GetInt64());
        Assert.AreEqual(500000000, root.GetProperty("sizeSaved").GetInt64());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("error").ValueKind);
    }

    [TestMethod]
    public async Task SendAsync_WithNullFilePath_SerializesNullFilePath()
    {
        var handler = new CapturingHttpMessageHandler();
        using var client = new HttpClient(handler);
        var provider = new WebhookProvider();
        var config = new NotificationConfig
        {
            Provider = "Webhook",
            Settings = new Dictionary<string, string>
            {
                ["Url"] = "https://example.com/webhook"
            }
        };

        var payload = new NotificationPayload
        {
            Title = "Conversion Started",
            Body = "movie.mkv",
            EventType = NotificationEventType.Started,
            FileName = "movie.mkv",
            FilePath = null
        };

        await provider.SendAsync(client, config, payload);

        Assert.IsNotNull(handler.CapturedBody);
        using var doc = JsonDocument.Parse(handler.CapturedBody);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("filePath", out var filePathProp));
        Assert.AreEqual(JsonValueKind.Null, filePathProp.ValueKind);
    }

    [TestMethod]
    public async Task SendAsync_WithAuthorizationHeader_SetsAuthorizationHeader()
    {
        var handler = new CapturingHttpMessageHandler();
        using var client = new HttpClient(handler);
        var provider = new WebhookProvider();
        var config = new NotificationConfig
        {
            Provider = "Webhook",
            Settings = new Dictionary<string, string>
            {
                ["Url"] = "https://example.com/webhook",
                ["Authorization"] = "Bearer token123"
            }
        };

        var payload = new NotificationPayload
        {
            Title = "Test",
            Body = "Test message"
        };

        await provider.SendAsync(client, config, payload);

        Assert.IsNotNull(handler.CapturedRequest);
        Assert.IsTrue(handler.CapturedRequest.Headers.Contains("Authorization"));
        CollectionAssert.Contains(handler.CapturedRequest.Headers.GetValues("Authorization").ToArray(), "Bearer token123");
    }

    [TestMethod]
    public async Task SendAsync_WithoutAuthorizationHeader_OmitsAuthorizationHeader()
    {
        var handler = new CapturingHttpMessageHandler();
        using var client = new HttpClient(handler);
        var provider = new WebhookProvider();
        var config = new NotificationConfig
        {
            Provider = "Webhook",
            Settings = new Dictionary<string, string>
            {
                ["Url"] = "https://example.com/webhook",
                ["Authorization"] = ""
            }
        };

        var payload = new NotificationPayload
        {
            Title = "Test",
            Body = "Test message"
        };

        await provider.SendAsync(client, config, payload);

        Assert.IsNotNull(handler.CapturedRequest);
        Assert.IsFalse(handler.CapturedRequest.Headers.Contains("Authorization"));
    }
}
