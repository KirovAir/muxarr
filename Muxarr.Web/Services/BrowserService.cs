using System.Text;
using Blazored.Toast.Services;
using Microsoft.JSInterop;

namespace Muxarr.Web.Services;

public class BrowserService(IJSRuntime jsRuntime, IToastService toastService)
{
    /// <summary>
    /// Hands the browser a text file to save (muxarrDownload in App.razor).
    /// </summary>
    public async Task DownloadText(string fileName, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var streamRef = new DotNetStreamReference(stream);
        await jsRuntime.InvokeVoidAsync("muxarrDownload", fileName, streamRef);
    }

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
