using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace Hamster;

public sealed class Conversation : IClaudeListener
{
    public const int MaxChats = 100;
    public static readonly TimeSpan AnswerShownTime = TimeSpan.FromSeconds(30);

    readonly IClaudeClient claude;
    readonly JsonFile<SavedChats> store;
    readonly HashSet<string> webTools = [];
    string? sessionId;
    CancellationTokenSource? run;
    ChatItem? active, lastAnswered;
    DateTime answeredAt;
    int resets;

    public Conversation(IClaudeClient claude, JsonFile<SavedChats> store)
    {
        (this.claude, this.store) = (claude, store);
        var saved = store.Load();
        (sessionId, Cost, Usage) = (saved.SessionId, saved.Cost, saved.Usage);
        foreach (var chat in saved.Chats)
            Chats.Add(ChatItem.From(chat));
    }

    public event Action? Changed;

    public ObservableCollection<ChatItem> Chats { get; } = [];
    public decimal Cost { get; private set; }
    public Usage? Usage { get; private set; }
    public bool IsBusy => run is not null;
    public bool IsBrowsingWeb => webTools.Count > 0;
    public bool IsWaitingForUser => Chats.Any(chat => chat.NeedsAction);

    public bool IsCurrent(ChatItem chat, DateTime now) =>
        chat == active || (chat == lastAnswered && now - answeredAt < AnswerShownTime);

    public bool AnsweredWithin(TimeSpan time, DateTime now) => lastAnswered is { Status: ChatStatus.Done } && now - answeredAt < time;

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

    public async Task SendAsync(string prompt, IReadOnlyList<ImageAttachment>? images = null)
    {
        if (IsBusy)
            return;
        var chat = active = new ChatItem(prompt);
        Chats.Add(chat);
        while (Chats.Count > MaxChats)
            Chats.RemoveAt(0);
        using var cancellation = run = new CancellationTokenSource();
        Changed?.Invoke();
        try
        {
            var resetsAtStart = resets;
            var result = await claude.SendAsync(prompt, images ?? [], sessionId, this, cancellation.Token);
            // An answer that arrived just before Stop is kept.
            var stopped = result.IsError && cancellation.IsCancellationRequested;
            chat.NeedsLogin = !stopped && result.NeedsLogin;
            chat.Answer = stopped ? "Afbrudt." : chat.NeedsLogin ? "Du er ikke logget ind i claude." : result.Text;
            chat.Status = result.IsError ? ChatStatus.Error : ChatStatus.Done;
            // After "Ny samtale" the turn's session and cost belong to the thrown-away conversation.
            if (resetsAtStart == resets)
            {
                sessionId = result.SessionId ?? (stopped ? sessionId : null);
                Cost = result.Cost ?? Cost;
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            (chat.Answer, chat.Status) = ($"Kunne ikke tale med claude: {exception.Message}", ChatStatus.Error);
        }
        finally
        {
            (run, active, lastAnswered, answeredAt) = (null, null, chat, DateTime.UtcNow);
            webTools.Clear();
            Save();
            Changed?.Invoke();
        }
    }

    public void Reset()
    {
        resets++;
        Cancel();
        Chats.Clear();
        (sessionId, Cost) = (null, 0);
        Save();
        Changed?.Invoke();
    }

    public void Delete(ChatItem chat)
    {
        if (chat == active)
            Cancel();
        Chats.Remove(chat);
        Save();
        Changed?.Invoke();
    }

    public void Cancel() => run?.Cancel();

    public void ToolStarted(ToolUse tool)
    {
        if (tool.IsWeb)
            webTools.Add(tool.Id);
        if (active is { } chat)
            (tool.IsWeb ? chat.Sources : chat.Commands).Lines.Add(tool.Description);
        Changed?.Invoke();
    }

    public void ToolFinished(ToolResult result)
    {
        if (webTools.Remove(result.ToolUseId))
            Changed?.Invoke();
    }

    public async Task<bool> AskPermissionAsync(PermissionRequest request, CancellationToken cancellationToken)
    {
        var chat = active!;
        var question = new UserRequest(request.ToolName, request.Details);
        chat.Requests.Add(question);
        Changed?.Invoke();
        try
        {
            using var registration = cancellationToken.Register(question.Cancel);
            var allowed = await question.Answer;
            if (!allowed)
                chat.Commands.Lines.Add($"Afvist: {request.ToolName}");
            return allowed;
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

    // Saved half-way, the running chat would come back chewing forever.
    void Save() => store.Save(new SavedChats(sessionId, [.. Chats.Where(chat => chat != active).Select(chat => chat.ToRecord())], Cost, Usage));
}
