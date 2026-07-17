using System.Text.Json.Serialization;

namespace Muxarr.Core.Config;

public class AuthConfig
{
    [JsonIgnore]
    public const string Key = "Auth";

    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool TrustLocalNetwork { get; set; }
}
