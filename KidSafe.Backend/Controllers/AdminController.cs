using System.Security.Claims;
using KidSafe.Backend.Data;
using KidSafe.Backend.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KidSafe.Backend.Controllers;

/// <summary>
/// Admin operations:
///   GET  /admin/users                    – list all users
///   GET  /admin/users/pending            – pending teacher accounts
///   POST /admin/users                    – create any role user
///   POST /admin/users/{id}/approve       – approve teacher account
///   POST /admin/users/{id}/disable       – disable a user
///   GET  /admin/moderation               – moderation history
///   POST /admin/moderation               – apply moderation action
///   GET  /admin/parent-links             – all parent-child links
///   POST /admin/parent-links             – link parent ↔ child
///   DELETE /admin/parent-links/{pid}/{cid} – unlink
/// </summary>
[ApiController]
[Route("admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminController(AppDbContext db) => _db = db;

    // ── Create User (Admin-only registration, non-Student roles) ──

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        if (await _db.Users.AnyAsync(u => u.Email == dto.Email))
            return Conflict("Email already in use.");

        var user = new User
        {
            Email        = dto.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            DisplayName  = dto.DisplayName,
            Role         = dto.Role,
            Status       = "active",
            AvatarEmoji  = dto.AvatarEmoji
        };
        _db.Users.Add(user);
        _db.Rewards.Add(new Reward { User = user });
        await _db.SaveChangesAsync();
        return Ok(new { user.Id, user.Email, user.DisplayName, user.Role });
    }

    // ── Atomic Student Creation ─────────────────────────────────────
    // POST /admin/students
    // Creates: Student + ClassEnrollment + Parent (new or existing) + ParentChild link
    // All in one DB transaction — partial creation is not possible.

    [HttpPost("students")]
    public async Task<IActionResult> CreateStudent([FromBody] CreateStudentDto dto)
    {
        // ── Validate required fields ──────────────────────────────
        if (string.IsNullOrWhiteSpace(dto.Name))        return BadRequest("Student name is required.");
        if (string.IsNullOrWhiteSpace(dto.Email))       return BadRequest("Student email is required.");
        if (string.IsNullOrWhiteSpace(dto.Password))    return BadRequest("Password is required.");
        if (string.IsNullOrWhiteSpace(dto.RollNumber))  return BadRequest("Roll number is required.");
        if (dto.ClassId <= 0)                           return BadRequest("Class assignment is required.");
        bool hasExistingParent = dto.ExistingParentId.HasValue && dto.ExistingParentId.Value > 0;
        bool hasNewParent      = !string.IsNullOrWhiteSpace(dto.ParentName)
                               && !string.IsNullOrWhiteSpace(dto.ParentEmail)
                               && !string.IsNullOrWhiteSpace(dto.ParentPassword);
        if (!hasExistingParent && !hasNewParent)
            return BadRequest("A parent account must be assigned or created.");

        // ── Check uniqueness ──────────────────────────────────────
        if (await _db.Users.AnyAsync(u => u.Email == dto.Email))
            return Conflict("Student email already in use.");
        if (await _db.Users.AnyAsync(u => u.RollNumber == dto.RollNumber))
            return Conflict($"Roll number '{dto.RollNumber}' is already taken.");

        // ── Check class exists ────────────────────────────────────
        var cls = await _db.Classes.FindAsync(dto.ClassId);
        if (cls == null) return BadRequest("Class not found.");

        // ── Resolve parent ────────────────────────────────────────
        User parent;
        if (hasExistingParent)
        {
            var foundParent = await _db.Users.FindAsync(dto.ExistingParentId!.Value);
            if (foundParent == null) return BadRequest("Parent user not found.");
            if (foundParent.Role != "Parent") return BadRequest("Selected user is not a Parent.");
            parent = foundParent;
        }
        else
        {
            if (await _db.Users.AnyAsync(u => u.Email == dto.ParentEmail))
                return Conflict("Parent email already in use.");
            parent = new User
            {
                Email        = dto.ParentEmail!,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.ParentPassword!),
                DisplayName  = dto.ParentName!,
                Role         = "Parent",
                Status       = "active"
            };
            _db.Users.Add(parent);
            _db.Rewards.Add(new Reward { User = parent });
        }

        // ── Create student ────────────────────────────────────────
        var student = new User
        {
            Email        = dto.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            DisplayName  = dto.Name,
            Role         = "Child",
            Status       = "active",
            RollNumber   = dto.RollNumber,
            AvatarEmoji  = dto.AvatarEmoji ?? "🧒"
        };
        _db.Users.Add(student);
        _db.Rewards.Add(new Reward { User = student });

        await _db.SaveChangesAsync();  // flush to get IDs

        // ── Enroll in class (remove any existing enrollment first) ─
        var existing = await _db.ClassStudents
            .Where(cs => cs.StudentId == student.Id).ToListAsync();
        _db.ClassStudents.RemoveRange(existing);
        _db.ClassStudents.Add(new ClassStudent { ClassId = dto.ClassId, StudentId = student.Id });

        // ── One-to-one parent-child link (remove duplicate if any) ─
        var oldLinks = await _db.ParentChildren
            .Where(pc => pc.ChildId == student.Id).ToListAsync();
        _db.ParentChildren.RemoveRange(oldLinks);
        _db.ParentChildren.Add(new ParentChild { ParentId = parent.Id, ChildId = student.Id });

        await _db.SaveChangesAsync();

        return Ok(new
        {
            student.Id, student.Email, student.DisplayName, student.RollNumber,
            ClassId = cls.Id, ClassName = cls.Name,
            ParentId = parent.Id, ParentName = parent.DisplayName
        });
    }

    // ── UC-11 Manage Users ──────────────────────────────────────────

    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _db.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.Email, u.DisplayName, u.Role, u.Status, u.CreatedAt })
            .OrderBy(u => u.CreatedAt)
            .ToListAsync();
        return Ok(users);
    }

    [HttpGet("users/pending")]
    public async Task<IActionResult> GetPendingTeachers()
    {
        var pending = await _db.Users
            .AsNoTracking()
            .Where(u => u.Status == "pending")
            .Select(u => new { u.Id, u.Email, u.DisplayName, u.Role, u.CreatedAt })
            .ToListAsync();
        return Ok(pending);
    }

    // ── UC-12 Approve Account ───────────────────────────────────────

    [HttpPost("users/{id:int}/approve")]
    public async Task<IActionResult> ApproveUser(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();
        user.Status = "active";
        await _db.SaveChangesAsync();
        return Ok(new { message = $"{user.DisplayName} approved." });
    }

    [HttpPost("users/{id:int}/disable")]
    public async Task<IActionResult> DisableUser(int id)
    {
        var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(adminIdClaim, out var adminId)) return Unauthorized();
        if (id == adminId) return BadRequest("Cannot disable your own account.");
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();
        user.Status = "disabled";
        await _db.SaveChangesAsync();
        return Ok(new { message = $"{user.DisplayName} disabled." });
    }

    // ── UC-09 Apply Moderation Action ───────────────────────────────

    [HttpPost("moderation")]
    public async Task<IActionResult> ApplyAction([FromBody] ApplyModerationDto dto)
    {
        var actorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(actorIdClaim, out var actorId)) return Unauthorized();
        var target  = await _db.Users.FindAsync(dto.TargetUserId);
        if (target == null) return NotFound("Target user not found.");

        var action = new ModerationAction
        {
            Type             = dto.Type,
            ActorId          = actorId,
            TargetUserId     = dto.TargetUserId,
            FlaggedMessageId = dto.FlaggedMessageId,
            Notes            = dto.Notes ?? string.Empty
        };
        _db.ModerationActions.Add(action);

        // Apply status change for block/mute
        if (dto.Type == "block")
            target.Status = "disabled";

        await _db.SaveChangesAsync();
        return Ok(new { action.Id, action.Type, action.CreatedAt });
    }
    

    // ── UC-10 View Moderation History ───────────────────────────────

    [HttpGet("moderation")]
    public async Task<IActionResult> GetModerationHistory()
    {
        var history = await _db.ModerationActions
            .AsNoTracking()
            .Include(ma => ma.Actor)
            .Include(ma => ma.TargetUser)
            .OrderByDescending(ma => ma.CreatedAt)
            .Select(ma => new
            {
                ma.Id,
                ma.Type,
                Actor      = ma.Actor.DisplayName,
                Target     = ma.TargetUser.DisplayName,
                ma.Notes,
                ma.CreatedAt,
                ma.RevokedAt
            })
            .ToListAsync();
        return Ok(history);
    }


    // ── Parent-Child Links ──────────────────────────────────────────

    [HttpGet("parent-links")]
    public async Task<IActionResult> GetParentLinks()
    {
        var links = await _db.ParentChildren
            .AsNoTracking()
            .Include(pc => pc.Parent)
            .Include(pc => pc.Child)
            .Select(pc => new
            {
                pc.ParentId, ParentName = pc.Parent.DisplayName, ParentEmail = pc.Parent.Email,
                pc.ChildId,  ChildName  = pc.Child.DisplayName,  ChildEmail  = pc.Child.Email,
                pc.LinkedAt
            })
            .ToListAsync();
        return Ok(links);
    }

    [HttpPost("parent-links")]
    public async Task<IActionResult> LinkParentChild([FromBody] ParentLinkDto dto)
    {
        if (await _db.ParentChildren.AnyAsync(pc => pc.ParentId == dto.ParentId && pc.ChildId == dto.ChildId))
            return Conflict("Already linked.");

        var parent = await _db.Users.FindAsync(dto.ParentId);
        var child  = await _db.Users.FindAsync(dto.ChildId);
        if (parent == null || child == null) return NotFound("User not found.");
        if (parent.Role != "Parent") return BadRequest("First user must be a Parent.");
        if (child.Role  != "Child")  return BadRequest("Second user must be a Child.");

        _db.ParentChildren.Add(new ParentChild { ParentId = dto.ParentId, ChildId = dto.ChildId });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Linked." });
    }

    [HttpDelete("parent-links/{parentId:int}/{childId:int}")]
    public async Task<IActionResult> UnlinkParentChild(int parentId, int childId)
    {
        var link = await _db.ParentChildren.FindAsync(parentId, childId);
        if (link == null) return NotFound();
        _db.ParentChildren.Remove(link);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record ApplyModerationDto(string Type, int TargetUserId, int? FlaggedMessageId, string? Notes);
public record CreateUserDto(string Email, string Password, string DisplayName, string Role, string? AvatarEmoji, int? ClassId = null);
public record ParentLinkDto(int ParentId, int ChildId);

public record CreateStudentDto(
    string Name,
    string Email,
    string Password,
    string RollNumber,
    int ClassId,
    string? AvatarEmoji,
    // Parent: select existing
    int? ExistingParentId,
    // OR create new parent inline
    string? ParentName,
    string? ParentEmail,
    string? ParentPassword);
