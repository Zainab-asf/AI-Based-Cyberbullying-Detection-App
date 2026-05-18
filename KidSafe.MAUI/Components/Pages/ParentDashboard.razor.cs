using KidSafe.Shared.DTOs;

namespace KidSafe.MAUI.Components.Pages;

public partial class ParentDashboard
{
    record ChildItem(int ChildId, string Name, string Email, string? Avatar, string Status);
    record LiveAlert(string SenderName, string MaskedMessage, string Label, DateTime Time);
    record ChildActivity(List<FlagActivity> Flagged);
    record FlagActivity(int Id, string MaskedMessage, string Label, double Score, DateTime Timestamp);

    DashboardStats?          _stats;
    List<FlaggedMessageItem> _flaggedMessages = new();
    List<LiveAlert>          _liveAlerts      = new();
    List<ChildItem>          _children        = new();
    string _filter = "All";
    bool   _loading = true;
    bool   _drawerOpen;
    string _fcmToast = "";
    int    _selectedChildId = 0;

    IEnumerable<FlaggedMessageItem> FilteredMessages =>
        _filter == "All" ? _flaggedMessages : _flaggedMessages.Where(m => m.Label == _filter);

    void ShowAllChildren() { _selectedChildId = 0; _filter = "All"; StateHasChanged(); }
    void ShowAll()         { _filter = "All";    StateHasChanged(); }
    void ShowWatch()       { _filter = "Watch";  StateHasChanged(); }
    void ShowReview()      { _filter = "Review"; StateHasChanged(); }

    async Task SelectChild(int childId)
    {
        _selectedChildId = childId;
        _loading = true; StateHasChanged();
        var activity = await Api.GetAsync<ChildActivity>($"parent/children/{childId}/activity");
        if (activity != null)
            _flaggedMessages = activity.Flagged.Select(f => new FlaggedMessageItem
            {
                Id            = f.Id,
                SenderName    = _children.FirstOrDefault(c => c.ChildId == childId)?.Name ?? "Child",
                SenderId      = childId,
                MaskedMessage = f.MaskedMessage,
                Label         = f.Label,
                Score         = f.Score,
                Timestamp     = f.Timestamp
            }).ToList();
        _loading = false; StateHasChanged();
    }

    protected override async Task OnInitializedAsync()
    {
        await AuthState.InitAsync();
        if (!AuthState.IsAuthenticated || !AuthState.IsParentOrTeacher) { Nav.NavigateTo("/login"); return; }
        Hub.OnFlaggedAlert      += OnAlert;
        Fcm.OnForegroundMessage += OnFcmToast;
        await Hub.StartAsync(AuthState.CurrentUser!.Token);
        await Hub.JoinParentRoomAsync();
        await Fcm.InitAsync();
        await LoadData();
    }

    async Task LoadData()
    {
        _loading = true; StateHasChanged();
        _stats           = await Api.GetDashboardStatsAsync();
        _flaggedMessages = await Api.GetFlaggedMessagesAsync();
        _children        = await Api.GetAsync<List<ChildItem>>("parent/children") ?? new();
        _loading = false; StateHasChanged();
    }

    void OnAlert(int _, string name, string masked, string label, double score)
    {
        _liveAlerts.Add(new(name, masked, label, DateTime.UtcNow));
        if (_liveAlerts.Count > 20) _liveAlerts.RemoveAt(0);
        if (_stats != null)
        {
            if (label == "Watch")  _stats.TotalWatch++;
            else if (label == "Review") _stats.TotalReview++;
            _stats.TotalFlagged = _stats.TotalWatch + _stats.TotalReview;
        }
        InvokeAsync(StateHasChanged);
    }

    void OnFcmToast(string title, string body, string label)
    {
        _fcmToast = $"{title}: {body}";
        InvokeAsync(StateHasChanged);
        _ = ClearFcmToastAfterDelayAsync();
    }

    private async Task ClearFcmToastAfterDelayAsync()
    {
        await Task.Delay(5000);
        _fcmToast = "";
        await InvokeAsync(StateHasChanged);
    }

    async Task Logout() { await AuthState.LogoutAsync(); Nav.NavigateTo("/login"); }

    public async ValueTask DisposeAsync()
    {
        Hub.OnFlaggedAlert      -= OnAlert;
        Fcm.OnForegroundMessage -= OnFcmToast;
        Fcm.Dispose();
        await Hub.DisposeAsync();
    }
}
