using KidSafe.Shared.DTOs;
using Microsoft.AspNetCore.Components.Web;

namespace KidSafe.MAUI.Components.Pages;

public partial class ClassesPage
{
    record ClassItem(int Id, string Name, string Section, string Subject,
                     string? Teacher, int TeacherId, int StudentCount,
                     int ContentCount, DateTime CreatedAt);

    class ClassDetail
    {
        public int    Id        { get; set; }
        public string Name      { get; set; } = "";
        public string Section   { get; set; } = "";
        public string Subject   { get; set; } = "";
        public string? Teacher  { get; set; }
        public int    TeacherId { get; set; }
        public List<StudentItem> Students { get; set; } = new();
    }

    record StudentItem(int Id, string DisplayName, string Email);
    record ContentItem(int Id, string Title, string? Description, string Type,
                       string? FilePath, DateTime? DueDate, int MaxPoints, DateTime CreatedAt);
    record ClassSendResult(int Id, string Label, double Score, DateTime Timestamp);
    record UnreadCount(int Count);

    bool _loading = true;
    List<ClassItem> _classes = new();
    ClassDetail? _activeClass;
    string _tab = "chat";
    int _unreadCount;

    List<ClassChatMessage> _classMsgs = new();
    string _chatInput = "", _chatToast = "", _chatToastClass = "", _typingUser = "";
    bool _chatSending;

    List<ContentItem> _contentItems = new();
    bool _contentLoading;

    string _uploadTitle = "", _uploadDesc = "", _uploadType = "Note",
           _uploadUrl = "", _uploadMsg = "";
    DateTime? _uploadDue;
    int _uploadMaxPts = 100;
    bool _uploading, _uploadOk;

    bool IsDesktop => true;
    bool IsTeacher => AuthState.CurrentUser?.Role == "Teacher";

    protected override async Task OnInitializedAsync()
    {
        await AuthState.InitAsync();
        if (!AuthState.IsAuthenticated) { Nav.NavigateTo("/login"); return; }

        Hub.OnClassMessageReceived += OnClassMsg;
        Hub.OnClassUserTyping      += OnClassTyping;
        Hub.OnStateChanged         += s => InvokeAsync(StateHasChanged);
        await Hub.StartAsync(AuthState.CurrentUser!.Token);

        await LoadClasses();
    }

    async Task LoadClasses()
    {
        _loading = true; StateHasChanged();
        _classes     = await Api.GetAsync<List<ClassItem>>("classes") ?? new();
        _unreadCount = (await Api.GetAsync<UnreadCount>("notifications/unread-count"))?.Count ?? 0;
        _loading = false; StateHasChanged();
    }

    async Task OpenClass(ClassItem cls)
    {
        _activeClass = await Api.GetAsync<ClassDetail>($"classes/{cls.Id}");
        _tab         = "chat";
        _classMsgs   = await Api.GetAsync<List<ClassChatMessage>>(
                           $"classes/{cls.Id}/messages?take=50") ?? new();
        await Hub.JoinClassAsync(cls.Id);
        StateHasChanged();
    }

    async Task BackToClasses()
    {
        if (_activeClass != null) await Hub.LeaveClassAsync(_activeClass.Id);
        _activeClass = null; StateHasChanged();
    }

    async Task SetTab(string tab)
    {
        _tab = tab;
        if (tab == "content" && _activeClass != null)
        {
            _contentLoading = true; StateHasChanged();
            _contentItems = await Api.GetAsync<List<ContentItem>>(
                $"content/class/{_activeClass.Id}") ?? new();
            _contentLoading = false;
        }
        StateHasChanged();
    }

    void OnClassMsg(ClassChatMessage m)
    {
        if (_activeClass != null && m.ClassId == _activeClass.Id)
        {
            _classMsgs.Add(m);
            InvokeAsync(StateHasChanged);
        }
    }

    void OnClassTyping(int classId, string name)
    {
        if (_activeClass?.Id != classId) return;
        _typingUser = name;
        InvokeAsync(StateHasChanged);
        _ = ClearTypingAfterDelayAsync();
    }

    private async Task ClearTypingAfterDelayAsync()
    {
        await Task.Delay(3000);
        _typingUser = "";
        await InvokeAsync(StateHasChanged);
    }

    async Task OnChatKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") { await SendClassMsg(); return; }
        if (_activeClass != null) await Hub.SendClassTypingAsync(_activeClass.Id);
    }

    async Task SendClassMsg()
    {
        if (string.IsNullOrWhiteSpace(_chatInput) || _chatSending || _activeClass == null) return;
        _chatSending = true;
        var text = _chatInput; _chatInput = "";

        var local = new ClassChatMessage
        {
            ClassId     = _activeClass.Id,
            SenderId    = AuthState.CurrentUser!.UserId,
            SenderName  = AuthState.CurrentUser.DisplayName,
            SenderEmoji = AuthState.CurrentUser.AvatarEmoji ?? "😊",
            Content     = text,
            Label       = "pending"
        };
        _classMsgs.Add(local); StateHasChanged();

        var resp = await Api.PostRawAsync<ClassSendResult>(
            $"messages/class/{_activeClass.Id}/send", new { Content = text });

        _classMsgs.Remove(local);
        if (resp != null)
        {
            local.Label   = resp.Label;
            local.Content = resp.Label == "Watch"  ? "[Message masked]"
                          : resp.Label == "Review" ? "[Message blocked]"
                          : text;
            _classMsgs.Add(local);

            (_chatToast, _chatToastClass) = resp.Label switch
            {
                "Safe"   => ("Sent!", "t-safe"),
                "Watch"  => ("Flagged and masked.", "t-warn"),
                "Review" => ("Blocked — please be kind!", "t-block"),
                _        => ("", "")
            };
        }

        _chatSending = false; StateHasChanged();
        await Task.Delay(3000);
        _chatToast = ""; StateHasChanged();
    }

    async Task UploadContent()
    {
        if (_activeClass == null || string.IsNullOrWhiteSpace(_uploadTitle)) return;
        _uploading = true; _uploadMsg = ""; StateHasChanged();

        var ok = await Api.PostAsync("content", new
        {
            ClassId     = _activeClass.Id,
            Title       = _uploadTitle,
            Description = _uploadDesc,
            Type        = _uploadType,
            LinkUrl     = _uploadUrl,
            DueDate     = _uploadDue,
            MaxPoints   = _uploadMaxPts
        });

        _uploadOk  = ok;
        _uploadMsg = ok ? "Uploaded successfully!" : "Upload failed.";
        _uploading = false;
        if (ok) { _uploadTitle = ""; _uploadDesc = ""; _uploadUrl = ""; }
        StateHasChanged();
    }

    string BubbleClass(ClassChatMessage m) =>
        m.Label switch { "Watch" => "flagged", "Review" => "blocked", "pending" => "pending", _ => "" };

    static string ContentIcon(string type) => type switch
    {
        "PDF"          => "bi-file-pdf-fill",
        "Assignment"   => "bi-file-text-fill",
        "Announcement" => "bi-megaphone-fill",
        "Link"         => "bi-link-45deg",
        _              => "bi-clipboard2-fill"
    };

    string GetBaseUrl() => AuthState.BackendUrl ?? "http://localhost:5000";

    void GoBack() => NavToDashboard();

    void NavToDashboard() => Nav.NavigateTo(AuthState.CurrentUser?.Role switch
    {
        "Admin"   => "/admin",
        "Teacher" => "/teacher",
        "Parent"  => "/dashboard",
        _         => "/child-home"
    });

    async Task Logout() { await AuthState.LogoutAsync(); Nav.NavigateTo("/login"); }

    public async ValueTask DisposeAsync()
    {
        Hub.OnClassMessageReceived -= OnClassMsg;
        Hub.OnClassUserTyping      -= OnClassTyping;
        if (_activeClass != null) await Hub.LeaveClassAsync(_activeClass.Id);
    }
}
