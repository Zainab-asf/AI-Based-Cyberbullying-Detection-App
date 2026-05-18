namespace KidSafe.MAUI.Components.Pages;

public partial class ProfilePage
{
    record ProfileDto(int Id, string Email, string DisplayName, string Role, string? AvatarEmoji, string? RollNumber, string? Phone, string Status);
    record RewardData(int Points, string BadgeLevel, int SafeMessages);
    record ClassmatesResp(string? ClassName, string? Section, object[]? Classmates);
    record BadgeDef(string IconClass, string Color, string Name, string Desc, string BgColor, bool Locked);

    ProfileDto? _profile;
    RewardData? _reward;
    bool   _loading = true;

    string _editName = "", _editPhone = "";
    string _profileError = "";
    bool   _profileBusy;

    string _pwCurrent = "", _pwNew = "", _pwConfirm = "", _pwError = "";
    bool   _pwBusy;

    string _toast = ""; bool _toastOk;

    string _className = "", _section = "";
    string _selectedAvatar = "🧒";
    bool   _showOnline = true, _allowMessages = true, _shareProgress;

    static readonly BadgeDef[] _allBadges =
    [
        new("bi-heart-fill",          "#E74C3C", "Kind Communicator", "Send 30 kind messages",      "#FFE4E8", false),
        new("bi-shield-check-fill",   "#7C3AED", "Safe Chatter",      "7 days of safe chatting",    "#EDE9FE", false),
        new("bi-star-fill",           "#F1C40F", "Star Student",      "Earned 100 stars",           "#FEF9E7", false),
        new("bi-emoji-smile-fill",    "#2980B9", "Emoji Master",      "Used 20 positive emojis",    "#D6EAF8", false),
        new("bi-fire",                "#E67E22", "Streak Champion",   "5-day safe streak",          "#FDEBD0", false),
        new("bi-hand-thumbs-up-fill", "#8E44AD", "Helpful Friend",    "Helped 2 classmates",        "#E8DAEF", false),
        new("bi-feather",             "#1ABC9C", "Peacemaker",        "Resolved a conflict kindly", "#D5F5E3", false),
        new("bi-lightning-fill",      "#5D6D7E", "Super Communicator","Send 200 safe messages",     "#EAECEE", true),
        new("bi-cpu-fill",            "#5D6D7E", "Digital Hero",      "30-day safe streak",         "#EAECEE", true),
    ];

    static readonly string[] _avatarOptions =
    {
        "😊","😎","🦄","🐱","🐶","🦊","🐼","🐸","🦋","🌟","🎮","🚀",
        "🎵","🌈","🎨","⚽","🏀","🎯","🍕","🍦","🌺","🦁","🐢","🦖"
    };

    bool IsChild      => AuthState.CurrentUser?.Role == "Child";
    int  EarnedBadges => _allBadges.Count(b => !b.Locked);

    protected override async Task OnInitializedAsync()
    {
        await AuthState.InitAsync();
        if (!AuthState.IsAuthenticated) { Nav.NavigateTo("/login"); return; }

        _profile = await Api.GetAsync<ProfileDto>("auth/me");

        if (_profile != null)
        {
            _editName       = _profile.DisplayName;
            _editPhone      = _profile.Phone ?? "";
            _selectedAvatar = _profile.AvatarEmoji ?? "🧒";
        }

        if (IsChild)
        {
            var t1 = Api.GetAsync<RewardData>("rewards/my");
            var t2 = Api.GetAsync<ClassmatesResp>("classes/classmates");
            await Task.WhenAll(t1, t2);
            _reward    = t1.Result;
            _className = t2.Result?.ClassName ?? "";
            _section   = t2.Result?.Section   ?? "";
        }

        _loading = false;
    }

    async Task SaveProfile()
    {
        if (string.IsNullOrWhiteSpace(_editName)) { _profileError = "Name cannot be empty."; return; }
        _profileBusy = true; _profileError = ""; StateHasChanged();

        var ok = await Api.PatchAsync("auth/profile", new { DisplayName = _editName.Trim(), Phone = _editPhone.Trim() });
        _profileBusy = false;

        if (!ok) { _profileError = "Failed to save profile. Please try again."; StateHasChanged(); return; }

        _profile = await Api.GetAsync<ProfileDto>("auth/me");
        if (_profile != null) { _editName = _profile.DisplayName; _editPhone = _profile.Phone ?? ""; }
        ShowToast("Profile updated successfully!", true);
    }

    async Task ChangePassword()
    {
        _pwError = "";
        if (string.IsNullOrWhiteSpace(_pwCurrent)) { _pwError = "Current password is required."; return; }
        if (string.IsNullOrWhiteSpace(_pwNew))     { _pwError = "New password is required."; return; }
        if (_pwNew.Length < 6)                     { _pwError = "New password must be at least 6 characters."; return; }
        if (_pwNew != _pwConfirm)                  { _pwError = "Passwords do not match."; return; }

        _pwBusy = true; StateHasChanged();
        var ok = await Api.PatchAsync("auth/password", new { CurrentPassword = _pwCurrent, NewPassword = _pwNew });
        _pwBusy = false;

        if (!ok) { _pwError = "Failed to update password. Check your current password and try again."; StateHasChanged(); return; }

        _pwCurrent = _pwNew = _pwConfirm = "";
        ShowToast("Password updated successfully!", true);
    }

    async Task SaveAvatar()
    {
        var ok = await Api.PatchAsync("auth/avatar", new { Emoji = _selectedAvatar });
        ShowToast(ok ? "Avatar updated!" : "Failed to update avatar.", ok);
    }

    void ShowToast(string msg, bool ok)
    {
        _toast = msg; _toastOk = ok; StateHasChanged();
        Task.Delay(3000).ContinueWith(_ => { _toast = ""; InvokeAsync(StateHasChanged); });
    }

    void NavigateTo(string url) { Sidebar.CloseDrawer(); Nav.NavigateTo(url); }

    async Task Logout() { await AuthState.LogoutAsync(); Nav.NavigateTo("/login"); }

    void GoBack() => Nav.NavigateTo(AuthState.CurrentUser?.Role switch
    {
        "Admin"   => "/admin",
        "Teacher" => "/teacher",
        "Parent"  => "/dashboard",
        _         => "/child-home"
    });
}
