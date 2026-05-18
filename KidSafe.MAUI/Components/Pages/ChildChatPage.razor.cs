using KidSafe.MAUI.Services;
using KidSafe.Shared.DTOs;
using Microsoft.AspNetCore.Components.Web;

namespace KidSafe.MAUI.Components.Pages;

public partial class ChildChatPage
{
    record ClassmateDto(int Id, string DisplayName, string Avatar);
    record ClassmatesResponse(string? ClassName, string? Section, List<ClassmateDto>? Classmates);
    record DmHistoryItem(int SenderId, int ReceiverId, string Content, string Label, double Score, DateTime Timestamp);

    List<ChatMessage>  _messages          = new();
    List<ClassmateDto> _classmates        = new();
    ClassmateDto?      _selectedClassmate;
    string             _className         = "";
    string             _input = "", _toast = "", _toastClass = "", _typingUser = "";
    bool               _sending, _loadingClassmates = true, _showEmoji;
    HubState           _hubState          = HubState.Disconnected;

    static readonly string[] _quickReplies = { "That's great!", "Sure, let's do it!", "I agree with you!", "Thanks for sharing!" };
    static readonly string[] _emojis       = { "😊","😂","❤️","👍","🎉","🌟","🦄","🎮","🍕","⚽","🌈","🦋","🥳","🤗","✨","💜" };

    protected override async Task OnInitializedAsync()
    {
        await AuthState.InitAsync();
        if (!AuthState.IsAuthenticated || !AuthState.IsChild) { Nav.NavigateTo("/login"); return; }

        Hub.OnMessageReceived += OnMsg;
        Hub.OnUserTyping      += OnTyping;
        Hub.OnStateChanged    += OnHubState;
        await Hub.StartAsync(AuthState.CurrentUser!.Token);

        var resp = await Api.GetAsync<ClassmatesResponse>("classes/classmates");
        _classmates = resp?.Classmates ?? new();
        _className  = resp?.ClassName ?? "";
        if (_classmates.Any())
        {
            _selectedClassmate = _classmates[0];
            await LoadHistoryAsync(_classmates[0].Id);
        }
        _loadingClassmates = false;
        StateHasChanged();
    }

    async Task LoadHistoryAsync(int otherUserId)
    {
        var history = await Api.GetAsync<List<DmHistoryItem>>($"messages/history/{otherUserId}");
        if (history == null) return;
        _messages = history.Select(h => new ChatMessage
        {
            SenderId   = h.SenderId,
            SenderName = h.SenderId == AuthState.CurrentUser!.UserId ? "You" : (_selectedClassmate?.DisplayName ?? ""),
            Content    = h.Content,
            Label      = h.Label,
            Timestamp  = h.Timestamp
        }).ToList();
        StateHasChanged();
    }

    void SelectClassmate(ClassmateDto cm)
    {
        _selectedClassmate = cm;
        _messages.Clear();
        _showEmoji = false;
        StateHasChanged();
        _ = LoadHistoryAsync(cm.Id);
    }

    void AppendEmoji(string e) { _input += e; _showEmoji = false; }

    void OnMsg(ChatMessage m)     { _messages.Add(m); InvokeAsync(StateHasChanged); }
    void OnHubState(HubState s)   { _hubState = s; InvokeAsync(StateHasChanged); }
    void OnTyping(string name)
    {
        _typingUser = name;
        InvokeAsync(StateHasChanged);
        _ = ClearTypingAfterDelayAsync();
    }

    async Task ClearTypingAfterDelayAsync()
    {
        await Task.Delay(3000);
        _typingUser = "";
        await InvokeAsync(StateHasChanged);
    }

    async Task OnKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") { await Send(); return; }
        await Hub.SendTypingAsync(0);
    }

    async Task Send()
    {
        if (string.IsNullOrWhiteSpace(_input) || _sending) return;
        _sending   = true;
        _showEmoji = false;
        var text       = _input; _input = "";
        var receiverId = _selectedClassmate?.Id ?? 0;

        var local = new ChatMessage
        {
            SenderId   = AuthState.CurrentUser!.UserId,
            SenderName = "You",
            Content    = text,
            Label      = "pending",
            Timestamp  = DateTime.UtcNow
        };
        _messages.Add(local); StateHasChanged();

        var result = await Api.SendMessageAsync(new SendMessageRequest(receiverId, text));
        _messages.Remove(local);

        if (result != null)
        {
            local.Label   = result.Label;
            local.Content = result.Label == "Watch" ? result.MaskedMessage ?? text : text;
            _messages.Add(local);

            (_toast, _toastClass) = result.Label switch
            {
                "Safe"   => ("✓ Sent! +10 points", "t-safe"),
                "Watch"  => ("Message flagged and masked.", "t-warn"),
                "Review" => ("Blocked — please be kind! 💜", "t-block"),
                _        => ("", "")
            };
        }

        _sending = false; StateHasChanged();
        await Task.Delay(3500);
        _toast = ""; StateHasChanged();
    }

    string WaBblClass(ChatMessage m)
    {
        if (m.SenderId != AuthState.CurrentUser?.UserId) return "";
        return m.Label switch { "Watch" => "flagged-bbl", "Review" => "blocked-bbl", "pending" => "pending-bbl", _ => "" };
    }

    async Task ReportMsg(ChatMessage msg)
    {
        var ok = await Api.ReportAbuseAsync($"Reported message from {msg.SenderName}");
        _toast      = ok ? "Reported. Thank you!" : "Failed to report.";
        _toastClass = ok ? "t-safe" : "t-block";
        StateHasChanged();
        await Task.Delay(3000);
        _toast = ""; StateHasChanged();
    }

    static string ShortName(string name) => name.Split(' ').First();

    public async ValueTask DisposeAsync()
    {
        Hub.OnMessageReceived -= OnMsg;
        Hub.OnUserTyping      -= OnTyping;
        Hub.OnStateChanged    -= OnHubState;
        await Hub.DisposeAsync();
    }
}
