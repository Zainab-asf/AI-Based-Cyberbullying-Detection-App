using KidSafe.Shared.DTOs;

namespace KidSafe.MAUI.Components.Pages;

public partial class AdminPanel
{
    record UserDto(int Id, string DisplayName, string Email, string Role, string Status);
    record ComplaintDto(int Id, string Description, string Status, DateTime Timestamp);
    record ClassDto(int Id, string Name, string Section, string Subject, string? Teacher, int TeacherId, int StudentCount);
    record ParentLinkDto(int ParentId, string ParentName, string ParentEmail,
                         int ChildId,  string ChildName,  string ChildEmail, DateTime LinkedAt);

    DashboardStats?          _stats;
    List<UserDto>            _allUsers        = new();
    List<UserDto>            _pendingUsers    = new();
    List<FlaggedMessageItem> _flaggedMessages = new();
    List<ComplaintDto>       _complaints      = new();
    List<ClassDto>           _classes         = new();
    List<ParentLinkDto>      _parentLinks     = new();
    bool   _loading = true;
    bool   _drawerOpen;
    string _activeTab = "Pending";
    string _toast     = "";
    string _aiStatus  = "checking…";

    static readonly (string tab, string icon, string label)[] _tabs =
    {
        ("Pending",    "bi-hourglass-split",         "Pending"),
        ("Students",   "bi-person-fill",              "Students"),
        ("Teachers",   "bi-mortarboard-fill",          "Teachers"),
        ("Parents",    "bi-people-fill",               "Parents"),
        ("Flagged",    "bi-exclamation-triangle-fill", "Flagged"),
        ("Complaints", "bi-clipboard2-fill",           "Complaints"),
        ("Classes",    "bi-journal-bookmark-fill",     "Classes"),
        ("Links",      "bi-link-45deg",                "Links")
    };

    bool   _showCreate, _createBusy, _createDone;
    string _createRole = "Parent", _createName = "", _createEmail = "", _createPassword = "", _createError = "";

    static readonly (string role, string icon, string label)[] _roles =
    {
        ("Parent",  "bi-people-fill",    "Parent"),
        ("Teacher", "bi-mortarboard-fill","Teacher")
    };

    bool   _showCreateStudent, _stuBusy, _stuDone;
    string _stuName = "", _stuEmail = "", _stuPassword = "", _stuRoll = "", _stuError = "";
    string _stuParentMode = "existing";
    int    _stuClassId, _stuExistingParentId;
    string _stuParentName = "", _stuParentEmail = "", _stuParentPassword = "";

    bool   _showCreateClass, _clsBusy, _clsOk;
    string _clsName = "", _clsSection = "", _clsSubject = "", _clsMsg = "";
    int    _clsTeacherId;

    ClassDto? _manageClass;
    int       _addStudentId;
    string    _classMsg = "";

    bool   _showLinkModal, _linkBusy, _linkOk;
    int    _linkParentId, _linkChildId;
    string _linkMsg = "";

    protected override async Task OnInitializedAsync()
    {
        await AuthState.InitAsync();
        if (!AuthState.IsAuthenticated || !AuthState.IsAdmin) { Nav.NavigateTo("/login"); return; }
        await LoadData();
    }

    async Task LoadData()
    {
        _loading = true; StateHasChanged();
        _stats           = await Api.GetDashboardStatsAsync();
        _allUsers        = await Api.GetAsync<List<UserDto>>("admin/users") ?? new();
        _pendingUsers    = await Api.GetAsync<List<UserDto>>("admin/users/pending") ?? new();
        _flaggedMessages = await Api.GetFlaggedMessagesAsync();
        _complaints      = await Api.GetAsync<List<ComplaintDto>>("reports/complaint") ?? new();
        _classes         = await Api.GetAsync<List<ClassDto>>("classes") ?? new();
        _parentLinks     = await Api.GetAsync<List<ParentLinkDto>>("admin/parent-links") ?? new();
        _aiStatus        = await Api.GetAiStatusAsync();
        _loading = false; StateHasChanged();
    }

    async Task RefreshUsers()
    {
        _allUsers    = await Api.GetAsync<List<UserDto>>("admin/users") ?? new();
        _pendingUsers = await Api.GetAsync<List<UserDto>>("admin/users/pending") ?? new();
        StateHasChanged();
    }

    async Task RefreshClasses()
    {
        _classes = await Api.GetAsync<List<ClassDto>>("classes") ?? new();
        StateHasChanged();
    }

    async Task RefreshLinks()
    {
        _parentLinks = await Api.GetAsync<List<ParentLinkDto>>("admin/parent-links") ?? new();
        StateHasChanged();
    }

    async Task RefreshComplaints()
    {
        _complaints = await Api.GetAsync<List<ComplaintDto>>("reports/complaint") ?? new();
        StateHasChanged();
    }

    async Task SetTab(string tab)
    {
        _activeTab = tab;
        if (_allUsers.Count == 0) await LoadData();
        else StateHasChanged();
    }

    void OpenCreateClass()
    {
        _clsName = _clsSection = _clsSubject = _clsMsg = "";
        _clsTeacherId = 0;
        _showCreateClass = true;
    }

    async Task CreateClass()
    {
        if (string.IsNullOrWhiteSpace(_clsName)) { _clsMsg = "Name required."; _clsOk = false; return; }
        _clsBusy = true; StateHasChanged();
        var ok = await Api.PostAsync("classes", new
        {
            Name      = _clsName,
            Section   = _clsSection,
            Subject   = _clsSubject,
            TeacherId = _clsTeacherId == 0 ? (int?)null : _clsTeacherId
        });
        _clsBusy = false;
        _clsOk   = ok;
        _clsMsg  = ok ? "✅ Class created!" : "❌ Failed to create class.";
        if (ok) { await RefreshClasses(); await Task.Delay(1500); _showCreateClass = false; }
        StateHasChanged();
    }

    async Task DeleteClass(int id)
    {
        if (await Api.DeleteAsync($"classes/{id}"))
        { _toast = "🗑 Class deleted."; await RefreshClasses(); ShowToast(); }
    }

    void ManageClass(ClassDto cl) { _manageClass = cl; _addStudentId = 0; _classMsg = ""; }

    async Task AddStudentToClass()
    {
        if (_manageClass == null || _addStudentId == 0) return;
        var ok = await Api.PostAsync($"classes/{_manageClass.Id}/students", new { StudentId = _addStudentId });
        _classMsg = ok ? "✅ Student added!" : "❌ Already enrolled or failed.";
        if (ok) await RefreshClasses();
        StateHasChanged();
    }

    async Task LinkParentChild()
    {
        if (_linkParentId == 0 || _linkChildId == 0) { _linkMsg = "Select both users."; _linkOk = false; return; }
        _linkBusy = true; StateHasChanged();
        var ok = await Api.PostAsync("admin/parent-links", new { ParentId = _linkParentId, ChildId = _linkChildId });
        _linkBusy = false;
        _linkOk   = ok;
        _linkMsg  = ok ? "✅ Linked!" : "❌ Already linked or invalid.";
        if (ok) { await RefreshLinks(); await Task.Delay(1500); _showLinkModal = false; }
        StateHasChanged();
    }

    async Task UnlinkParentChild(int parentId, int childId)
    {
        if (await Api.DeleteAsync($"admin/parent-links/{parentId}/{childId}"))
        { _toast = "🔗 Link removed."; await RefreshLinks(); ShowToast(); }
    }

    async Task Approve(int id)
    {
        if (await Api.PostAsync($"admin/users/{id}/approve", new { }))
        { _toast = "✅ Account approved"; await RefreshUsers(); }
        ShowToast();
    }

    async Task Disable(int id)
    {
        if (await Api.PostAsync($"admin/users/{id}/disable", new { }))
        { _toast = "🚫 Account disabled"; await RefreshUsers(); }
        ShowToast();
    }

    async Task UpdateComplaint(int id, string status)
    {
        await Api.PatchAsync($"reports/complaint/{id}", new { Status = status });
        _toast = status == "resolved" ? "✅ Resolved" : "📋 Marked under review";
        await RefreshComplaints();
        ShowToast();
    }

    void OpenCreate(string role)  { _createRole = role; _createError = ""; _createDone = false; _showCreate = true; }
    void OpenCreateTeacher() => OpenCreate("Teacher");
    void OpenCreateParent()  => OpenCreate("Parent");
    void CloseCreate()
    {
        _showCreate = false;
        _createName = _createEmail = _createPassword = "";
        _createError = ""; _createDone = false;
    }

    void OpenCreateChild()
    {
        _stuName = _stuEmail = _stuPassword = _stuRoll = _stuError = "";
        _stuParentName = _stuParentEmail = _stuParentPassword = "";
        _stuParentMode = "existing";
        _stuClassId = _stuExistingParentId = 0;
        _stuDone = false;
        _showCreateStudent = true;
    }

    void CloseCreateStudent()
    {
        _showCreateStudent = false;
        _stuName = _stuEmail = _stuPassword = _stuRoll = _stuError = "";
        _stuParentName = _stuParentEmail = _stuParentPassword = "";
        _stuDone = false;
    }

    async Task CreateStudent()
    {
        if (string.IsNullOrWhiteSpace(_stuName))     { _stuError = "Student name is required."; return; }
        if (string.IsNullOrWhiteSpace(_stuEmail))    { _stuError = "Student email is required."; return; }
        if (string.IsNullOrWhiteSpace(_stuPassword)) { _stuError = "Password is required (min 6 chars)."; return; }
        if (string.IsNullOrWhiteSpace(_stuRoll))     { _stuError = "Roll number is required."; return; }
        if (_stuClassId == 0)                        { _stuError = "Please select a class."; return; }

        if (_stuParentMode == "existing" && _stuExistingParentId == 0)
            { _stuError = "Please select an existing parent account."; return; }
        if (_stuParentMode == "new")
        {
            if (string.IsNullOrWhiteSpace(_stuParentName))     { _stuError = "Parent name is required."; return; }
            if (string.IsNullOrWhiteSpace(_stuParentEmail))    { _stuError = "Parent email is required."; return; }
            if (string.IsNullOrWhiteSpace(_stuParentPassword)) { _stuError = "Parent password is required."; return; }
        }

        _stuBusy = true; _stuError = ""; StateHasChanged();

        var payload = new
        {
            Name             = _stuName.Trim(),
            Email            = _stuEmail.Trim(),
            Password         = _stuPassword,
            RollNumber       = _stuRoll.Trim(),
            ClassId          = _stuClassId,
            ExistingParentId = _stuParentMode == "existing" ? _stuExistingParentId : (int?)null,
            ParentName       = _stuParentMode == "new" ? _stuParentName.Trim()     : (string?)null,
            ParentEmail      = _stuParentMode == "new" ? _stuParentEmail.Trim()    : (string?)null,
            ParentPassword   = _stuParentMode == "new" ? _stuParentPassword        : (string?)null,
        };

        var (ok, error) = await Api.PostWithErrorAsync("admin/students", payload);
        _stuBusy = false;

        if (!ok) { _stuError = string.IsNullOrEmpty(error) ? "Failed to create student." : error; StateHasChanged(); return; }

        _stuDone = true;
        await RefreshUsers();
        await Task.Delay(2000);
        CloseCreateStudent();
    }

    Task MarkUnderReview(int id) => UpdateComplaint(id, "underReview");
    Task MarkResolved(int id)    => UpdateComplaint(id, "resolved");

    async Task CreateUser()
    {
        if (string.IsNullOrWhiteSpace(_createEmail) || string.IsNullOrWhiteSpace(_createPassword) || string.IsNullOrWhiteSpace(_createName))
        { _createError = "All fields are required."; return; }

        _createBusy = true; _createError = "";
        var ok = await Api.PostAsync("admin/users", new
        {
            Email       = _createEmail.Trim(),
            Password    = _createPassword,
            DisplayName = _createName.Trim(),
            Role        = _createRole
        });
        _createBusy = false;

        if (!ok) { _createError = "Failed. Email may already be taken."; return; }

        _createDone = true;
        _createName = _createEmail = _createPassword = "";
        await RefreshUsers();
        await Task.Delay(2000);
        CloseCreate();
    }

    void ShowToast() { StateHasChanged(); _ = ClearToastAfterDelayAsync(); }

    private async Task ClearToastAfterDelayAsync()
    {
        await Task.Delay(3000);
        _toast = "";
        await InvokeAsync(StateHasChanged);
    }

    static string RoleIconClass(string role) => role switch
    {
        "Child"   => "bi-person-fill",
        "Parent"  => "bi-people-fill",
        "Teacher" => "bi-mortarboard-fill",
        "Admin"   => "bi-shield-fill",
        _         => "bi-person-circle"
    };

    static string StatusBadge(string s) => s switch
    {
        "resolved"    => "badge-green",
        "underReview" => "badge-gold",
        _             => "badge-blue"
    };

    async Task Logout() { await AuthState.LogoutAsync(); Nav.NavigateTo("/login"); }
}
