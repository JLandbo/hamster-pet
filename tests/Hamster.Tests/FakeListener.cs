namespace Hamster.Tests;

sealed class FakeListener(PermissionAnswer? answer = PermissionAnswer.Allow) : IClaudeListener
{
    readonly TaskCompletionSource _firstResult = new();

    public CancellationToken Question { get; private set; }
    public List<string?> Turns { get; } = [];
    public List<ToolUse> Started { get; } = [];
    public List<ToolResult> Finished { get; } = [];
    public List<Usage> Usages { get; } = [];
    public List<string> Modes { get; } = [];
    public List<int> Tasks { get; } = [];
    public List<ClaudeResult> Results { get; } = [];
    public bool QuestionCancelledBeforeNextTool { get; private set; }
    public Task FirstResult => _firstResult.Task;

    public void TurnStarted(string? messageId) => Turns.Add(messageId);

    public void ToolStarted(ToolUse tool)
    {
        Started.Add(tool);
        QuestionCancelledBeforeNextTool = Question.IsCancellationRequested;
    }

    public void ToolFinished(ToolResult result) => Finished.Add(result);

    public void UsageReported(Usage usage) => Usages.Add(usage);

    public void ModeChanged(string mode) => Modes.Add(mode);

    public void BackgroundTasksChanged(int count) => Tasks.Add(count);

    public void ResultReceived(ClaudeResult result)
    {
        Results.Add(result);
        _firstResult.TrySetResult();
    }

    public void Exited(string error)
    {
    }

    public async Task<PermissionAnswer> AskPermissionAsync(PermissionRequest request, CancellationToken cancellationToken)
    {
        Question = cancellationToken;
        if (answer is { } given)
        {
            return given;
        }
        var never = new TaskCompletionSource<PermissionAnswer>();
        using var registration = cancellationToken.Register(() => never.TrySetCanceled(cancellationToken));
        return await never.Task;
    }
}
