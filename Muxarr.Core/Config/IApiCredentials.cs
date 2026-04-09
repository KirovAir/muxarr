namespace Muxarr.Core.Config;

public interface IApiCredentials
{
    string Url { get; }
    string ApiKey { get; }
}
