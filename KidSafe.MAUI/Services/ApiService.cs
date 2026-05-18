using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KidSafe.Shared.DTOs;

namespace KidSafe.MAUI.Services;

public class ApiService
{
    private readonly HttpClient _http;
    public ApiService(HttpClient http) => _http = http;

    public string BaseUrl => _http.BaseAddress?.ToString().TrimEnd('/') ?? "http://localhost:5000";

    // ── Auth ──────────────────────────────────────────────────────────

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("auth/register", req);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<AuthResponse>() : null;
        }
        catch { return null; }
    }

    public async Task<(AuthResponse? Data, string Error)> LoginAsync(LoginRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("auth/login", req);
            if (resp.IsSuccessStatusCode)
                return (await resp.Content.ReadFromJsonAsync<AuthResponse>(), "");

            // extract server error message
            string msg = "Invalid email or password.";
            try
            {
                var raw = await resp.Content.ReadAsStringAsync();
                var el  = JsonSerializer.Deserialize<JsonElement>(raw);
                if (el.TryGetProperty("error",   out var e)) msg = e.GetString() ?? msg;
                else if (el.TryGetProperty("message", out var m)) msg = m.GetString() ?? msg;
            }
            catch { }
            return (null, msg);
        }
        catch (HttpRequestException)
        {
            return (null, "Cannot connect to server. Make sure the backend is running.");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    // ── Messages ──────────────────────────────────────────────────────

    public async Task<MessageResult?> SendMessageAsync(SendMessageRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("messages/send", req);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<MessageResult>() : null;
        }
        catch { return null; }
    }

    public async Task<List<FlaggedMessageItem>> GetFlaggedMessagesAsync()
    {
        try
        {
            var resp = await _http.GetAsync("messages/flagged");
            if (!resp.IsSuccessStatusCode) return [];
            return await resp.Content.ReadFromJsonAsync<List<FlaggedMessageItem>>() ?? [];
        }
        catch { return []; }
    }

    // ── Dashboard ─────────────────────────────────────────────────────

    public async Task<DashboardStats?> GetDashboardStatsAsync()
    {
        try
        {
            var resp = await _http.GetAsync("dashboard/stats");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<DashboardStats>();
        }
        catch { return null; }
    }

    // ── FCM ───────────────────────────────────────────────────────────

    public async Task RegisterFcmTokenAsync(string token)
    {
        try { await _http.PostAsJsonAsync("rewards/fcm-token", new { Token = token }); }
        catch { /* fire-and-forget, ignore failures */ }
    }

    // ── Generic helpers ───────────────────────────────────────────────

    public async Task<T?> GetAsync<T>(string url)
    {
        try
        {
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return default;
            return await resp.Content.ReadFromJsonAsync<T>();
        }
        catch { return default; }
    }

    public async Task<bool> PostAsync(string url, object body)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(url, body);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<T?> PostRawAsync<T>(string url, object body)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(url, body);
            if (!resp.IsSuccessStatusCode) return default;
            return await resp.Content.ReadFromJsonAsync<T>();
        }
        catch { return default; }
    }

    public async Task<bool> DeleteAsync(string url)
    {
        try
        {
            var resp = await _http.DeleteAsync(url);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> PatchAsync(string url, object body)
    {
        try
        {
            var json    = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp    = await _http.PatchAsync(url, content);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<T?> PatchRawAsync<T>(string url, object body)
    {
        try
        {
            var json    = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp    = await _http.PatchAsync(url, content);
            if (!resp.IsSuccessStatusCode) return default;
            return await resp.Content.ReadFromJsonAsync<T>();
        }
        catch { return default; }
    }

    public async Task<(bool Ok, string Error)> PostWithErrorAsync(string url, object body)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(url, body);
            if (resp.IsSuccessStatusCode) return (true, "");
            var raw = await resp.Content.ReadAsStringAsync();
            try
            {
                var el = JsonSerializer.Deserialize<JsonElement>(raw);
                if (el.TryGetProperty("message", out var m)) return (false, m.GetString() ?? raw);
                if (el.TryGetProperty("title",   out var t)) return (false, t.GetString() ?? raw);
            }
            catch { }
            return (false, string.IsNullOrWhiteSpace(raw) ? "Request failed." : raw.Trim('"'));
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ── Health ────────────────────────────────────────────────────────

    /// <summary>Ping the backend. Returns true if the server responds.</summary>
    public async Task<bool> IsServerReachableAsync()
    {
        try
        {
            // Use a short timeout for the ping
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var resp = await _http.GetAsync("health", cts.Token);
            // Any HTTP response (even 404) means the server is up
            return (int)resp.StatusCode < 600;
        }
        catch { return false; }
    }

    /// <summary>Returns "ok" or "unavailable" for the AI microservice.</summary>
    public async Task<string> GetAiStatusAsync()
    {
        try
        {
            var resp = await _http.GetAsync("health/ai");
            if (!resp.IsSuccessStatusCode) return "unavailable";
            var el = JsonSerializer.Deserialize<JsonElement>(
                await resp.Content.ReadAsStringAsync());
            return el.TryGetProperty("ai", out var v) ? v.GetString() ?? "unavailable" : "unavailable";
        }
        catch { return "unavailable"; }
    }

    // ── Reports / Complaints ─────────────────────────────────────────

    public async Task<bool> ReportAbuseAsync(string reason, int? flaggedMessageId = null)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("reports/abuse",
                new { Reason = reason, FlaggedMessageId = flaggedMessageId });
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> FileComplaintAsync(string description, int? linkedReportId = null)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("reports/complaint",
                new { Description = description, LinkedReportId = linkedReportId });
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
