using KidSafe.Shared.DTOs;
using Microsoft.AspNetCore.SignalR.Client;

namespace KidSafe.MAUI.Services;

public enum HubState { Disconnected, Connecting, Connected, Reconnecting }

public class ChatHubService : IAsyncDisposable
{
    private HubConnection? _conn;

    public event Action<ChatMessage>? OnMessageReceived;
    public event Action<ClassChatMessage>? OnClassMessageReceived;
    public event Action<int, string, string, string, double>? OnFlaggedAlert;
    public event Action<string>? OnUserTyping;
    public event Action<int, string>? OnClassUserTyping;
    public event Action<string, bool>? OnUserStatusChanged;
    public event Action<HubState>? OnStateChanged;

    public HubState State { get; private set; } = HubState.Disconnected;

    public async Task StartAsync(string token, string baseUrl = "http://localhost:5000")
    {
        if (string.IsNullOrEmpty(token) || token is "pending" or "disabled"
            || AuthStateService.IsTokenExpired(token))
        {
            SetState(HubState.Disconnected);
            return;
        }

        if (_conn != null) await DisposeAsync();

        _conn = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/hubs/chat?access_token={token}")
            .WithAutomaticReconnect(new ExponentialBackoffRetryPolicy())
            .Build();

        _conn.On<int, string, string, string>("ReceiveMessage",
            (id, name, msg, label) => OnMessageReceived?.Invoke(new ChatMessage
            {
                SenderId = id, SenderName = name, Content = msg, Label = label
            }));

        _conn.On<int, int, string, string, string, string, double, DateTime>(
            "ReceiveClassMessage",
            (classId, senderId, senderName, emoji, content, label, score, ts) =>
                OnClassMessageReceived?.Invoke(new ClassChatMessage
                {
                    ClassId = classId, SenderId = senderId, SenderName = senderName,
                    SenderEmoji = emoji, Content = content, Label = label, Score = score,
                    Timestamp = ts
                }));

        _conn.On<int, string, string, string, double>("FlaggedMessageAlert",
            (id, name, masked, label, score) =>
                OnFlaggedAlert?.Invoke(id, name, masked, label, score));

        _conn.On<string, string>("UserTyping",
            (_, name) => OnUserTyping?.Invoke(name));

        _conn.On<int, string>("ClassUserTyping",
            (classId, name) => OnClassUserTyping?.Invoke(classId, name));

        _conn.On<string, string, bool>("UserStatusChanged",
            (uid, _, online) => OnUserStatusChanged?.Invoke(uid, online));

        _conn.Reconnecting += _ => { SetState(HubState.Reconnecting); return Task.CompletedTask; };
        _conn.Reconnected  += _ => { SetState(HubState.Connected);    return Task.CompletedTask; };
        _conn.Closed       += _ => { SetState(HubState.Disconnected); return Task.CompletedTask; };

        SetState(HubState.Connecting);
        try
        {
            await _conn.StartAsync();
            SetState(HubState.Connected);
        }
        catch
        {
            SetState(HubState.Disconnected);
        }
    }

    public Task JoinParentRoomAsync()              => Invoke("JoinParentRoom");
    public Task LeaveParentRoomAsync()             => Invoke("LeaveParentRoom");
    public Task JoinClassAsync(int classId)        => Invoke("JoinClass", classId);
    public Task LeaveClassAsync(int classId)       => Invoke("LeaveClass", classId);
    public Task SendTypingAsync(int recvId)        => Invoke("SendTypingIndicator", recvId);
    public Task SendClassTypingAsync(int classId)  => Invoke("SendClassTypingIndicator", classId);

    public async ValueTask DisposeAsync()
    {
        if (_conn != null) { await _conn.DisposeAsync(); _conn = null; }
        SetState(HubState.Disconnected);
    }

    private async Task Invoke(string method, params object[] args)
    {
        if (_conn?.State == HubConnectionState.Connected)
            await _conn.InvokeCoreAsync(method, args);
    }

    private void SetState(HubState s) { State = s; OnStateChanged?.Invoke(s); }
}

file sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] _delays =
        { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
          TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) };

    public TimeSpan? NextRetryDelay(RetryContext ctx)
    {
        var i = (int)ctx.PreviousRetryCount;
        return _delays[Math.Min(i, _delays.Length - 1)];
    }
}
