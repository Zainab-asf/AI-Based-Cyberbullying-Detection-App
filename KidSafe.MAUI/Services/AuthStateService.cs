using System.Text.Json;
using KidSafe.Shared.DTOs;

#pragma warning disable CA1416

namespace KidSafe.MAUI.Services;

/// <summary>
/// MAUI version — uses Preferences instead of IJSRuntime localStorage.
/// </summary>
public class AuthStateService
{
    private const string Key = "kidsafe_user";

    public AuthResponse? CurrentUser { get; private set; }
    public event Action? OnChange;

    public Task InitAsync()
    {
        var json = Preferences.Default.Get<string?>(Key, null);
        if (!string.IsNullOrEmpty(json))
            CurrentUser = JsonSerializer.Deserialize<AuthResponse>(json);
        return Task.CompletedTask;
    }

    public Task SetUserAsync(AuthResponse user)
    {
        CurrentUser = user;
        Preferences.Default.Set(Key, JsonSerializer.Serialize(user));
        OnChange?.Invoke();
        return Task.CompletedTask;
    }

    public Task LogoutAsync()
    {
        CurrentUser = null;
        Preferences.Default.Remove(Key);
        OnChange?.Invoke();
        return Task.CompletedTask;
    }

    public bool IsAuthenticated   => CurrentUser != null
                                  && CurrentUser.Token is not ("pending" or "disabled")
                                  && !IsTokenExpired(CurrentUser.Token);
    public bool IsChild           => CurrentUser?.Role == "Child";
    public bool IsParentOrTeacher => CurrentUser?.Role is "Parent" or "Teacher";
    public bool IsAdmin           => CurrentUser?.Role == "Admin";

    public static bool IsTokenExpired(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return true;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += new string('=', (4 - payload.Length % 4) % 4);
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var exp))
                return DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()) < DateTimeOffset.UtcNow;
            return false;
        }
        catch { return true; }
    }

    /// <summary>Backend base URL for direct file links (uploads).</summary>
    public string BackendUrl { get; set; } = "http://localhost:5000";
}
