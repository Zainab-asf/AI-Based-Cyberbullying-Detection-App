namespace KidSafe.MAUI.Components.Pages;

public partial class ChildDashboard
{
    record RewardDto(int Points, string BadgeLevel, int SafeMessages, string? Badges);
    record ClassmatesResponse(string? ClassName, string? Section, object[]? Classmates);
    record BadgeDef(string IconClass, string Color, string Name, string BgColor, bool Locked);

    RewardDto? _reward;
    string _className = "", _section = "";
    int _unreadCount, _streak;

    static readonly BadgeDef[] _badges =
    [
        new("bi-heart-fill",         "#E74C3C", "Kind Communicator", "#FFE4E8", false),
        new("bi-shield-check-fill",  "#7C3AED", "Safe Chatter",      "#EDE9FE", false),
        new("bi-star-fill",          "#F1C40F", "Star Student",      "#FEF9E7", false),
        new("bi-emoji-smile-fill",   "#2980B9", "Emoji Master",      "#D6EAF8", false),
        new("bi-fire",               "#E67E22", "Streak Champion",   "#FDEBD0", false),
        new("bi-hand-thumbs-up-fill","#8E44AD", "Helpful Friend",    "#E8DAEF", false),
        new("bi-feather",            "#1ABC9C", "Peacemaker",        "#D5F5E3", false),
        new("bi-lightning-fill",     "#5D6D7E", "Super Communicator","#EAECEE", true),
        new("bi-cpu-fill",           "#5D6D7E", "Digital Hero",      "#EAECEE", true),
    ];

    static readonly string[] _tips =
    [
        "Kind words are like sunshine — they make everyone feel warm and happy!",
        "Being safe online means thinking before you type. Is it kind, true, and necessary?",
        "If something makes you feel uncomfortable online, tell a trusted adult right away!",
        "You earn stars for every safe message — keep chatting kindly to unlock new badges.",
        "Remember: behind every screen is a real person with real feelings. Be kind!",
        "Great friends support each other. Share something positive with your classmates today!"
    ];

    string Greeting         => DateTime.Now.Hour < 12 ? "Good morning!" : DateTime.Now.Hour < 17 ? "Good afternoon!" : "Good evening!";
    string FirstName        => AuthState.CurrentUser?.DisplayName?.Split(' ').First() ?? "Student";
    int    NextMilestone    => (_reward?.Points ?? 0) < 100 ? 100 : (_reward?.Points ?? 0) < 250 ? 250 : (_reward?.Points ?? 0) < 500 ? 500 : 1000;
    int    ProgressPct      => _reward is null ? 0 : (int)Math.Min(100, _reward.Points * 100.0 / NextMilestone);
    string NextBadge        => NextMilestone == 100 ? "Star Student" : NextMilestone == 250 ? "Streak Champion" : "Super Communicator";
    int    EarnedBadgeCount => _badges.Count(b => !b.Locked);
    string CurrentTip       => _tips[DateTime.Today.DayOfYear % _tips.Length];

    void NavigateTo(string url) { Sidebar.CloseDrawer(); Nav.NavigateTo(url); }

    protected override async Task OnInitializedAsync()
    {
        await AuthState.InitAsync();
        if (!AuthState.IsAuthenticated || !AuthState.IsChild) { Nav.NavigateTo("/login"); return; }

        var t1 = Api.GetAsync<RewardDto>("rewards/my");
        var t2 = Api.GetAsync<ClassmatesResponse>("classes/classmates");
        var t3 = Api.GetAsync<int>("notifications/unread-count");
        await Task.WhenAll(t1, t2, t3);

        _reward      = t1.Result;
        _className   = t2.Result?.ClassName ?? "";
        _section     = t2.Result?.Section ?? "";
        _unreadCount = t3.Result;
        _streak      = Math.Min(7, (_reward?.SafeMessages ?? 0) / 5);
    }
}
