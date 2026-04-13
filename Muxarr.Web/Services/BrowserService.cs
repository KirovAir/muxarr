using Blazored.Toast.Services;
using Microsoft.JSInterop;

namespace Muxarr.Web.Services;

public class BrowserService(IJSRuntime jsRuntime, IToastService toastService)
{
    public async Task CopyToClipboard(string text)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", text);
            toastService.ShowSuccess("Copied to clipboard.");
        }
        catch (JSException)
        {
            toastService.ShowError("Failed to copy to clipboard.");
        }
    }
}
