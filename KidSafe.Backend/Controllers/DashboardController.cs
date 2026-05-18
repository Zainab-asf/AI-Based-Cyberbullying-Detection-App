using KidSafe.Backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidSafe.Backend.Controllers;

[ApiController]
[Route("dashboard")]
[Authorize(Roles = "Parent,Teacher,Admin")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;

    public DashboardController(AppDbContext db) => _db = db;

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        // Use updated labels: Watch (was flagged) | Review (was blocked)
        var totalWatch    = await _db.FlaggedMessages.CountAsync(f => f.Label == "Watch");
        var totalReview   = await _db.FlaggedMessages.CountAsync(f => f.Label == "Review");
        var totalFlagged  = totalWatch + totalReview;   // combined for backwards compat
        var totalChildren = await _db.Users.CountAsync(u => u.Role == "Child");
        var pendingTeachers = await _db.Users.CountAsync(u => u.Status == "pending");
        var totalComplaints = await _db.Complaints.CountAsync();
        var totalReports    = await _db.AbuseReports.CountAsync();

        var recentActivity = await _db.FlaggedMessages
            .AsNoTracking()
            .Include(f => f.Sender)
            .OrderByDescending(f => f.Timestamp)
            .Take(10)
            .Select(f => new
            {
                SenderName = f.Sender.DisplayName,
                f.Label,
                f.Score,
                f.Timestamp
            })
            .ToListAsync();

        return Ok(new
        {
            totalWatch,
            totalReview,
            totalFlagged,
            totalChildren,
            pendingTeachers,
            totalComplaints,
            totalReports,
            recentActivity
        });
    }
}
