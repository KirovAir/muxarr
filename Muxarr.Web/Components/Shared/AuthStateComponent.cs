using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Muxarr.Web.Components.Shared;

public abstract class AuthStateComponent : DisposableComponent {
    [CascadingParameter]
    public AuthenticationState? AuthState { get; set; }

    [Inject]
    public AuthenticationStateProvider? AuthenticationStateProvider { get; set; }

    protected override async Task OnInitializedAsync() {
        if (AuthenticationStateProvider == null) {
            return;
        }

        AuthState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        AuthenticationStateProvider.AuthenticationStateChanged += AuthStateHasChanged;
    }

    private void AuthStateHasChanged(Task<AuthenticationState> task) {
        AuthState = task.Result;
        _ = InvokeStateHasChanged();
    }

    public override void Dispose() {
        if (AuthenticationStateProvider == null) {
            return;
        }

        AuthenticationStateProvider.AuthenticationStateChanged -= AuthStateHasChanged;
        base.Dispose();
    }
}