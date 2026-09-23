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

    public ObservableCollection<UserRequest> Requests { get; } = [];

    public ObservableCollection<string> Activity { get; } = [];

    public bool NeedsAction => Requests.Count > 0;

    public string DisplayAnswer => Status == ChatStatus.Busy ? "Tygger…" : Answer;

    public ChatRecord ToRecord() => new(Prompt, Answer, Status);

    public static ChatItem From(ChatRecord record) => new(record.Prompt) { Answer = record.Answer, Status = record.Status };

    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
