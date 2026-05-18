namespace KidSafe.Backend.Data.Entities;

/// <summary>Persisted direct message between two students (all labels including Safe).</summary>
public class DirectMessage
{
    public int      Id         { get; set; }
    public int      SenderId   { get; set; }
    public int      ReceiverId { get; set; }
    public string   Content    { get; set; } = string.Empty;
    public string   Label      { get; set; } = "Safe";    // Safe | Watch | Review
    public double   Score      { get; set; }
    public string?  Masked     { get; set; }
    public DateTime Timestamp  { get; set; } = DateTime.UtcNow;

    public User Sender   { get; set; } = null!;
    public User Receiver { get; set; } = null!;
}
