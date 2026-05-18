namespace KidSafe.MAUI.Components.Pages;

public partial class RewardsPage
{
    int _points;

    record B(string IconClass, string Color, string Name, int Pts, bool Unlocked);
    List<B> _badges = new();

    protected override async Task OnInitializedAsync()
    {
        await AuthState.InitAsync();
        if (!AuthState.IsAuthenticated) { Nav.NavigateTo("/login"); return; }
        _points = 0;
        _badges =
        [
            new("bi-chat-dots-fill",     "#7C3AED", "Safe Chatter",   100,   _points >= 100),
            new("bi-star-fill",          "#F1C40F", "Kind Star",      500,   _points >= 500),
            new("bi-lightning-fill",     "#2980B9", "Cyber Hero",     1000,  _points >= 1000),
            new("bi-mortarboard-fill",   "#8E44AD", "Chat Scholar",   2000,  _points >= 2000),
            new("bi-trophy-fill",        "#E67E22", "Safety King",    5000,  _points >= 5000),
            new("bi-rocket-takeoff-fill","#E74C3C", "Legend",         10000, _points >= 10000),
        ];
    }

    int BarWidth     => _badges.FirstOrDefault(b => !b.Unlocked) is { } next
        ? (int)Math.Min(100, (_points / (double)next.Pts) * 100) : 100;
    int PointsToNext => Math.Max(0, (_badges.FirstOrDefault(b => !b.Unlocked)?.Pts ?? 0) - _points);
}
