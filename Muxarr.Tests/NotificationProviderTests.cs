using System.Net;
using System.Text.Json;
using Muxarr.Core.Config;
using Muxarr.Web.Services.Notifications;
using Muxarr.Web.Services.Notifications.Providers;

namespace Muxarr.Tests;

[TestClass]
public class NotificationProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static async Task<JsonElement> Send(NotificationProvider provider, Dictionary<string, string> settings)
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var config = new NotificationConfig { Provider = provider.Type, Settings = settings };
        var payload = new NotificationPayload
        {
            Title = "Conversion Completed",
            Body = "movie.mkv - saved 500 MB",
            EventType = NotificationEventType.Completed,
            FileName = "movie.mkv",
            FilePath = "/data/media/movie.mkv"
        };

        await provider.SendAsync(client, config, payload);

        Assert.IsNotNull(handler.Body);
        return JsonDocument.Parse(handler.Body).RootElement;
    }

    [TestMethod]
    public async Task Webhook_SendsFilePath()
    {
        var root = await Send(new WebhookProvider(), new() { ["Url"] = "https://example.com/hook" });

        Assert.AreEqual("/data/media/movie.mkv", root.GetProperty("filePath").GetString());
        Assert.AreEqual("movie.mkv", root.GetProperty("fileName").GetString());
    }

    [TestMethod]
    public async Task Telegram_TopicIdBecomesThreadId()
    {
        var root = await Send(new TelegramProvider(),
            new() { ["BotToken"] = "t", ["ChatId"] = "-100", ["TopicId"] = "42" });

        Assert.AreEqual(42, root.GetProperty("message_thread_id").GetInt64());
    }

    [TestMethod]
    public async Task Telegram_NoTopicIdOmitsThreadId()
    {
        var root = await Send(new TelegramProvider(), new() { ["BotToken"] = "t", ["ChatId"] = "-100" });

        Assert.IsFalse(root.TryGetProperty("message_thread_id", out _));
    }
}
