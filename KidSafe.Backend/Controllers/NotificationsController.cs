using System.Security.Claims;
using KidSafe.Backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidSafe.Backend.Controllers;

[ApiController]
[Route("notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    public NotificationsController(AppDbContext db) => _db = db;

    // ── GET /notifications ───────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetNotifications([FromQuery] int take = 30)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();

        var notes = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == uid)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new
            {
                n.Id, n.Title, n.Body, n.Type, n.IsRead, n.CreatedAt
            })
            .ToListAsync();

        return Ok(notes);
    }

    // ── POST /notifications/{id}/read ────────────────────────────

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();
        var n   = await _db.Notifications.FindAsync(id);
        if (n == null || n.UserId != uid) return NotFound();
        n.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── POST /notifications/read-all ────────────────────────────

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();
        await _db.Notifications
            .Where(n => n.UserId == uid && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return Ok();
    }

    // ── GET /notifications/unread-count ─────────────────────────

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();
        var count = await _db.Notifications.CountAsync(n => n.UserId == uid && !n.IsRead);
        return Ok(new { count });
    }
}
