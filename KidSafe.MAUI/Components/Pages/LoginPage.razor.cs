using KidSafe.Shared.DTOs;

namespace KidSafe.MAUI.Components.Pages;

public partial class LoginPage
{
    record RoleOption(string Role, string Label, string IconClass, string Email, string Password);

    static readonly RoleOption[] _roleOptions =
    [
        new("Admin",   "Admin",   "bi-shield-fill",      "admin@kidsafe.app",  "Admin@123!"),
        new("Teacher", "Teacher", "bi-mortarboard-fill", "teacher@demo.com",   "Demo@123!"),
        new("Parent",  "Parent",  "bi-people-fill",      "parent@demo.com",    "Demo@123!"),
        new("Child",   "Student", "bi-person-fill",      "student@demo.com",   "Demo@123!"),
    ];

    string _email = "", _password = "", _error = "";
    bool   _busy, _showPwd;
    string? _selectedRole;

    protected override async Task OnInitializedAsync()
    {
        await AuthState.InitAsync();
        if (AuthState.IsAuthenticated && AuthState.CurrentUser != null)
            Redirect(AuthState.CurrentUser.Role);
    }

    void SelectRole(RoleOption r)
    {
        _selectedRole = r.Role;
        _email        = r.Email;
        _password     = r.Password;
        _error        = "";
    }

    async Task Submit()
    {
        if (string.IsNullOrWhiteSpace(_email) || string.IsNullOrWhiteSpace(_password))
        { _error = "Email and password are required."; return; }

        _busy = true; _error = "";
        StateHasChanged();

        var (data, error) = await Api.LoginAsync(new LoginRequest(_email, _password));

        if (data == null)
            _error = error;
        else if (data.Token == "pending")
            _error = "Your account is awaiting approval.";
        else if (data.Token == "disabled")
            _error = "Your account has been disabled. Contact your administrator.";
        else
        {
            await AuthState.SetUserAsync(data);
            Redirect(data.Role);
        }

        _busy = false;
        StateHasChanged();
    }

    void Redirect(string role) => Nav.NavigateTo(
        role == "Admin"   ? "/admin"      :
        role == "Child"   ? "/child-home" :
        role == "Teacher" ? "/teacher"    : "/dashboard",
        replace: true);
}
