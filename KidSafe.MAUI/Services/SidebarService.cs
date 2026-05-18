#pragma warning disable CA1416

namespace KidSafe.MAUI.Services;

/// <summary>
/// Manages sidebar UI state: drawer (mobile), collapsed (desktop), dark mode.
/// Dark mode and collapsed state are persisted across sessions via MAUI Preferences.
/// </summary>
public class SidebarService
{
    private const string DarkModeKey  = "sb_dark";
    private const string CollapsedKey = "sb_collapsed";

    public bool IsDarkMode   { get; private set; }
    public bool IsCollapsed  { get; private set; }
    public bool IsDrawerOpen { get; private set; }

    public event Action? OnChange;

    public void Init()
    {
        IsDarkMode  = Preferences.Default.Get(DarkModeKey,  false);
        IsCollapsed = Preferences.Default.Get(CollapsedKey, false);
    }

    public void ToggleDarkMode()
    {
        IsDarkMode = !IsDarkMode;
        Preferences.Default.Set(DarkModeKey, IsDarkMode);
        OnChange?.Invoke();
    }

    public void ToggleCollapsed()
    {
        IsCollapsed = !IsCollapsed;
        Preferences.Default.Set(CollapsedKey, IsCollapsed);
        OnChange?.Invoke();
    }

    public void OpenDrawer()   { IsDrawerOpen = true;          OnChange?.Invoke(); }
    public void CloseDrawer()  { IsDrawerOpen = false;         OnChange?.Invoke(); }
    public void ToggleDrawer() { IsDrawerOpen = !IsDrawerOpen; OnChange?.Invoke(); }
}

#pragma warning restore CA1416
