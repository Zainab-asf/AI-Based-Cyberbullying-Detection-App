using System.Security.Claims;
using System.Text.Json;
using KidSafe.Backend.Data;
using KidSafe.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidSafe.Backend.Controllers;

[ApiController]
[Route("rewards")]
[Authorize]
public class RewardsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;

    public RewardsController(AppDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    [HttpGet]
    [HttpGet("my")]
    public async Task<IActionResult> GetMyRewards()
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)) return Unauthorized();
        var reward = await _db.Rewards.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId);
        if (reward == null) return NotFound();

        var badges = JsonSerializer.Deserialize<List<string>>(reward.Badges) ?? new();
        return Ok(new
        {
            reward.Points,
            reward.BadgeLevel,
            reward.SafeMessages,
            Badges = badges
        });
    }

    /// <summary>
    /// Called by the client after obtaining an FCM token from Firebase SDK.
    /// Saves the token and auto-subscribes parents/teachers to the alerts topic.
    /// </summary>
    [HttpPost("fcm-token")]
    public async Task<IActionResult> UpdateFcmToken([FromBody] FcmTokenDto dto)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)) return Unauthorized();
        var role   = User.FindFirst(ClaimTypes.Role)?.Value;
        var user   = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        // Unsubscribe old token from topic first (if changed)
        if (!string.IsNullOrEmpty(user.FcmToken) && user.FcmToken != dto.Token
            && role is "Parent" or "Teacher")
        {
            await _notifications.UnsubscribeFromTopicAsync(
                user.FcmToken, NotificationService.ParentsTopic);
        }

        user.FcmToken = dto.Token;
        await _db.SaveChangesAsync();

        // Parents and teachers join the shared alerts topic
        if (role is "Parent" or "Teacher")
        {
            await _notifications.SubscribeToTopicAsync(
                dto.Token, NotificationService.ParentsTopic);
        }

        return Ok(new { subscribed = role is "Parent" or "Teacher" });
    }
}

public record FcmTokenDto(string Token);
