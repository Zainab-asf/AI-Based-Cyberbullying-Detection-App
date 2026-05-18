namespace KidSafe.MAUI.Components.Pages;

public partial class NotificationsPage
{
    class NoteItem
    {
        public int      Id        { get; set; }
        public string   Title     { get; set; } = "";
        public string   Body      { get; set; } = "";
        public string   Type      { get; set; } = "system";
        public bool     IsRead    { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    List<NoteItem> _notifications = new();
    bool _loading = true;
    bool IsDesktop => AuthState.CurrentUser?.Role is "Teacher" or "Admin" or "Parent";

    protected override async Task OnInitializedAsync()
    {
        await AuthState.InitAsync();
        if (!AuthState.IsAuthenticated) { Nav.NavigateTo("/login"); return; }
        await Load();
    }

    async Task Load()
    {
        _loading = true; StateHasChanged();
        _notifications = await Api.GetAsync<List<NoteItem>>("notifications") ?? new();
        _loading = false; StateHasChanged();
    }

    async Task ReadNote(NoteItem n)
    {
        if (!n.IsRead)
        {
            n.IsRead = true;
            await Api.PostAsync($"notifications/{n.Id}/read", new { });
            StateHasChanged();
        }
    }

    async Task MarkAllRead()
    {
        await Api.PostAsync("notifications/read-all", new { });
        foreach (var n in _notifications) n.IsRead = true;
        StateHasChanged();
    }

    static string NoteIcon(string type) => type switch
    {
        "alert"      => "bi-exclamation-triangle-fill",
        "assignment" => "bi-file-text-fill",
        "badge"      => "bi-trophy-fill",
        _            => "bi-info-circle-fill"
    };

    static string TimeAgo(DateTime dt)
    {
        var diff = DateTime.UtcNow - dt;
        if (diff.TotalMinutes < 1)  return "just now";
        if (diff.TotalHours   < 1)  return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalDays    < 1)  return $"{(int)diff.TotalHours}h ago";
        return $"{(int)diff.TotalDays}d ago";
    }

    void GoBack() => Nav.NavigateTo(AuthState.CurrentUser?.Role switch
    {
        "Admin"   => "/admin",
        "Teacher" => "/teacher",
        "Parent"  => "/dashboard",
        _         => "/child-home"
    });

    async Task Logout() { await AuthState.LogoutAsync(); Nav.NavigateTo("/login"); }
}
