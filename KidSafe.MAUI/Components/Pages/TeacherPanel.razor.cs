using KidSafe.Shared.DTOs;

namespace KidSafe.MAUI.Components.Pages;

public partial class TeacherPanel
{
    record ChildSummary(string Name, string Avatar, int FlagCount);

    DashboardStats?          _stats;
    List<FlaggedMessageItem> _flaggedMessages = new();
    List<ChildSummary>       _children        = new();
    bool   _loading = true;
    bool   _drawerOpen;
    string _toast   = "";

    protected override async Task OnInitializedAsync()
    {
        await AuthState.InitAsync();
        if (!AuthState.IsAuthenticated || AuthState.CurrentUser?.Role != "Teacher")
        { Nav.NavigateTo("/login"); return; }
        await LoadData();
    }

    async Task LoadData()
    {
        _loading = true; StateHasChanged();
        _stats           = await Api.GetDashboardStatsAsync();
        _flaggedMessages = await Api.GetFlaggedMessagesAsync();

        var avatars = new[] { "😊","🎮","🦄","🌟","🎨","🐼","🦊","🐸","🦁","🐧" };
        var rng     = new Random(42);
        _children   = _flaggedMessages
            .GroupBy(f => f.SenderName)
            .Select(g => new ChildSummary(g.Key, avatars[rng.Next(avatars.Length)], g.Count()))
            .OrderByDescending(c => c.FlagCount)
            .ToList();

        if (!_children.Any())
            _children.Add(new ChildSummary("No incidents", "✅", 0));

        _loading = false; StateHasChanged();
    }

    async Task FileComplaint(FlaggedMessageItem m)
    {
        var ok = await Api.FileComplaintAsync($"Incident: {m.SenderName} — {m.Label} (score {m.Score:P0})");
        _toast = ok ? "📝 Complaint filed successfully" : "Failed to file complaint";
        StateHasChanged();
        await Task.Delay(3000);
        _toast = ""; StateHasChanged();
    }

    async Task Logout() { await AuthState.LogoutAsync(); Nav.NavigateTo("/login"); }
}
