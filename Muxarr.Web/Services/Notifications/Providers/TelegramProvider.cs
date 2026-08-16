using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class TelegramSettings
{
    [Field("Bot Token", Type = FieldType.Password, HelpText = "Create a bot via @BotFather to get this token.")]
    public string BotToken { get; set; } = "";

    [Field("Chat ID", HelpText = "User, group, or channel ID to send messages to.")]
    public string ChatId { get; set; } = "";

    [Field("Topic ID", HelpText = "Optional. Thread ID for sending to a specific forum topic.")]
    public string TopicId { get; set; } = "";
}

public class TelegramProvider : NotificationProvider<TelegramSettings>
{
    public override string Icon => "bi-telegram";

    protected override Task SendCoreAsync(HttpClient client, TelegramSettings s, NotificationPayload payload)
    {
        // sendMessage caps text at 4096 characters after entity parsing.
        var text = Clip($"<b>{EscapeHtml(payload.Title)}</b>\n{EscapeHtml(payload.Body)}", 4096);

        // message_thread_id must be an integer and only included when set; omit it for
        // non-forum chats.
        var body = new Dictionary<string, object?>
        {
            ["chat_id"] = s.ChatId,
            ["text"] = text,
            ["parse_mode"] = "HTML"
        };

        if (long.TryParse(s.TopicId, out var threadId))
        {
            body["message_thread_id"] = threadId;
        }

        return PostJsonAsync(client, $"https://api.telegram.org/bot{s.BotToken}/sendMessage", body);
    }
}
