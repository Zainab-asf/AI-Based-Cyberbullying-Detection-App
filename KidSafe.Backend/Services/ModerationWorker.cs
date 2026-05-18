using KidSafe.Backend.Common;
using KidSafe.Backend.Data;
using KidSafe.Backend.Data.Entities;
using KidSafe.Backend.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KidSafe.Backend.Services;

/// <summary>
/// Background worker that drains ModerationQueue.
/// Handles DB persistence, badge awards, SignalR alerts, and FCM push —
/// all off the HTTP request thread so message Send returns immediately after AI.
/// </summary>
public sealed class ModerationWorker : BackgroundService
{
    private readonly ModerationQueue                  _queue;
    private readonly IServiceScopeFactory             _scopes;
    private readonly IHubContext<ChatHub, IChatClient> _hub;
    private readonly ILogger<ModerationWorker>        _logger;

    public ModerationWorker(
        ModerationQueue                   queue,
        IServiceScopeFactory              scopes,
        IHubContext<ChatHub, IChatClient> hub,
        ILogger<ModerationWorker>         logger)
    {
        _queue   = queue;
        _scopes  = scopes;
        _hub     = hub;
        _logger  = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        await foreach (var job in _queue.ReadAllAsync(stopping))
        {
            try   { await ProcessAsync(job, stopping); }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ModerationWorker failed for sender={Sender} label={Label}",
                    job.SenderName, job.Label);
            }
        }
    }

    private async Task ProcessAsync(ModerationSideEffect job, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db    = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notif = scope.ServiceProvider.GetRequiredService<INotificationService>();

        if (job.Label == AppConstants.AILabels.Safe)
        {
            // Award 10 safety points (SRS FR-AP-01)
            var reward = await db.Rewards.FirstOrDefaultAsync(
                r => r.UserId == job.SenderId, ct);

            if (reward != null)
            {
                reward.Points       += 10;
                reward.SafeMessages += 1;
                AwardBadgeIfEligible(reward);
            }

            // Persist direct message so receiver can load history
            if (job.ReceiverId > 0)
            {
                db.DirectMessages.Add(new KidSafe.Backend.Data.Entities.DirectMessage
                {
                    SenderId   = job.SenderId,
                    ReceiverId = job.ReceiverId,
                    Content    = job.OriginalMessage,
                    Label      = "Safe",
                    Score      = job.Score
                });
            }

            await db.SaveChangesAsync(ct);

            // Deliver via SignalR (ReceiverId == 0 means class group — handled separately)
            if (job.ReceiverId > 0)
            {
                await _hub.Clients
                    .Group($"user_{job.ReceiverId}")
                    .ReceiveMessage(job.SenderId, job.SenderName, job.OriginalMessage, "Safe");
            }
        }
        else
        {
            // Persist flagged/blocked message (SRS FR-CB-01)
            db.FlaggedMessages.Add(new FlaggedMessage
            {
                SenderId      = job.SenderId,
                ReceiverId    = job.ReceiverId,
                Message       = job.OriginalMessage,
                MaskedMessage = job.MaskedMessage,
                Label         = job.Label,
                Score         = job.Score
            });
            await db.SaveChangesAsync(ct);

            // FCM push to all parents + teachers
            var tokens = await db.Users
                .Where(u => (u.Role == AppConstants.Roles.Parent || u.Role == AppConstants.Roles.Teacher) && u.FcmToken != null)
                .Select(u => u.FcmToken!)
                .ToListAsync(ct);

            await notif.BroadcastToParentsAsync(
                job.SenderName, job.Label, job.MaskedMessage, tokens);

            // Real-time SignalR alert → "parents" group (Parents + Teachers + Admins)
            await _hub.Clients
                .Group("parents")
                .FlaggedMessageAlert(
                    job.SenderId, job.SenderName,
                    job.MaskedMessage, job.Label, job.Score);

            // Persist direct message for Watch/Review too
            if (job.ReceiverId > 0)
            {
                db.DirectMessages.Add(new KidSafe.Backend.Data.Entities.DirectMessage
                {
                    SenderId   = job.SenderId,
                    ReceiverId = job.ReceiverId,
                    Content    = job.OriginalMessage,
                    Label      = job.Label,
                    Score      = job.Score,
                    Masked     = job.MaskedMessage
                });
                await db.SaveChangesAsync(ct);
            }

            // Deliver masked copy to receiver for Watch (Review is blocked)
            if (job.Label == AppConstants.AILabels.Watch && job.ReceiverId > 0)
            {
                await _hub.Clients
                    .Group($"user_{job.ReceiverId}")
                    .ReceiveMessage(
                        job.SenderId, job.SenderName, job.MaskedMessage, "Watch");
            }
        }
    }

    /// <summary>
    /// Checks if a new badge should be awarded and updates the reward entity in-memory.
    /// The caller is responsible for calling SaveChangesAsync.
    /// </summary>
    private static void AwardBadgeIfEligible(Reward reward)
    {
        var badges = System.Text.Json.JsonSerializer
            .Deserialize<List<string>>(reward.Badges) ?? [];

        string? newBadge = reward.Points switch
        {
            >= 10000 when !badges.Contains("Legend")       => "Legend",
            >= 5000  when !badges.Contains("Safety King")  => "Safety King",
            >= 2000  when !badges.Contains("Chat Scholar") => "Chat Scholar",
            >= 1000  when !badges.Contains("Cyber Hero")   => "Cyber Hero",
            >= 500   when !badges.Contains("Kind Star")    => "Kind Star",
            >= 100   when !badges.Contains("Safe Chatter") => "Safe Chatter",
            _ => null
        };

        if (newBadge is not null)
        {
            badges.Add(newBadge);
            reward.Badges     = System.Text.Json.JsonSerializer.Serialize(badges);
            reward.BadgeLevel = newBadge;
        }
    }
}
