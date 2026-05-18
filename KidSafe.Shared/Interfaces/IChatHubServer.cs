namespace KidSafe.Shared.Interfaces;

/// <summary>
/// Full set of client-to-server SignalR methods.
/// Must match the hub methods in KidSafe.Backend/Hubs/ChatHub.cs exactly.
/// </summary>
public interface IChatHubServer
{
    Task JoinParentRoom();
    Task LeaveParentRoom();
    Task JoinClass(int classId);
    Task LeaveClass(int classId);
    Task SendTypingIndicator(int receiverId);
    Task SendClassTypingIndicator(int classId);
}
