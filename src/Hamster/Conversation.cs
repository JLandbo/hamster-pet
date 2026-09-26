using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace Hamster;

public sealed class Conversation : IClaudeListener
{
    public const int MaxChats = 100;
    public const string BackgroundPrompt = "Fra Claude";
    public const string AnsweredAbove = "Besvaret sammen med beskeden ovenfor.";
    public static readonly TimeSpan AnswerShownTime = TimeSpan.FromSeconds(30);

    readonly IClaudeClient claude;
    JsonFile<SavedChats>? store;
    readonly HashSet<string> webTools = [];
    readonly Dictionary<string, ChatItem> waiting = [];
    readonly Dictionary<string, ChatItem> toolChats = [];
    string? sessionId;
    ChatItem? turn, lastAnswered;
    bool turnRunning, autonomous, stopping, restartWhenIdle;

    public Conversation(IClaudeClient claude, JsonFile<SavedChats>? store)
    {
        (this.claude, this.store) = (claude, store);
        Usage = Show(store?.Load() ?? SavedChats.Empty).Usage;
    }

    public event Action? Changed;

    public event Action? Started;

    public event Action<string>? PermissionModeChanged;

    public ObservableCollection<ChatItem> Chats { get; } = [];
    public bool IsTemporary => store is null;
    public decimal Cost { get; private set; }
    public Usage? Usage { get; private set; }
    public int BackgroundTasks { get; private set; }
    public DateTime AnsweredAt { get; private set; }
    public bool IsBusy => turnRunning || waiting.Count > 0;
    public bool RestartPending => restartWhenIdle;
    public bool IsBrowsingWeb => webTools.Count > 0;
    public bool IsWaitingForUser => Chats.Any(chat => chat.NeedsAction);

    public bool IsCurrent(ChatItem chat, DateTime now) =>
        chat.Status == ChatStatus.Busy || chat.NeedsAction || (chat == lastAnswered && now - AnsweredAt < AnswerShownTime);

    public bool AnsweredWithin(TimeSpan time, DateTime now) => lastAnswered is { Status: ChatStatus.Done } && now - AnsweredAt < time;

    public bool FailedWithin(TimeSpan time, DateTime now) => lastAnswered is { Status: ChatStatus.Error } && now - AnsweredAt < time;

    public static string WithAttachments(string text, IReadOnlyList<string> files, IReadOnlyList<ImageAttachment> images)
    {
        if (files.Count == 0 && images.Count == 0)
            return text;
        var prompt = text.Length > 0 ? text : "Se på de vedhæftede filer.";
        if (files.Count > 0)
            prompt += $"\n\nVedhæftede filer:\n{string.Join('\n', files)}";
        if (images.Count > 0)
            prompt += $"\n\nVedhæftede billeder:\n{string.Join('\n', images.Select(image => image.Name))}";
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
        await claude.StartAsync(sessionId, this);
        Started?.Invoke();
    }

    public async Task SendAsync(string prompt, IReadOnlyList<ImageAttachment>? images = null, string? title = null)
    {
        if (prompt == "/clear")
        {
            Reset();
            return;
        }
        var restart = restartWhenIdle && !IsBusy && BackgroundTasks == 0;
        restartWhenIdle &= !restart;
        var id = Guid.NewGuid().ToString();
        var chat = waiting[id] = Add(new ChatItem(prompt) { Title = title });
        Changed?.Invoke();
        try
        {
            if (restart || !claude.IsRunning)
                await StartClaudeAsync();
            if (!waiting.ContainsKey(id))
                return;
            await claude.SendAsync(id, prompt, images ?? []);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            if (!waiting.Remove(id))
                return;
            (chat.Answer, chat.Status) = ($"Kunne ikke tale med claude: {exception.Message}", ChatStatus.Error);
            MarkAnswered(chat, stopped: false);
            Save();
            Changed?.Invoke();
        }
    }

    public void Reset() => Reset(SavedChats.Empty);

    public void Switch(Func<JsonFile<SavedChats>?> target)
    {
        Save();
        store = target();
        Reset(store?.Load() ?? SavedChats.Empty);
    }

    void Reset(SavedChats saved)
    {
        Chats.Clear();
        waiting.Clear();
        toolChats.Clear();
        (lastAnswered, BackgroundTasks, AnsweredAt, restartWhenIdle) = (null, 0, default, false);
        Show(saved);
        EndTurn();
        _ = StartAsync();
    }

    SavedChats Show(SavedChats saved)
    {
        (sessionId, Cost) = (saved.SessionId, saved.Cost);
        foreach (var chat in saved.Chats)
            Chats.Add(ChatItem.From(chat));
        return saved;
    }

    public void Restart()
    {
        restartWhenIdle = IsBusy || BackgroundTasks > 0;
        if (!restartWhenIdle)
            _ = StartAsync();
    }

    public void Delete(ChatItem chat)
    {
        var id = waiting.FirstOrDefault(entry => entry.Value == chat).Key;
        if (chat == turn)
            Cancel();
        else if (id is not null)
            claude.Withdraw(id);
        if (id is not null)
            waiting.Remove(id);
        if (chat == lastAnswered)
            (lastAnswered, AnsweredAt) = (null, default);
        Remove(chat);
        Save();
        Changed?.Invoke();
    }

    public void Cancel()
    {
        if (turnRunning)
        {
            stopping = true;
            claude.Interrupt();
            return;
        }
        foreach (var (id, chat) in waiting)
        {
            claude.Withdraw(id);
            Finish(chat, new ClaudeResult(null, "", IsError: true), stopped: true);
        }
        waiting.Clear();
        Save();
        Changed?.Invoke();
    }

    public void TurnStarted(string? messageId)
    {
        if (turnRunning)
            return;
        turn = messageId is null ? null : waiting.GetValueOrDefault(messageId);
        (turnRunning, autonomous) = (true, turn is null);
        Changed?.Invoke();
    }

    public void ToolStarted(ToolUse tool)
    {
        if (tool.IsWeb)
            webTools.Add(tool.Id);
        if (ChatFor(tool.ParentId) is { } chat)
        {
            toolChats[tool.Id] = chat;
            (tool.IsWeb ? chat.Sources : chat.Commands).Lines.Add(tool.Description);
        }
        Changed?.Invoke();
    }

    public void ToolFinished(ToolResult result)
    {
        if (webTools.Remove(result.ToolUseId))
            Changed?.Invoke();
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
                chat.Commands.Lines.Add($"Afvist: {request.ToolName}");
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
        var stopped = result.IsError && stopping;
        var failedToStart = result.IsError && result.Answers is null && !turnRunning;
        List<ChatItem> answered = failedToStart ? [.. waiting.Values] : [];
        if (failedToStart)
            waiting.Clear();
        foreach (var id in result.Answers ?? [])
            if (waiting.Remove(id, out var chat))
                answered.Add(chat);

        var target = answered.FirstOrDefault() ?? turn ?? (autonomous && result.Text.Length > 0 ? Add(new ChatItem(BackgroundPrompt)) : null);
        if (target is not null)
            Finish(target, result, stopped);
        foreach (var chat in answered.Skip(1))
        {
            if (failedToStart)
                Finish(chat, result, stopped);
            else
                (chat.Answer, chat.Status) = (AnsweredAbove, ChatStatus.Done);
        }
        if (turn is { Status: ChatStatus.Busy } && turn != target)
            turn.Status = ChatStatus.Done;

        if (result.Cost is { } cost)
            Cost = result.SessionId is not null && result.SessionId == sessionId ? Math.Max(Cost, cost) : cost;
        sessionId = failedToStart ? null : result.SessionId ?? sessionId;
        MarkAnswered(target, stopped);
        EndTurn();
    }

    public void Exited(string error)
    {
        var result = new ClaudeResult(null, error, IsError: true);
        MarkAnswered(turn ?? waiting.Values.FirstOrDefault(), stopping);
        foreach (var chat in waiting.Values)
            Finish(chat, result, stopping);
        if (turn is { Status: ChatStatus.Busy })
            Finish(turn, result, stopping);
        waiting.Clear();
        BackgroundTasks = 0;
        EndTurn();
    }

    void EndTurn()
    {
        (turn, turnRunning, autonomous, stopping) = (null, false, false, false);
        webTools.Clear();
        Save();
        Changed?.Invoke();
    }

    void MarkAnswered(ChatItem? chat, bool stopped)
    {
        if (chat is not null && !stopped && Chats.Contains(chat))
            (lastAnswered, AnsweredAt) = (chat, DateTime.UtcNow);
    }

    ChatItem? ChatFor(string? toolUseId) =>
        toolUseId is not null && toolChats.TryGetValue(toolUseId, out var chat) && Chats.Contains(chat) ? chat
        : turn ?? (autonomous ? turn = Add(new ChatItem(BackgroundPrompt)) : Chats.LastOrDefault());

    ChatItem Add(ChatItem chat)
    {
        Chats.Add(chat);
        while (Chats.Count > MaxChats)
            Remove(Chats[0]);
        return chat;
    }

    void Remove(ChatItem chat)
    {
        foreach (var request in chat.Requests.ToArray())
            request.Respond(false);
        foreach (var (id, owner) in toolChats)
            if (owner == chat)
                toolChats.Remove(id);
        Chats.Remove(chat);
    }

    static void Finish(ChatItem chat, ClaudeResult result, bool stopped)
    {
        chat.NeedsLogin = !stopped && result.NeedsLogin;
        chat.Answer = stopped ? "Afbrudt." : chat.NeedsLogin ? "Du er ikke logget ind i claude." : result.Text;
        chat.Status = result.IsError ? ChatStatus.Error : ChatStatus.Done;
    }

    public void Save() => store?.Save(new SavedChats(sessionId, [.. Chats.Where(chat => chat.Status != ChatStatus.Busy).Select(chat => chat.ToRecord())], Cost, Usage));
}
