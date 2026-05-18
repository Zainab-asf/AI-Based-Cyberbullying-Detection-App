using System.Security.Claims;
using KidSafe.Backend.Data;
using KidSafe.Backend.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidSafe.Backend.Controllers;

/// <summary>
/// Reports &amp; Complaints (SDD UC-19, UC-20):
///   POST /reports/abuse           – child/parent/teacher submits abuse report
///   GET  /reports/abuse           – admin/teacher views all reports
///   POST /reports/complaint       – file formal complaint
///   GET  /reports/complaint       – view complaints (role-scoped)
///   PATCH /reports/complaint/{id} – admin updates complaint status
/// </summary>
[ApiController]
[Route("reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db) => _db = db;

    // ── UC-19 Report Abuse — creates report + in-app notification for all admins ──

    [HttpPost("abuse")]
    public async Task<IActionResult> ReportAbuse([FromBody] AbuseReportDto dto)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)) return Unauthorized();
        var reporter = await _db.Users.FindAsync(userId);

        var report = new AbuseReport
        {
            ReporterId           = userId,
            ReferencedMessageId  = dto.FlaggedMessageId,
            Reason               = dto.Reason
        };
        _db.AbuseReports.Add(report);

        // Notify all Admins via in-app notification
        var adminIds = await _db.Users
            .Where(u => u.Role == "Admin" && u.Status == "active")
            .Select(u => u.Id)
            .ToListAsync();

        foreach (var adminId in adminIds)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = adminId,
                Title  = "Abuse Report Filed",
                Body   = $"{reporter?.DisplayName ?? "A student"} reported: {dto.Reason}",
                Type   = "alert"
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { report.Id, report.Timestamp });
    }

    [HttpGet("abuse")]
    [Authorize(Roles = "Admin,Teacher,Parent")]
    public async Task<IActionResult> GetAbuseReports()
    {
        var reports = await _db.AbuseReports
            .AsNoTracking()
            .Include(r => r.Reporter)
            .OrderByDescending(r => r.Timestamp)
            .Select(r => new
            {
                r.Id,
                Reporter            = r.Reporter.DisplayName,
                r.Reason,
                r.ReferencedMessageId,
                r.Timestamp
            })
            .ToListAsync();
        return Ok(reports);
    }

    // ── UC-20 File Complaint ────────────────────────────────────────

    [HttpPost("complaint")]
    public async Task<IActionResult> FileComplaint([FromBody] ComplaintDto dto)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)) return Unauthorized();
        var complaint = new Complaint
        {
            UserId        = userId,
            Description   = dto.Description,
            LinkedReportId = dto.LinkedReportId
        };
        _db.Complaints.Add(complaint);
        await _db.SaveChangesAsync();
        return Ok(new { complaint.Id, complaint.Status, complaint.DateFiled });
    }

    [HttpGet("complaint")]
    public async Task<IActionResult> GetComplaints()
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)) return Unauthorized();
        var role   = User.FindFirst(ClaimTypes.Role)?.Value;

        // Admins see all; others see only their own (SDD UC-16 least privilege)
        var query = _db.Complaints.AsNoTracking().Include(c => c.User).AsQueryable();
        if (role != "Admin")
            query = query.Where(c => c.UserId == userId);

        var complaints = await query
            .OrderByDescending(c => c.DateFiled)
            .Select(c => new
            {
                c.Id,
                FiledBy     = c.User.DisplayName,
                c.Description,
                c.Status,
                c.DateFiled,
                c.LinkedReportId
            })
            .ToListAsync();
        return Ok(complaints);
    }

    [HttpPatch("complaint/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateComplaintStatus(int id, [FromBody] UpdateComplaintDto dto)
    {
        var complaint = await _db.Complaints.FindAsync(id);
        if (complaint == null) return NotFound();
        complaint.Status = dto.Status;
        await _db.SaveChangesAsync();
        return Ok(new { complaint.Id, complaint.Status });
    }
}

public record AbuseReportDto(string Reason, int? FlaggedMessageId);
public record ComplaintDto(string Description, int? LinkedReportId);
public record UpdateComplaintDto(string Status);
