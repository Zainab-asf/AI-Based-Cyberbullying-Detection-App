using Microsoft.AspNetCore.Mvc;

namespace KidSafe.Backend.Controllers;

/// <summary>
/// Lightweight health endpoints — no auth required so the MAUI app
/// can check connectivity before attempting API calls.
/// </summary>
[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration     _config;

    public HealthController(IHttpClientFactory factory, IConfiguration config)
    {
        _factory = factory;
        _config  = config;
    }

    /// <summary>Backend is alive.</summary>
    [HttpGet]
    public IActionResult Ping() => Ok(new { status = "ok", timestamp = DateTime.UtcNow });

    /// <summary>
    /// Checks whether the AI microservice (FastAPI/Keras on :8000) is reachable.
    /// Returns 200 { ai: "ok" } or 200 { ai: "unavailable", message: "..." }.
    /// Never throws — safe to call from UI status indicators.
    /// </summary>
    [HttpGet("ai")]
    public async Task<IActionResult> AiStatus()
    {
        try
        {
            var aiUrl  = _config["AI:BaseUrl"] ?? "http://localhost:8000";
            var client = _factory.CreateClient("ai-health");
            using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var resp = await client.GetAsync($"{aiUrl}/health", cts.Token);
            if (resp.IsSuccessStatusCode)
                return Ok(new { ai = "ok" });

            return Ok(new { ai = "unavailable", message = $"HTTP {(int)resp.StatusCode}" });
        }
        catch (Exception ex)
        {
            return Ok(new { ai = "unavailable", message = ex.Message });
        }
    }
}
