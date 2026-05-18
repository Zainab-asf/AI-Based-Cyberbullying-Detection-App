using Microsoft.Extensions.Logging;
using KidSafe.MAUI.Services;

#pragma warning disable CA1416

namespace KidSafe.MAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // ── Singleton services ────────────────────────────────────────
        builder.Services.AddSingleton<AuthStateService>();
        builder.Services.AddSingleton<SidebarService>();
        builder.Services.AddSingleton<ChatHubService>();
        builder.Services.AddSingleton<FcmService>();

        // ── HTTP client with auth delegating handler ──────────────────
        builder.Services.AddTransient<AuthDelegatingHandler>();

        builder.Services.AddHttpClient<ApiService>(c =>
            {
                c.BaseAddress = new Uri("http://localhost:5000/");
                c.Timeout     = TimeSpan.FromSeconds(10);   // fail fast if backend down
            })
            .AddHttpMessageHandler<AuthDelegatingHandler>();

        var app = builder.Build();

        // ── Global exception handlers ─────────────────────────────────
        // Prevent unhandled background-thread and fire-and-forget exceptions
        // from crashing the app or triggering VS "User-Unhandled" dialogs.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            System.Diagnostics.Debug.WriteLine(
                $"[KidSafe] UnhandledException: {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            System.Diagnostics.Debug.WriteLine(
                $"[KidSafe] UnobservedTaskException: {e.Exception}");
        };

        return app;
    }
}
