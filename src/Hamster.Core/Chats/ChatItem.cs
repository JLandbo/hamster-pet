using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Hamster.Core.Claude;
using Hamster.Core.Languages;

namespace Hamster.Core.Chats;

public sealed class ChatItem : INotifyPropertyChanged
{
    readonly StringBuilder _partialAnswer = new();
    string? _shownPartialAnswer, _lastDisplayAnswer;
    bool _partialAnswerChanged, _replacePartialAnswerOnNextText;

    public ChatItem(string prompt)
    {
        Prompt = prompt;
        Requests.CollectionChanged += (_, _) => Changed(nameof(NeedsAction));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Prompt { get; }

    public string? Title { get; init; }

    public string DisplayPrompt => Title ?? Prompt;

    public bool Shown
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }
            field = value;
            Changed();
        }
    } = true;

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
            if (field != ChatStatus.Busy)
            {
                _partialAnswer.Clear();
                (_shownPartialAnswer, _lastDisplayAnswer, _partialAnswerChanged, _replacePartialAnswerOnNextText) = (null, null, false, false);
            }
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

    public ToolGroup Commands { get; } = new(isWeb: false);

    public ToolGroup Sources { get; } = new(isWeb: true);

    public IReadOnlyList<ToolGroup> ToolGroups => [Commands, Sources];

    public bool NeedsAction => Requests.Count > 0;

    public string DisplayAnswer => Status == ChatStatus.Busy ? _shownPartialAnswer ?? Strings.Format("Chat.Chewing", FormatElapsed(DateTime.UtcNow - StartedAt)) : Answer;

    public static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalMinutes < 1 ? $"{elapsed.Seconds} s" : $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s";

    public void StartPartialAnswer()
    {
        _replacePartialAnswerOnNextText = true;
    }

    public void AppendPartialAnswer(string text)
    {
        if (_replacePartialAnswerOnNextText)
        {
            _partialAnswer.Clear();
            _replacePartialAnswerOnNextText = false;
        }
        _partialAnswer.Append(text);
        _partialAnswerChanged = true;
    }

    public bool RefreshDisplayAnswer()
    {
        if (Status != ChatStatus.Busy)
        {
            return false;
        }
        if (_partialAnswerChanged)
        {
            _shownPartialAnswer = _partialAnswer.Length > 0 ? _partialAnswer.ToString() : null;
            _partialAnswerChanged = false;
        }
        var next = DisplayAnswer;
        if (next == _lastDisplayAnswer)
        {
            return false;
        }
        _lastDisplayAnswer = next;
        Changed(nameof(DisplayAnswer));
        return true;
    }

    public ChatRecord ToRecord() => new(Prompt, Answer, Status, Title, [.. Commands.Lines], [.. Sources.Lines]);

    public static ChatItem From(ChatRecord record)
    {
        var chat = new ChatItem(record.Prompt) { Title = record.Title, Answer = record.Answer, Status = record.Status };
        foreach (var line in record.Commands ?? [])
        {
            chat.Commands.Lines.Add(line);
        }
        foreach (var line in record.Sources ?? [])
        {
            chat.Sources.Lines.Add(line);
        }
        return chat;
    }

    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ToolGroup(bool isWeb)
{
    public bool IsWeb { get; } = isWeb;

    public ObservableCollection<string> Lines { get; } = [];
}
