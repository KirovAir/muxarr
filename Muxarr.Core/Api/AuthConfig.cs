using System.Text.Json.Serialization;

namespace Muxarr.Core.Api;

public class AuthConfig
{
    [JsonIgnore]
    public const string Key = "Auth";

    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
}
