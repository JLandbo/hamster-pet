using System.Collections.ObjectModel;
using System.ComponentModel;
using Hamster.Core.Claude;
using Hamster.Core.Languages;
using Hamster.Core.Storage;

namespace Hamster.Core.Chats;

public sealed class Conversation : IClaudeListener
{
    public const int MaxChats = 100;
    static readonly TimeSpan _readTime = TimeSpan.FromSeconds(2);
    public static string BackgroundPrompt => Strings.Of("Chat.FromClaude");
    public static string AnsweredAbove => Strings.Of("Chat.AnsweredAbove");

    readonly IClaudeClient _claude;
    JsonFile<SavedChats>? _store;
    readonly HashSet<string> _webTools = [];
    readonly Dictionary<string, PendingPrompt> _waiting = [];
    readonly Dictionary<string, ChatItem> _toolChats = [];
    string? _sessionId, _turnMessageId;
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
        var chat = OpenChat();
        ChatPrompt queuedPrompt;
        if (chat is null)
        {
            chat = Add(new ChatItem(prompt) { Title = title });
            queuedPrompt = chat.Prompts[0];
        }
        else
        {
            queuedPrompt = chat.AddPrompt(prompt, title);
        }
        _waiting[id] = new(chat, queuedPrompt);
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
            if (!_waiting.Remove(id, out var failed))
            {
                return;
            }
            var result = new ClaudeResult(null, Strings.Format("Chat.CouldNotTalkToClaude", exception.Message), IsError: true);
            if (failed.Chat.RemovePrompt(failed.Prompt))
            {
                var failedChat = Add(new ChatItem(prompt) { Title = title });
                Finish(failedChat, result, stopped: false);
                MarkAnswered(failedChat, stopped: false);
            }
            else
            {
                foreach (var waitingId in IdsFor(failed.Chat))
                {
                    _claude.Withdraw(waitingId);
                    _waiting.Remove(waitingId);
                }
                Finish(failed.Chat, result, stopped: false);
                MarkAnswered(failed.Chat, stopped: false);
            }
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
        var ids = IdsFor(chat);
        if (chat == _turn)
        {
            Cancel();
            foreach (var id in ids.Where(id => id != _turnMessageId))
            {
                _claude.Withdraw(id);
            }
        }
        else
        {
            foreach (var id in ids)
            {
                _claude.Withdraw(id);
            }
        }
        foreach (var id in ids)
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
        var waiting = _waiting.ToArray();
        foreach (var (id, _) in waiting)
        {
            _claude.Withdraw(id);
        }
        foreach (var chat in waiting.Select(entry => entry.Value.Chat).Distinct())
        {
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
        _turnMessageId = messageId;
        _turn = messageId is null ? null : _waiting.GetValueOrDefault(messageId)?.Chat;
        (_turnRunning, _autonomous) = (true, _turn is null);
        Changed?.Invoke();
    }

    public void PartialMessageStarted(string? messageId)
    {
        _partialChat = _turn ?? (messageId is { } id ? _waiting.GetValueOrDefault(id)?.Chat : null);
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

    public void AssistantTextReceived(string text)
    {
        var chat = _partialChat ?? _turn;
        if (chat is null && _autonomous)
        {
            chat = _partialChat = _turn = Add(new ChatItem(BackgroundPrompt));
            Changed?.Invoke();
        }
        if (chat is not null && Chats.Contains(chat))
        {
            chat.CompletePartialAnswer(text, _claude.Settings.EnablePartialMessages);
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
        List<ChatItem> answered = failedToStart ? [.. _waiting.Values.Select(waiting => waiting.Chat).Distinct()] : [];
        if (failedToStart)
        {
            _waiting.Clear();
        }
        foreach (var id in result.Answers ?? (_turnMessageId is { } turnMessageId ? [turnMessageId] : []))
        {
            if (_waiting.Remove(id, out var waiting))
            {
                answered.Add(waiting.Chat);
            }
        }
        answered = [.. answered.Distinct()];

        var target = answered.FirstOrDefault() ?? _turn ?? (_autonomous && result.Text.Length > 0 ? Add(new ChatItem(BackgroundPrompt)) : null);
        if (target is not null)
        {
            if (stopped)
            {
                foreach (var id in IdsFor(target))
                {
                    _claude.Withdraw(id);
                    _waiting.Remove(id);
                }
            }
            Finish(target, result, stopped, complete: !WaitingFor(target));
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
        if (target is not { Status: ChatStatus.Busy })
        {
            MarkAnswered(target, stopped);
        }
        EndTurn();
    }

    public void Exited(string error)
    {
        var result = new ClaudeResult(null, error, IsError: true);
        var chats = _waiting.Values.Select(waiting => waiting.Chat).Append(_turn).OfType<ChatItem>().Distinct().ToArray();
        MarkAnswered(_turn ?? chats.FirstOrDefault(), _stopping);
        foreach (var chat in chats)
        {
            Finish(chat, result, _stopping);
        }
        _waiting.Clear();
        BackgroundTasks = 0;
        EndTurn();
    }

    void EndTurn()
    {
        (_turn, _partialChat, _turnMessageId, _turnRunning, _autonomous, _stopping) = (null, null, null, false, false, false);
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

    ChatItem? OpenChat() => _turn is { Status: ChatStatus.Busy } && !_autonomous && Chats.Contains(_turn) ? _turn
        : _waiting.Values.Select(waiting => waiting.Chat).LastOrDefault(chat => chat.Status == ChatStatus.Busy);

    string[] IdsFor(ChatItem chat) => [.. _waiting.Where(entry => entry.Value.Chat == chat).Select(entry => entry.Key)];

    bool WaitingFor(ChatItem chat) => _waiting.Values.Any(waiting => waiting.Chat == chat);

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

    static void Finish(ChatItem chat, ClaudeResult result, bool stopped, bool complete = true)
    {
        chat.NeedsLogin = !stopped && result.NeedsLogin;
        chat.CompleteResult(stopped ? Strings.Of("Chat.Stopped") : chat.NeedsLogin ? Strings.Of("Chat.NotLoggedIn") : result.Text);
        if (complete)
        {
            chat.Status = result.IsError ? ChatStatus.Error : ChatStatus.Done;
        }
    }

    public void Save()
    {
        if (_store is null)
        {
            return;
        }
        _store.Save(new SavedChats(_sessionId, [.. Chats.Select(Snapshot).OfType<ChatRecord>()], Cost, Usage));
    }

    ChatRecord? Snapshot(ChatItem chat)
    {
        var pending = _waiting.Values.Where(waiting => waiting.Chat == chat).Select(waiting => waiting.Prompt).ToArray();
        return chat.Status == ChatStatus.Busy && pending.Length == 0 ? null : chat.ToRecordWithout(pending);
    }

    sealed record PendingPrompt(ChatItem Chat, ChatPrompt Prompt);
}
