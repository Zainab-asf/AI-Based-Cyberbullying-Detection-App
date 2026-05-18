using KidSafe.Backend.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KidSafe.Backend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User>             Users             => Set<User>();
    public DbSet<FlaggedMessage>   FlaggedMessages   => Set<FlaggedMessage>();
    public DbSet<Reward>           Rewards           => Set<Reward>();
    public DbSet<AbuseReport>      AbuseReports      => Set<AbuseReport>();
    public DbSet<Complaint>        Complaints        => Set<Complaint>();
    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();
    public DbSet<Class>            Classes           => Set<Class>();
    public DbSet<ClassStudent>     ClassStudents     => Set<ClassStudent>();
    public DbSet<ParentChild>      ParentChildren    => Set<ParentChild>();
    public DbSet<ContentItem>      ContentItems      => Set<ContentItem>();
    public DbSet<Submission>       Submissions       => Set<Submission>();
    public DbSet<ChatMessage>      ChatMessages      => Set<ChatMessage>();
    public DbSet<DirectMessage>    DirectMessages    => Set<DirectMessage>();
    public DbSet<Notification>     Notifications     => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        // ── User ──────────────────────────────────────────────────
        m.Entity<User>().HasIndex(u => u.Email).IsUnique();
        m.Entity<User>().HasIndex(u => u.RollNumber).IsUnique()
            .HasFilter("[RollNumber] IS NOT NULL");

        // ── FlaggedMessage ────────────────────────────────────────
        m.Entity<FlaggedMessage>()
            .HasOne(f => f.Sender).WithMany(u => u.SentFlaggedMessages)
            .HasForeignKey(f => f.SenderId).OnDelete(DeleteBehavior.Restrict);

        // ── Reward (1-1) ──────────────────────────────────────────
        m.Entity<Reward>()
            .HasOne(r => r.User).WithOne(u => u.Reward)
            .HasForeignKey<Reward>(r => r.UserId);

        // ── AbuseReport ───────────────────────────────────────────
        m.Entity<AbuseReport>()
            .HasOne(a => a.Reporter).WithMany(u => u.AbuseReports)
            .HasForeignKey(a => a.ReporterId).OnDelete(DeleteBehavior.Restrict);

        // ── Complaint ─────────────────────────────────────────────
        m.Entity<Complaint>()
            .HasOne(c => c.User).WithMany(u => u.Complaints)
            .HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);

        // ── ModerationAction ──────────────────────────────────────
        m.Entity<ModerationAction>()
            .HasOne(ma => ma.Actor).WithMany()
            .HasForeignKey(ma => ma.ActorId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ModerationAction>()
            .HasOne(ma => ma.TargetUser).WithMany()
            .HasForeignKey(ma => ma.TargetUserId).OnDelete(DeleteBehavior.Restrict);

        // ── Class ─────────────────────────────────────────────────
        m.Entity<Class>()
            .HasOne(c => c.Teacher).WithMany()
            .HasForeignKey(c => c.TeacherId).OnDelete(DeleteBehavior.SetNull);

        // ── ClassStudent (composite PK) ───────────────────────────
        m.Entity<ClassStudent>()
            .HasKey(cs => new { cs.ClassId, cs.StudentId });
        m.Entity<ClassStudent>()
            .HasOne(cs => cs.Class).WithMany(c => c.Students)
            .HasForeignKey(cs => cs.ClassId);
        m.Entity<ClassStudent>()
            .HasOne(cs => cs.Student).WithMany(u => u.ClassEnrollments)
            .HasForeignKey(cs => cs.StudentId).OnDelete(DeleteBehavior.Restrict);

        // ── ParentChild (composite PK) ────────────────────────────
        m.Entity<ParentChild>().HasKey(pc => new { pc.ParentId, pc.ChildId });
        m.Entity<ParentChild>()
            .HasOne(pc => pc.Parent).WithMany()
            .HasForeignKey(pc => pc.ParentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ParentChild>()
            .HasOne(pc => pc.Child).WithMany()
            .HasForeignKey(pc => pc.ChildId).OnDelete(DeleteBehavior.Restrict);

        // ── ContentItem ───────────────────────────────────────────
        m.Entity<ContentItem>()
            .HasOne(ci => ci.Class).WithMany(c => c.Content)
            .HasForeignKey(ci => ci.ClassId);
        m.Entity<ContentItem>()
            .HasOne(ci => ci.Teacher).WithMany()
            .HasForeignKey(ci => ci.TeacherId).OnDelete(DeleteBehavior.Restrict);

        // ── Submission ────────────────────────────────────────────
        m.Entity<Submission>()
            .HasOne(s => s.Assignment).WithMany(ci => ci.Submissions)
            .HasForeignKey(s => s.ContentItemId);
        m.Entity<Submission>()
            .HasOne(s => s.Student).WithMany(u => u.Submissions)
            .HasForeignKey(s => s.StudentId).OnDelete(DeleteBehavior.Restrict);

        // ── ChatMessage ───────────────────────────────────────────
        m.Entity<ChatMessage>()
            .HasOne(cm => cm.Class).WithMany(c => c.Messages)
            .HasForeignKey(cm => cm.ClassId);
        m.Entity<ChatMessage>()
            .HasOne(cm => cm.Sender).WithMany()
            .HasForeignKey(cm => cm.SenderId).OnDelete(DeleteBehavior.Restrict);

        // ── DirectMessage ─────────────────────────────────────────
        m.Entity<DirectMessage>()
            .HasOne(d => d.Sender).WithMany()
            .HasForeignKey(d => d.SenderId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<DirectMessage>()
            .HasOne(d => d.Receiver).WithMany()
            .HasForeignKey(d => d.ReceiverId).OnDelete(DeleteBehavior.Restrict);

        // ── Notification ──────────────────────────────────────────
        m.Entity<Notification>()
            .HasOne(n => n.User).WithMany(u => u.Notifications)
            .HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
