using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class TelegramSettings
{
    [Field("Bot Token", Type = FieldType.Password, HelpText = "Create a bot via @BotFather to get this token.")]
    public string BotToken { get; set; } = "";

    [Field("Chat ID", HelpText = "User, group, or channel ID to send messages to.")]
    public string ChatId { get; set; } = "";
}

public class TelegramProvider : NotificationProvider<TelegramSettings>
{
    public override string Icon => "bi-telegram";

    protected override Task SendCoreAsync(HttpClient client, TelegramSettings s, NotificationPayload payload)
    {
        // sendMessage caps text at 4096 characters after entity parsing.
        var text = Clip($"<b>{EscapeHtml(payload.Title)}</b>\n{EscapeHtml(payload.Body)}", 4096);

        return PostJsonAsync(client, $"https://api.telegram.org/bot{s.BotToken}/sendMessage", new
        {
            chat_id = s.ChatId,
            text,
            parse_mode = "HTML"
        });
    }
}
