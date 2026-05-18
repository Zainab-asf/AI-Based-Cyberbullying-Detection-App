using System.Security.Claims;
using KidSafe.Backend.Data;
using KidSafe.Backend.Data.Entities;
using KidSafe.Backend.DTOs;
using KidSafe.Backend.Hubs;
using KidSafe.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KidSafe.Backend.Controllers;

/// <summary>
/// AI label mapping (SDD §4.1 / SRS FR-CB-01):
///   Safe   → deliver + award 10 pts (side-effected async)
///   Watch  → mask + persist + alert  (side-effected async)
///   Review → block + persist + alert (side-effected async)
///
/// Flow: await AI (~10-30 ms) → return label to client → enqueue side effects.
/// DB writes, badge awards, FCM push, and SignalR alerts all run off-thread.
/// </summary>
[ApiController]
[Route("messages")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly AppDbContext          _db;
    private readonly IAIService            _ai;
    private readonly ModerationQueue       _queue;
    private readonly IHubContext<ChatHub, IChatClient> _hub;

    public MessagesController(AppDbContext db, IAIService ai, ModerationQueue queue,
                              IHubContext<ChatHub, IChatClient> hub)
    {
        _db    = db;
        _ai    = ai;
        _queue = queue;
        _hub   = hub;
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send(SendMessageDto dto)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var senderId)) return Unauthorized();
        var sender   = await _db.Users.FindAsync(senderId);
        if (sender == null) return Unauthorized();

        // 1. Call AI — fast with Keras BiLSTM (~10-30 ms)
        var analysis = await _ai.AnalyzeAsync(dto.Message);
        var label    = MapLabel(analysis.Label);
        var masked   = _ai.MaskMessage(dto.Message);

        // 2. Enqueue side effects (DB + SignalR + FCM) — runs off this thread
        await _queue.EnqueueAsync(new ModerationSideEffect(
            senderId, sender.DisplayName, dto.ReceiverId,
            dto.Message, masked, label, analysis.Score));

        // 3. Return AI result immediately to client
        return label switch
        {
            "Safe"   => Ok(new MessageResultDto("sent",    null,   "Safe",   analysis.Score)),
            "Watch"  => Ok(new MessageResultDto("masked",  masked, "Watch",  analysis.Score)),
            _        => Ok(new MessageResultDto("blocked", null,   "Review", analysis.Score))
        };
    }

    [HttpGet("history/{otherUserId:int}")]
    public async Task<IActionResult> GetHistory(int otherUserId)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();

        var messages = await _db.DirectMessages
            .AsNoTracking()
            .Where(d => (d.SenderId == uid && d.ReceiverId == otherUserId)
                     || (d.SenderId == otherUserId && d.ReceiverId == uid))
            .OrderBy(d => d.Timestamp)
            .Take(100)
            .Select(d => new
            {
                d.Id,
                d.SenderId,
                d.ReceiverId,
                Content   = d.Label == "Watch"   ? d.Masked ?? d.Content
                          : d.Label == "Review"  ? "[Message blocked]"
                          : d.Content,
                d.Label,
                d.Score,
                d.Timestamp
            })
            .ToListAsync();

        return Ok(messages);
    }

    [HttpGet("flagged")]
    [Authorize(Roles = "Parent,Teacher,Admin")]
    public async Task<IActionResult> GetFlagged()
    {
        var messages = await _db.FlaggedMessages
            .AsNoTracking()
            .Include(f => f.Sender)
            .OrderByDescending(f => f.Timestamp)
            .Select(f => new
            {
                f.Id,
                SenderName    = f.Sender.DisplayName,
                f.SenderId,
                f.ReceiverId,
                f.MaskedMessage,
                f.Label,
                f.Score,
                f.Timestamp
            })
            .ToListAsync();

        return Ok(messages);
    }

    // ── POST /messages/class/{classId}/send ──────────────────────

    [HttpPost("class/{classId:int}/send")]
    public async Task<IActionResult> SendToClass(int classId, [FromBody] ClassMessageDto dto)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Unauthorized();
        var sender = await _db.Users.FindAsync(uid);
        if (sender == null) return Unauthorized();

        var analysis = await _ai.AnalyzeAsync(dto.Content);
        var label    = MapLabel(analysis.Label);
        var masked   = _ai.MaskMessage(dto.Content);

        // Persist class message
        var msg = new ChatMessage
        {
            ClassId   = classId,
            SenderId  = uid,
            Content   = dto.Content,
            Label     = label,
            Score     = analysis.Score,
            Masked    = label == "Watch" ? masked : null
        };
        _db.ChatMessages.Add(msg);
        await _db.SaveChangesAsync();

        var displayContent = label == "Watch"  ? masked
                           : label == "Review" ? "[Message blocked]"
                           : dto.Content;

        // Broadcast to class room via SignalR
        await _hub.Clients.Group($"class_{classId}")
            .ReceiveClassMessage(classId, uid, sender.DisplayName,
                sender.AvatarEmoji ?? "😊", displayContent, label, analysis.Score, msg.Timestamp);

        // Escalate Watch/Review to parents/admin via moderation queue
        if (label != "Safe")
        {
            await _queue.EnqueueAsync(new ModerationSideEffect(
                uid, sender.DisplayName, 0,
                dto.Content, masked, label, analysis.Score));
        }
        else
        {
            // Award safe message points
            await _queue.EnqueueAsync(new ModerationSideEffect(
                uid, sender.DisplayName, 0,
                dto.Content, masked, "Safe", analysis.Score));
        }

        return Ok(new { msg.Id, label, score = analysis.Score, timestamp = msg.Timestamp });
    }

    private static string MapLabel(string raw) => raw.ToLowerInvariant() switch
    {
        "safe"    => "Safe",
        "watch"   => "Watch",
        "flagged" => "Watch",
        "review"  => "Review",
        "blocked" => "Review",
        _         => "Watch"
    };
}
