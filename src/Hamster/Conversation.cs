using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace Hamster;

/// <summary>The chat with Claude: history, the running turn, and the questions Claude needs the user to answer.</summary>
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

    public Conversation(IClaudeClient claude, JsonFile<SavedChats> store)
    {
        (this.claude, this.store) = (claude, store);
        var saved = store.Load();
        (sessionId, Cost) = (saved.SessionId, saved.Cost);
        foreach (var chat in saved.Chats)
            Chats.Add(ChatItem.From(chat));
    }

    public event Action? Changed;
    public event Action<ChatItem>? Answered;

    public ObservableCollection<ChatItem> Chats { get; } = [];
    public decimal Cost { get; private set; }
    public bool IsBusy => run is not null;
    public bool IsBrowsingWeb => webTools.Count > 0;
    public bool IsWaitingForUser => Chats.Any(chat => chat.NeedsAction);

    /// <summary>What the collapsed chat list shows: the running chat and a fresh answer.</summary>
    public bool IsCurrent(ChatItem chat, DateTime now) =>
        chat == active || (chat == lastAnswered && now - answeredAt < AnswerShownTime);

    /// <summary>Claude reads attached files itself, so it needs their paths; images travel inside the message and are only named.</summary>
    public static string WithAttachments(string text, IReadOnlyList<string> files, IReadOnlyList<string> images)
    {
        if (files.Count == 0 && images.Count == 0)
            return text;
        var prompt = text.Length > 0 ? text : "Se på de vedhæftede filer.";
        if (files.Count > 0)
            prompt += $"\n\nVedhæftede filer:\n{string.Join('\n', files)}";
        if (images.Count > 0)
            prompt += $"\n\nVedhæftede billeder:\n{string.Join('\n', images)}";
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
            var result = await claude.SendAsync(prompt, images ?? [], sessionId, this, cancellation.Token);
            (chat.Answer, chat.Status, sessionId) = (result.Text, result.IsError ? ChatStatus.Error : ChatStatus.Done, result.SessionId);
            Cost = result.Cost ?? Cost;
        }
        catch (OperationCanceledException)
        {
            (chat.Answer, chat.Status) = ("Afbrudt.", ChatStatus.Error);
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
            Answered?.Invoke(chat);
            Changed?.Invoke();
        }
    }

    public void Reset()
    {
        Cancel();
        foreach (var request in Chats.SelectMany(chat => chat.Requests))
            request.Cancel();
        Chats.Clear();
        (sessionId, Cost) = (null, 0);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Removes the chat from the list; Claude's session still remembers it.</summary>
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
        if (tool.IsWeb && webTools.Add(tool.Id))
            Changed?.Invoke();
    }

    public void ToolFinished(ToolResult result)
    {
        if (webTools.Remove(result.ToolUseId))
            Changed?.Invoke();
    }

    public Task<bool> AskPermissionAsync(PermissionRequest request, CancellationToken cancellationToken) =>
        AskAsync(active!, request.ToolName, request.Details, cancellationToken);

    async Task<bool> AskAsync(ChatItem chat, string title, string details, CancellationToken cancellationToken)
    {
        var request = new UserRequest(title, details);
        chat.Requests.Add(request);
        Changed?.Invoke();
        try
        {
            using var registration = cancellationToken.Register(request.Cancel);
            return await request.Answer;
        }
        finally
        {
            chat.Requests.Remove(request);
            Changed?.Invoke();
        }
    }

    void Save() => store.Save(new SavedChats(sessionId, [.. Chats.Select(chat => chat.ToRecord())], Cost));
}
