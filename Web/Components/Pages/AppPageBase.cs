using Microsoft.AspNetCore.Components;
using Web.Services;

namespace Web.Components.Pages
{
    public class AppPageBase : ComponentBase
    {
        [Inject] protected AuthStateService AuthState { get; set; } = null!;
        [Inject] protected ApiHttpClient ApiClient { get; set; } = null!;
        [Inject] protected NavigationManager Nav { get; set; } = null!;

        protected bool _initialized = false;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await AuthState.InitializeAsync();
                _initialized = true;
                await OnPageInitializedAsync();
                StateHasChanged();
            }
        }

        protected virtual Task OnPageInitializedAsync() => Task.CompletedTask;
    }
}
