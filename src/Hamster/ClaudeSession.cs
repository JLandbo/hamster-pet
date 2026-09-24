using System.Collections.Concurrent;
using System.IO;

namespace Hamster;

public sealed class ClaudeSession(TextReader output, TextWriter input, IClaudeListener listener)
{
    readonly TextWriter input = TextWriter.Synchronized(input);
    readonly ConcurrentDictionary<string, CancellationTokenSource> questions = new();
    volatile bool detached;

    public int Results { get; private set; }

    public async Task ReadAsync()
    {
        try
        {
            while (await output.ReadLineAsync() is { } line)
            {
                foreach (var message in ClaudeProtocol.Parse(line))
                {
                    if (message is ClaudeResult)
                        Results++;
                    if (!detached)
                        Dispatch(message);
                }
            }
        }
        finally
        {
            CancelQuestions();
        }
    }

    public Task SendAsync(string id, string prompt, IReadOnlyList<ImageAttachment> images) =>
        Task.Run(() => Send(ClaudeProtocol.UserMessage(prompt, images, id)));

    public void Send(string json)
    {
        input.Write(json + '\n');
        input.Flush();
    }

    public void Detach()
    {
        detached = true;
        CancelQuestions();
    }

    void Dispatch(ClaudeEvent message)
    {
        switch (message)
        {
            case TurnStarted turn:
                listener.TurnStarted(turn.MessageId);
                break;
            case ToolUse tool:
                listener.ToolStarted(tool);
                break;
            case ToolResult toolResult:
                listener.ToolFinished(toolResult);
                break;
            case PermissionRequest request:
                var withdrawal = questions[request.RequestId] = new CancellationTokenSource();
                _ = AnswerAsync(request, withdrawal.Token);
                break;
            case CancelRequest cancel when questions.TryRemove(cancel.RequestId, out var withdrawn):
                withdrawn.Cancel();
                break;
            case Usage usage:
                listener.UsageReported(usage);
                break;
            case ModeChanged mode:
                listener.ModeChanged(mode.Mode);
                break;
            case BackgroundTasksChanged tasks:
                listener.BackgroundTasksChanged(tasks.Count);
                break;
            case ClaudeResult result:
                listener.ResultReceived(result);
                break;
        }
    }

    async Task AnswerAsync(PermissionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var allowed = await listener.AskPermissionAsync(request, cancellationToken);
            if (questions.TryRemove(request.RequestId, out _))
                Send(allowed ? ClaudeProtocol.Allow(request) : ClaudeProtocol.Deny(request));
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    void CancelQuestions()
    {
        foreach (var id in questions.Keys)
            if (questions.TryRemove(id, out var question))
                question.Cancel();
    }
}
