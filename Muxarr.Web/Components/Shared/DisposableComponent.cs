using Microsoft.AspNetCore.Components;

namespace Muxarr.Web.Components.Shared;

public abstract class DisposableComponent : ComponentBase, IDisposable {
    public virtual void Dispose() {
        // This one is needed for possible event hooks we need to remove. 
    }

    public async Task InvokeStateHasChanged() {
        await InvokeAsync(StateHasChanged);
    }
}