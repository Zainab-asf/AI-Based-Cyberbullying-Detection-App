using System.Security.Claims;
using KidSafe.Backend.Data;
using KidSafe.Backend.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidSafe.Backend.Controllers;

[ApiController]
[Route("content")]
[Authorize]
public class ContentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public ContentController(AppDbContext db, IWebHostEnvironment env)
    {
        _db  = db;
        _env = env;
    }

    // ── GET /content/class/{classId} ─────────────────────────────

    [HttpGet("class/{classId:int}")]
    public async Task<IActionResult> GetClassContent(int classId)
    {
        var items = await _db.ContentItems
            .AsNoTracking()
            .Where(ci => ci.ClassId == classId)
            .Include(ci => ci.Teacher)
            .OrderByDescending(ci => ci.CreatedAt)
            .Select(ci => new
            {
                ci.Id, ci.Title, ci.Description, ci.Type,
                ci.FilePath, ci.FileType, ci.FileSize,
                ci.DueDate, ci.MaxPoints,
                Teacher   = ci.Teacher.DisplayName,
                ci.TeacherId,
                ci.CreatedAt,
                SubmissionCount = ci.Submissions.Count
            })
            .ToListAsync();

        return Ok(items);
    }

    // ── GET /content/{id} ────────────────────────────────────────

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetContent(int id)
    {
        var ci = await _db.ContentItems
            .AsNoTracking()
            .Include(c => c.Teacher)
            .Include(c => c.Submissions).ThenInclude(s => s.Student)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (ci == null) return NotFound();

        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Child";

        object? mySubmission = null;
        if (role == "Child")
        {
            var sub = ci.Submissions.FirstOrDefault(s => s.StudentId == uid);
            if (sub != null)
                mySubmission = new { sub.Id, sub.Points, sub.Feedback, sub.SubmittedAt };
        }

        return Ok(new
        {
            ci.Id, ci.Title, ci.Description, ci.Type,
            ci.FilePath, ci.FileType, ci.FileSize,
            ci.DueDate, ci.MaxPoints,
            Teacher  = ci.Teacher.DisplayName,
            ci.TeacherId, ci.ClassId, ci.CreatedAt,
            Submissions = role is "Teacher" or "Admin"
                ? ci.Submissions.Select(s => new
                  {
                      s.Id, s.StudentId,
                      Student   = s.Student.DisplayName,
                      s.Points, s.Feedback, s.SubmittedAt
                  })
                : null,
            MySubmission = mySubmission
        });
    }

    // ── POST /content  (Teacher / Admin) ─────────────────────────
    // multipart: fields + optional file

    [HttpPost]
    [Authorize(Roles = "Teacher,Admin")]
    [RequestSizeLimit(50_000_000)] // 50 MB
    public async Task<IActionResult> CreateContent([FromForm] CreateContentDto dto)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();

        string? filePath = null;
        string? fileType = null;
        long    fileSize = 0;

        if (dto.File != null)
        {
            var uploadsDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads");
            Directory.CreateDirectory(uploadsDir);

            var ext  = Path.GetExtension(dto.File.FileName).ToLowerInvariant();
            var name = $"{Guid.NewGuid()}{ext}";
            var full = Path.Combine(uploadsDir, name);

            await using var stream = System.IO.File.Create(full);
            await dto.File.CopyToAsync(stream);

            filePath = $"/uploads/{name}";
            fileType = ext.TrimStart('.');
            fileSize = dto.File.Length;
        }

        var item = new ContentItem
        {
            ClassId     = dto.ClassId,
            TeacherId   = uid,
            Title       = dto.Title,
            Description = dto.Description ?? string.Empty,
            Type        = dto.Type,
            FilePath    = filePath ?? dto.LinkUrl,
            FileType    = fileType,
            FileSize    = fileSize,
            DueDate     = dto.DueDate,
            MaxPoints   = dto.MaxPoints ?? 0
        };

        _db.ContentItems.Add(item);
        await _db.SaveChangesAsync();

        return Ok(new { item.Id, item.Title, item.Type, item.FilePath });
    }

    // ── DELETE /content/{id}  (Teacher owner / Admin) ────────────

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> DeleteContent(int id)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Teacher";

        var item = await _db.ContentItems.FindAsync(id);
        if (item == null) return NotFound();
        if (role != "Admin" && item.TeacherId != uid)
            return Forbid();

        // Delete physical file if present
        if (item.FilePath != null && item.FilePath.StartsWith("/uploads/"))
        {
            var full = Path.Combine(_env.WebRootPath ?? "wwwroot", item.FilePath.TrimStart('/'));
            if (System.IO.File.Exists(full))
                System.IO.File.Delete(full);
        }

        _db.ContentItems.Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── POST /content/{id}/submit  (Student) ─────────────────────

    [HttpPost("{id:int}/submit")]
    [Authorize(Roles = "Child")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Submit(int id, [FromForm] SubmitAssignmentDto dto)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();

        var item = await _db.ContentItems.FindAsync(id);
        if (item == null || item.Type != "Assignment") return NotFound();

        if (await _db.Submissions.AnyAsync(s => s.ContentItemId == id && s.StudentId == uid))
            return Conflict("Already submitted.");

        string? filePath = null;
        if (dto.File != null)
        {
            var uploadsDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "submissions");
            Directory.CreateDirectory(uploadsDir);
            var ext  = Path.GetExtension(dto.File.FileName).ToLowerInvariant();
            var name = $"{Guid.NewGuid()}{ext}";
            var full = Path.Combine(uploadsDir, name);
            await using var stream = System.IO.File.Create(full);
            await dto.File.CopyToAsync(stream);
            filePath = $"/uploads/submissions/{name}";
        }

        var sub = new Submission
        {
            ContentItemId = id,
            StudentId     = uid,
            FilePath      = filePath,
            TextAnswer    = dto.TextAnswer
        };

        _db.Submissions.Add(sub);
        await _db.SaveChangesAsync();

        return Ok(new { sub.Id, sub.SubmittedAt });
    }

    // ── POST /content/submissions/{id}/grade  (Teacher / Admin) ──

    [HttpPost("submissions/{id:int}/grade")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> Grade(int id, [FromBody] GradeDto dto)
    {
        var sub = await _db.Submissions.FindAsync(id);
        if (sub == null) return NotFound();

        sub.Points   = dto.Points;
        sub.Feedback = dto.Feedback ?? string.Empty;
        await _db.SaveChangesAsync();

        return Ok(new { sub.Id, sub.Points, sub.Feedback });
    }
}

public record CreateContentDto(
    int ClassId, string Title, string? Description, string Type,
    IFormFile? File, string? LinkUrl,
    DateTime? DueDate, int? MaxPoints);

public record SubmitAssignmentDto(IFormFile? File, string? TextAnswer);
public record GradeDto(int Points, string? Feedback);
