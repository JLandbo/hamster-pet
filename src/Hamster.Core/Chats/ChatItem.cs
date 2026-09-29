using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Hamster.Core.Claude;
using Hamster.Core.Languages;

namespace Hamster.Core.Chats;

public sealed class ChatItem : INotifyPropertyChanged
{
    readonly List<ChatPrompt> _prompts;
    readonly StringBuilder _partialAnswer = new();
    string? _shownPartialAnswer, _lastDisplayAnswer, _lastCompletedAnswer;
    bool _partialAnswerChanged, _showPartialAnswer;

    public ChatItem(string prompt)
    {
        _prompts = [new(prompt)];
        Requests.CollectionChanged += (_, _) => Changed(nameof(NeedsAction));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Prompt => _prompts[0].Text;

    public string? Title
    {
        get => _prompts[0].Title;
        init => _prompts[0] = _prompts[0] with { Title = value };
    }

    public IReadOnlyList<ChatPrompt> Prompts => _prompts;

    public string DisplayPrompt => string.Join("\n\n", _prompts.Select(prompt => prompt.Title ?? prompt.Text));

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
                (_shownPartialAnswer, _lastDisplayAnswer, _lastCompletedAnswer, _partialAnswerChanged, _showPartialAnswer) = (null, null, null, false, false);
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

    public string DisplayAnswer
    {
        get
        {
            if (Status != ChatStatus.Busy)
            {
                return Answer;
            }
            if (_showPartialAnswer && Joined(Answer, _shownPartialAnswer) is { Length: > 0 } shown)
            {
                return shown;
            }
            return Strings.Format("Chat.Chewing", FormatElapsed(DateTime.UtcNow - StartedAt));
        }
    }

    public static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalMinutes < 1 ? $"{elapsed.Seconds} s" : $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s";

    public void StartPartialAnswer()
    {
        _showPartialAnswer = true;
        if (_partialAnswer.Length > 0)
        {
            AppendCompletedAnswer(_partialAnswer.ToString());
            ClearPartialAnswer();
        }
    }

    public void AppendPartialAnswer(string text)
    {
        _partialAnswer.Append(text);
        _partialAnswerChanged = true;
    }

    public void CompletePartialAnswer(string text, bool showWhileBusy)
    {
        _showPartialAnswer |= showWhileBusy;
        ClearPartialAnswer();
        AppendCompletedAnswer(text);
    }

    public void CompleteResult(string text)
    {
        ClearPartialAnswer();
        _showPartialAnswer = true;
        if (text.Length > 0 && text != _lastCompletedAnswer)
        {
            AppendCompletedAnswer(text);
        }
    }

    public ChatPrompt AddPrompt(string text, string? title)
    {
        var prompt = new ChatPrompt(text, title);
        _prompts.Add(prompt);
        Changed(nameof(DisplayPrompt));
        return prompt;
    }

    public bool RemovePrompt(ChatPrompt prompt)
    {
        var index = _prompts.FindIndex(candidate => ReferenceEquals(candidate, prompt));
        if (index <= 0)
        {
            return false;
        }
        _prompts.RemoveAt(index);
        Changed(nameof(DisplayPrompt));
        return true;
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

    public ChatRecord ToRecord() => new(Prompt, Answer, Status, Title, [.. Commands.Lines], [.. Sources.Lines], [.. _prompts.Skip(1)]);

    internal ChatRecord? ToRecordWithout(IEnumerable<ChatPrompt> excluded)
    {
        var prompts = _prompts.Where(prompt => !excluded.Any(candidate => ReferenceEquals(candidate, prompt))).ToArray();
        return prompts.Length == 0 ? null : new(prompts[0].Text, Answer, Status == ChatStatus.Busy ? ChatStatus.Done : Status, prompts[0].Title,
            [.. Commands.Lines], [.. Sources.Lines], [.. prompts.Skip(1)]);
    }

    public static ChatItem From(ChatRecord record)
    {
        var chat = new ChatItem(record.Prompt) { Title = record.Title, Answer = record.Answer, Status = record.Status };
        foreach (var prompt in record.AdditionalPrompts ?? [])
        {
            chat.AddPrompt(prompt.Text, prompt.Title);
        }
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

    void AppendCompletedAnswer(string text)
    {
        if (text.Length == 0)
        {
            return;
        }
        Answer = Joined(Answer, text);
        _lastCompletedAnswer = text;
    }

    void ClearPartialAnswer()
    {
        _partialAnswer.Clear();
        (_shownPartialAnswer, _partialAnswerChanged) = (null, false);
    }

    static string Joined(string? first, string? second) => first is not { Length: > 0 } ? second ?? ""
        : second is not { Length: > 0 } ? first : $"{first}\n\n{second}";

    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ToolGroup(bool isWeb)
{
    public bool IsWeb { get; } = isWeb;

    public ObservableCollection<string> Lines { get; } = [];
}
