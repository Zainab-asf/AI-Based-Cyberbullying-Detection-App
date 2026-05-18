using System.Text;
using System.Text.Json;
using KidSafe.Backend.DTOs;

namespace KidSafe.Backend.Services;

public class AIServiceOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8000";
}

public class ToxicityOptions
{
    public double FlaggedThreshold { get; set; } = 0.5;
    public double BlockedThreshold { get; set; } = 0.8;
}

public class AIService : IAIService
{
    private readonly HttpClient _http;
    private readonly ToxicityOptions _toxicity;

    public AIService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _toxicity = new ToxicityOptions
        {
            FlaggedThreshold = config.GetValue<double>("Toxicity:FlaggedThreshold", 0.5),
            BlockedThreshold = config.GetValue<double>("Toxicity:BlockedThreshold", 0.8)
        };
    }

    public async Task<AnalyzeResponseDto> AnalyzeAsync(string message)
    {
        try
        {
            var payload = new AnalyzeRequestDto(message);
            var json    = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response     = await _http.PostAsync("/analyze", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var result       = JsonSerializer.Deserialize<AnalyzeResponseDto>(
                                   responseJson,
                                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? FallbackAnalyze(message);
        }
        catch
        {
            // AI service offline → built-in keyword fallback
            return FallbackAnalyze(message);
        }
    }

    // ── Built-in keyword detector (used when Python AI is offline) ────────────
    private static readonly string[] _blocked = [
        "hate", "kill", "die", "murder", "stupid", "idiot", "dumb", "shut up",
        "ugly", "fat", "loser", "freak", "retard", "slut", "bitch", "bastard",
        "damn you", "go away", "nobody likes you", "i will hurt", "touch you"
    ];

    private static readonly string[] _watch = [
        "mean", "bad", "stop it", "leave me", "dont talk", "go away",
        "you suck", "annoying", "liar", "cheat", "fake"
    ];

    private static AnalyzeResponseDto FallbackAnalyze(string message)
    {
        var lower = message.ToLowerInvariant();
        if (_blocked.Any(w => lower.Contains(w))) return new("review", 0.95);
        if (_watch.Any(w => lower.Contains(w)))   return new("watch",  0.65);
        return new("safe", 0.05);
    }

    public string MaskMessage(string message)
    {
        // Mask every other word so context is still inferrable by moderators
        var words  = message.Split(' ');
        var masked = words.Select((w, i) => i % 2 == 0 ? new string('*', w.Length) : w);
        return string.Join(' ', masked);
    }
}
