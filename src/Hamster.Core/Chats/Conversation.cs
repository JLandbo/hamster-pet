using System.Collections.ObjectModel;
using System.ComponentModel;
using Hamster.Core.Claude;
using Hamster.Core.Languages;
using Hamster.Core.Storage;

namespace Hamster.Core.Chats;

public sealed class Conversation : IClaudeListener
{
    public const int MaxChats = 100;
    public static string BackgroundPrompt => Strings.Of("Chat.FromClaude");
    public static string AnsweredAbove => Strings.Of("Chat.AnsweredAbove");
    static readonly TimeSpan _readTime = TimeSpan.FromSeconds(2);

    readonly IClaudeClient _claude;
    JsonFile<SavedChats>? _store;
    readonly HashSet<string> _webTools = [];
    readonly Dictionary<string, ChatItem> _waiting = [];
    readonly Dictionary<string, ChatItem> _toolChats = [];
    string? _sessionId;
    ChatItem? _turn, _lastAnswered, _partialChat;
    bool _turnRunning, _autonomous, _stopping, _restartWhenIdle;

    public Conversation(IClaudeClient claude, JsonFile<SavedChats>? store)
    {
        (_claude, _store) = (claude, store);
        Usage = Show(store?.Load() ?? SavedChats.Empty).Usage;
    }

    public event Action? Changed;

    public event Action? Started;

    public event Action<string>? PermissionModeChanged;

    public ObservableCollection<ChatItem> Chats { get; } = [];
    public bool IsTemporary => _store is null;
    public decimal Cost { get; private set; }
    public Usage? Usage { get; private set; }
    public int BackgroundTasks { get; private set; }
    public DateTime AnsweredAt { get; private set; }
    public bool IsBusy => _turnRunning || _waiting.Count > 0;
    public bool RestartPending => _restartWhenIdle;
    public bool IsBrowsingWeb => _webTools.Count > 0;
    public bool IsWaitingForUser => Chats.Any(chat => chat.NeedsAction);

    public bool IsCurrent(ChatItem chat, bool showLastResponse, DateTime sentAt) => chat.Status == ChatStatus.Busy || chat.NeedsAction
        || (chat == _lastAnswered && showLastResponse && sentAt - AnsweredAt < _readTime);

    public bool AnsweredWithin(TimeSpan time, DateTime now) => _lastAnswered is { Status: ChatStatus.Done } && now - AnsweredAt < time;

    public bool FailedWithin(TimeSpan time, DateTime now) => _lastAnswered is { Status: ChatStatus.Error } && now - AnsweredAt < time;

    public bool FailedSince(DateTime seenAt) => _lastAnswered is { Status: ChatStatus.Error } && AnsweredAt > seenAt;

    public static string WithAttachments(string text, IReadOnlyList<string> files, IReadOnlyList<ImageAttachment> images)
    {
        if (files.Count == 0 && images.Count == 0)
        {
            return text;
        }
        var prompt = text.Length > 0 ? text : Strings.Of("Chat.LookAtAttachments");
        if (files.Count > 0)
        {
            prompt += $"\n\n{Strings.Of("Chat.AttachedFiles")}\n{string.Join('\n', files)}";
        }
        if (images.Count > 0)
        {
            prompt += $"\n\n{Strings.Of("Chat.AttachedImages")}\n{string.Join('\n', images.Select(image => image.Name))}";
        }
        return prompt;
    }

    public async Task StartAsync()
    {
        try
        {
            await StartClaudeAsync();
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
        }
    }

    async Task StartClaudeAsync()
    {
        await _claude.StartAsync(_sessionId, this);
        Started?.Invoke();
    }

    public async Task SendAsync(string prompt, IReadOnlyList<ImageAttachment>? images = null, string? title = null)
    {
        if (prompt == "/clear")
        {
            Reset();
            return;
        }
        var restart = _restartWhenIdle && !IsBusy && BackgroundTasks == 0;
        _restartWhenIdle &= !restart;
        var id = Guid.NewGuid().ToString();
        var chat = _waiting[id] = Add(new ChatItem(prompt) { Title = title });
        Changed?.Invoke();
        try
        {
            var starting = restart || !_claude.IsRunning ? StartClaudeAsync() : Task.CompletedTask;
            var sending = _claude.SendAsync(id, prompt, images ?? []);
            await starting;
            await sending;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            if (!_waiting.Remove(id))
            {
                return;
            }
            (chat.Answer, chat.Status) = (Strings.Format("Chat.CouldNotTalkToClaude", exception.Message), ChatStatus.Error);
            MarkAnswered(chat, stopped: false);
            Save();
            Changed?.Invoke();
        }
    }

    public void Reset() => Reset(SavedChats.Empty);

    public void Switch(Func<JsonFile<SavedChats>?> target)
    {
        Save();
        _store = target();
        Reset(_store?.Load() ?? SavedChats.Empty);
    }

    void Reset(SavedChats saved)
    {
        Chats.Clear();
        _waiting.Clear();
        _toolChats.Clear();
        (_lastAnswered, BackgroundTasks, AnsweredAt, _restartWhenIdle) = (null, 0, default, false);
        Show(saved);
        EndTurn();
        _ = StartAsync();
    }

    SavedChats Show(SavedChats saved)
    {
        (_sessionId, Cost) = (saved.SessionId, saved.Cost);
        foreach (var chat in saved.Chats)
        {
            Chats.Add(ChatItem.From(chat));
        }
        return saved;
    }

    public void Restart()
    {
        _restartWhenIdle = IsBusy || BackgroundTasks > 0;
        if (!_restartWhenIdle)
        {
            _ = StartAsync();
        }
    }

    public void Delete(ChatItem chat)
    {
        var id = _waiting.FirstOrDefault(entry => entry.Value == chat).Key;
        if (chat == _turn)
        {
            Cancel();
        }
        else if (id is not null)
        {
            _claude.Withdraw(id);
        }
        if (id is not null)
        {
            _waiting.Remove(id);
        }
        if (chat == _lastAnswered)
        {
            (_lastAnswered, AnsweredAt) = (null, default);
        }
        Remove(chat);
        Save();
        Changed?.Invoke();
    }

    public void Cancel()
    {
        if (_turnRunning)
        {
            _stopping = true;
            _claude.Interrupt();
            return;
        }
        foreach (var (id, chat) in _waiting)
        {
            _claude.Withdraw(id);
            Finish(chat, new ClaudeResult(null, "", IsError: true), stopped: true);
        }
        _waiting.Clear();
        Save();
        Changed?.Invoke();
    }

    public void TurnStarted(string? messageId)
    {
        if (_turnRunning)
        {
            return;
        }
        _turn = messageId is null ? null : _waiting.GetValueOrDefault(messageId);
        (_turnRunning, _autonomous) = (true, _turn is null);
        Changed?.Invoke();
    }

    public void PartialMessageStarted(string? messageId)
    {
        _partialChat = _turn ?? (messageId is { } id ? _waiting.GetValueOrDefault(id) : null);
        _partialChat?.StartPartialAnswer();
    }

    public void PartialMessageReceived(string text)
    {
        if (_partialChat is null && _autonomous)
        {
            _partialChat = _turn = Add(new ChatItem(BackgroundPrompt));
            _partialChat.StartPartialAnswer();
            Changed?.Invoke();
        }
        if (_partialChat is not null && Chats.Contains(_partialChat))
        {
            _partialChat.AppendPartialAnswer(text);
        }
    }

    public void ToolStarted(ToolUse tool)
    {
        if (tool.IsWeb)
        {
            _webTools.Add(tool.Id);
        }
        if (ChatFor(tool.ParentId) is { } chat)
        {
            _toolChats[tool.Id] = chat;
            (tool.IsWeb ? chat.Sources : chat.Commands).Lines.Add(tool.Description);
        }
        Changed?.Invoke();
    }

    public void ToolFinished(ToolResult result)
    {
        if (_webTools.Remove(result.ToolUseId))
        {
            Changed?.Invoke();
        }
    }

    public async Task<PermissionAnswer> AskPermissionAsync(PermissionRequest request, CancellationToken cancellationToken)
    {
        var chat = ChatFor(request.ToolUseId) ?? Add(new ChatItem(BackgroundPrompt) { Status = ChatStatus.Done });
        var question = new UserRequest(request.ToolName, request.Details, request.AlwaysScope);
        chat.Requests.Add(question);
        Changed?.Invoke();
        try
        {
            using var registration = cancellationToken.Register(question.Cancel);
            var answer = await question.Answer;
            if (answer == PermissionAnswer.Deny)
            {
                chat.Commands.Lines.Add(Strings.Format("Chat.Denied", request.ToolName));
            }
            return answer;
        }
        finally
        {
            chat.Requests.Remove(question);
            Changed?.Invoke();
        }
    }

    public void UsageReported(Usage usage)
    {
        Usage = usage;
        Changed?.Invoke();
    }

    public void ModeChanged(string mode) => PermissionModeChanged?.Invoke(mode);

    public void BackgroundTasksChanged(int count)
    {
        BackgroundTasks = count;
        Changed?.Invoke();
    }

    public void ResultReceived(ClaudeResult result)
    {
        var stopped = result.IsError && _stopping;
        var failedToStart = result.IsError && result.Answers is null && !_turnRunning;
        List<ChatItem> answered = failedToStart ? [.. _waiting.Values] : [];
        if (failedToStart)
        {
            _waiting.Clear();
        }
        foreach (var id in result.Answers ?? [])
        {
            if (_waiting.Remove(id, out var chat))
            {
                answered.Add(chat);
            }
        }

        var target = answered.FirstOrDefault() ?? _turn ?? (_autonomous && result.Text.Length > 0 ? Add(new ChatItem(BackgroundPrompt)) : null);
        if (target is not null)
        {
            Finish(target, result, stopped);
        }
        foreach (var chat in answered.Skip(1))
        {
            if (failedToStart)
            {
                Finish(chat, result, stopped);
            }
            else
            {
                (chat.Answer, chat.Status) = (AnsweredAbove, ChatStatus.Done);
            }
        }
        if (_turn is { Status: ChatStatus.Busy } && _turn != target)
        {
            _turn.Status = ChatStatus.Done;
        }

        if (result.Cost is { } cost)
        {
            Cost = result.SessionId is not null && result.SessionId == _sessionId ? Math.Max(Cost, cost) : cost;
        }
        _sessionId = failedToStart ? null : result.SessionId ?? _sessionId;
        MarkAnswered(target, stopped);
        EndTurn();
    }

    public void Exited(string error)
    {
        var result = new ClaudeResult(null, error, IsError: true);
        MarkAnswered(_turn ?? _waiting.Values.FirstOrDefault(), _stopping);
        foreach (var chat in _waiting.Values)
        {
            Finish(chat, result, _stopping);
        }
        if (_turn is { Status: ChatStatus.Busy })
        {
            Finish(_turn, result, _stopping);
        }
        _waiting.Clear();
        BackgroundTasks = 0;
        EndTurn();
    }

    void EndTurn()
    {
        (_turn, _partialChat, _turnRunning, _autonomous, _stopping) = (null, null, false, false, false);
        _webTools.Clear();
        Save();
        Changed?.Invoke();
    }

    void MarkAnswered(ChatItem? chat, bool stopped)
    {
        if (chat is not null && !stopped && Chats.Contains(chat))
        {
            (_lastAnswered, AnsweredAt) = (chat, DateTime.UtcNow);
        }
    }

    ChatItem? ChatFor(string? toolUseId) => toolUseId is not null && _toolChats.TryGetValue(toolUseId, out var chat) && Chats.Contains(chat) ? chat
        : _turn ?? (_autonomous ? _turn = Add(new ChatItem(BackgroundPrompt)) : Chats.LastOrDefault());

    ChatItem Add(ChatItem chat)
    {
        Chats.Add(chat);
        while (Chats.Count > MaxChats)
        {
            Remove(Chats[0]);
        }
        return chat;
    }

    void Remove(ChatItem chat)
    {
        foreach (var request in chat.Requests.ToArray())
        {
            request.Respond(false);
        }
        foreach (var (id, owner) in _toolChats)
        {
            if (owner == chat)
            {
                _toolChats.Remove(id);
            }
        }
        Chats.Remove(chat);
    }

    static void Finish(ChatItem chat, ClaudeResult result, bool stopped)
    {
        chat.NeedsLogin = !stopped && result.NeedsLogin;
        chat.Answer = stopped ? Strings.Of("Chat.Stopped") : chat.NeedsLogin ? Strings.Of("Chat.NotLoggedIn") : result.Text;
        chat.Status = result.IsError ? ChatStatus.Error : ChatStatus.Done;
    }

    public void Save() => _store?.Save(new SavedChats(_sessionId, [.. Chats.Where(chat => chat.Status != ChatStatus.Busy).Select(chat => chat.ToRecord())], Cost, Usage));
}
