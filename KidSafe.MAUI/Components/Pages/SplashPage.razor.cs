namespace KidSafe.MAUI.Components.Pages;

public partial class SplashPage
{
    protected override async Task OnInitializedAsync()
    {
        await AuthState.InitAsync();

        // Show brand splash for a moment
        await Task.Delay(1000);

        // Switch to "connecting" phase
        _phase = SplashPhase.Connecting;
        StateHasChanged();

        await CheckAndNavigateAsync();
    }

    async Task CheckAndNavigateAsync()
    {
        var reachable = await Api.IsServerReachableAsync();

        if (!reachable)
        {
            _phase = SplashPhase.Offline;
            StateHasChanged();
            return;
        }

        // Server is up — proceed
        NavigateToDashboard();
    }

    async Task RetryAsync()
    {
        if (_retrying) return;
        _retrying = true;

        // Apply any URL change the user typed
        var newUrl = _serverUrlInput.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(newUrl) && newUrl != _serverUrl)
        {
            _serverUrl = newUrl;
            AuthState.BackendUrl = newUrl;
        }

        _phase = SplashPhase.Connecting;
        StateHasChanged();

        await Task.Delay(500); // brief visual pause
        var reachable = await Api.IsServerReachableAsync();

        if (!reachable)
        {
            _phase    = SplashPhase.Offline;
            _retrying = false;
            StateHasChanged();
            return;
        }

        _retrying = false;
        NavigateToDashboard();
    }

    void SkipToLogin()
    {
        Nav.NavigateTo("/login");
    }

    void NavigateToDashboard()
    {
        Nav.NavigateTo(!AuthState.IsAuthenticated ? "/login" :
            AuthState.CurrentUser?.Role switch
            {
                "Admin"   => "/admin",
                "Child"   => "/child-home",
                "Teacher" => "/teacher",
                _         => "/dashboard"
            });
    }
}
