using KidSafe.Backend.Data;
using KidSafe.Backend.DTOs;
using KidSafe.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace KidSafe.Backend.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    public AuthController(IAuthService auth) => _auth = auth;

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        var result = await _auth.RegisterAsync(dto);
        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var result = await _auth.LoginAsync(dto);
        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpPatch("avatar")]
    [Authorize]
    public async Task<IActionResult> UpdateAvatar(
        [FromBody] AvatarDto dto,
        [FromServices] AppDbContext db)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();
        var user = await db.Users.FindAsync(uid);
        if (user == null) return NotFound();
        user.AvatarEmoji = dto.Emoji;
        await db.SaveChangesAsync();
        return Ok();
    }

    // ── GET /auth/me ──────────────────────────────────────────────
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe([FromServices] AppDbContext db)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();
        var user = await db.Users.FindAsync(uid);
        if (user == null) return NotFound();
        return Ok(new {
            user.Id, user.Email, user.DisplayName, user.Role,
            user.AvatarEmoji, user.RollNumber, user.Phone, user.Status
        });
    }

    // ── PATCH /auth/profile — Parent & Teacher only ───────────────
    [HttpPatch("profile")]
    [Authorize(Roles = "Parent,Teacher,Admin")]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileDto dto,
        [FromServices] AppDbContext db)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();
        var user = await db.Users.FindAsync(uid);
        if (user == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.DisplayName))
            user.DisplayName = dto.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Phone))
            user.Phone = dto.Phone.Trim();

        await db.SaveChangesAsync();
        return Ok(new { user.DisplayName, user.Phone });
    }

    // ── PATCH /auth/password — all roles ─────────────────────────
    [HttpPatch("password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordDto dto,
        [FromServices] AppDbContext db)
    {
        if (string.IsNullOrWhiteSpace(dto.CurrentPassword) ||
            string.IsNullOrWhiteSpace(dto.NewPassword))
            return BadRequest("Both current and new password are required.");
        if (dto.NewPassword.Length < 6)
            return BadRequest("New password must be at least 6 characters.");

        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();
        var user = await db.Users.FindAsync(uid);
        if (user == null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            return BadRequest("Current password is incorrect.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        await db.SaveChangesAsync();
        return Ok(new { message = "Password updated." });
    }
}

public record AvatarDto(string Emoji);
public record UpdateProfileDto(string? DisplayName, string? Phone);
public record ChangePasswordDto(string CurrentPassword, string NewPassword);
