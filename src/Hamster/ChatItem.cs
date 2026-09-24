using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Hamster;

public sealed class ChatItem : INotifyPropertyChanged
{
    public ChatItem(string prompt)
    {
        Prompt = prompt;
        Requests.CollectionChanged += (_, _) => Changed(nameof(NeedsAction));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Prompt { get; }

    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public string Answer
    {
        get;
        set
        {
            field = value;
            Changed();
            Changed(nameof(DisplayAnswer));
        }
    } = "";

    public ChatStatus Status
    {
        get;
        set
        {
            field = value;
            Changed();
            Changed(nameof(DisplayAnswer));
        }
    } = ChatStatus.Busy;

    public bool NeedsLogin
    {
        get;
        set
        {
            field = value;
            Changed();
        }
    }

    public ObservableCollection<UserRequest> Requests { get; } = [];

    public ToolGroup Commands { get; } = new("Commands");

    public ToolGroup Sources { get; } = new("Kilder");

    public IReadOnlyList<ToolGroup> ToolGroups => [Commands, Sources];

    public bool NeedsAction => Requests.Count > 0;

    public string DisplayAnswer => Status == ChatStatus.Busy ? $"Tygger… {FormatElapsed(DateTime.UtcNow - StartedAt)}" : Answer;

    public static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalMinutes < 1 ? $"{elapsed.Seconds} s" : $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s";

    public void RefreshElapsed()
    {
        if (Status == ChatStatus.Busy)
            Changed(nameof(DisplayAnswer));
    }

    public ChatRecord ToRecord() => new(Prompt, Answer, Status);

    public static ChatItem From(ChatRecord record) => new(record.Prompt) { Answer = record.Answer, Status = record.Status };

    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ToolGroup(string title)
{
    public string Title { get; } = title;

    public ObservableCollection<string> Lines { get; } = [];
}
