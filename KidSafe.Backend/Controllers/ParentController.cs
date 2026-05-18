using System.Security.Claims;
using KidSafe.Backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidSafe.Backend.Controllers;

[ApiController]
[Route("parent")]
[Authorize(Roles = "Parent,Admin")]
public class ParentController : ControllerBase
{
    private readonly AppDbContext _db;
    public ParentController(AppDbContext db) => _db = db;

    // ── GET /parent/children ─────────────────────────────────────

    [HttpGet("children")]
    public async Task<IActionResult> GetChildren()
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Parent";

        var query = _db.ParentChildren.AsNoTracking().AsQueryable();
        if (role != "Admin")
            query = query.Where(pc => pc.ParentId == uid);

        var children = await query
            .Include(pc => pc.Child)
            .Select(pc => new
            {
                pc.ChildId,
                Name      = pc.Child.DisplayName,
                Email     = pc.Child.Email,
                Status    = pc.Child.Status,
                Avatar    = pc.Child.AvatarEmoji,
                LinkedAt  = pc.LinkedAt
            })
            .ToListAsync();

        return Ok(children);
    }

    // ── GET /parent/children/{childId}/activity ──────────────────

    [HttpGet("children/{childId:int}/activity")]
    public async Task<IActionResult> GetChildActivity(int childId, [FromQuery] int take = 50)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Parent";

        // Verify link unless admin
        if (role != "Admin")
        {
            var linked = await _db.ParentChildren
                .AnyAsync(pc => pc.ParentId == uid && pc.ChildId == childId);
            if (!linked) return Forbid();
        }

        var flagged = await _db.FlaggedMessages
            .AsNoTracking()
            .Where(f => f.SenderId == childId)
            .OrderByDescending(f => f.Timestamp)
            .Take(take)
            .Select(f => new
            {
                f.Id, f.MaskedMessage, f.Label, f.Score, f.Timestamp
            })
            .ToListAsync();

        var reward = await _db.Rewards
            .Where(r => r.UserId == childId)
            .Select(r => new { r.Points, r.BadgeLevel })
            .FirstOrDefaultAsync();

        var classes = await _db.ClassStudents
            .AsNoTracking()
            .Where(cs => cs.StudentId == childId)
            .Include(cs => cs.Class)
            .Select(cs => new
            {
                cs.ClassId,
                ClassName = cs.Class.Name,
                Section   = cs.Class.Section,
                Subject   = cs.Class.Subject,
                cs.JoinedAt
            })
            .ToListAsync();

        return Ok(new { flagged, reward, classes });
    }

    // ── GET /parent/children/{childId}/chat ──────────────────────

    [HttpGet("children/{childId:int}/chat")]
    public async Task<IActionResult> GetChildChatHistory(int childId, [FromQuery] int take = 30)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Parent";

        if (role != "Admin")
        {
            var linked = await _db.ParentChildren
                .AnyAsync(pc => pc.ParentId == uid && pc.ChildId == childId);
            if (!linked) return Forbid();
        }

        var msgs = await _db.ChatMessages
            .AsNoTracking()
            .Where(m => m.SenderId == childId)
            .Include(m => m.Sender)
            .OrderByDescending(m => m.Timestamp)
            .Take(take)
            .OrderBy(m => m.Timestamp)
            .Select(m => new
            {
                m.Id, m.ClassId,
                Content   = m.Label == "Watch" ? m.Masked : m.Content,
                m.Label, m.Score, m.Timestamp
            })
            .ToListAsync();

        return Ok(msgs);
    }
}
