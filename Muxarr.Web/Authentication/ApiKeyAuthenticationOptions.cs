using Microsoft.AspNetCore.Authentication;

namespace Muxarr.Web.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-Api-Key";
    public string QueryName { get; set; } = "apikey";
}
