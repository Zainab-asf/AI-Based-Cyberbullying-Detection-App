namespace KidSafe.Shared.Interfaces;

/// <summary>
/// Full set of server-to-client SignalR methods.
/// Must match IChatClient in KidSafe.Backend/Hubs/ChatHub.cs exactly.
/// </summary>
public interface IChatHubClient
{
    Task ReceiveMessage(int senderId, string senderName, string message, string label);
    Task ReceiveClassMessage(int classId, int senderId, string senderName, string senderEmoji,
                             string content, string label, double score, DateTime timestamp);
    Task UserTyping(string userId, string userName);
    Task ClassUserTyping(int classId, string userName);
    Task FlaggedMessageAlert(int senderId, string senderName, string maskedMessage, string label, double score);
    Task ConnectionAck(string connectionId, string userId);
    Task UserStatusChanged(string userId, string displayName, bool online);
    Task NotificationReceived(string title, string body, string type);
}
