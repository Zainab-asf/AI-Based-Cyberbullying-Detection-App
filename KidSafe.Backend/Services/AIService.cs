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
        var payload = new AnalyzeRequestDto(message);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync("/analyze", content);
        }
        catch
        {
            return new AnalyzeResponseDto("safe", 0.0);
        }

        if (!response.IsSuccessStatusCode)
            return new AnalyzeResponseDto("safe", 0.0);

        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<AnalyzeResponseDto>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return result ?? new AnalyzeResponseDto("safe", 0.0);
    }

    public string MaskMessage(string message)
    {
        var words = message.Split(' ');
        var masked = words.Select((w, i) => i % 2 == 0 ? new string('*', w.Length) : w);
        return string.Join(' ', masked);
    }
}
