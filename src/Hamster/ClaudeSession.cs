using System.Collections.Concurrent;
using System.IO;
using System.Text.Json.Nodes;

namespace Hamster;

public sealed class ClaudeSession(TextReader output, TextWriter input, IClaudeListener listener)
{
    readonly TextWriter input = TextWriter.Synchronized(input);
    readonly ConcurrentDictionary<string, CancellationTokenSource> questions = new();
    readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject?>> replies = new();
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
            Detach();
        }
    }

    public Task SendAsync(string id, string prompt, IReadOnlyList<ImageAttachment> images) =>
        Task.Run(() => Send(ClaudeProtocol.UserMessage(prompt, images, id)));

    public async Task<JsonObject?> RequestAsync(JsonObject request, TimeSpan timeout)
    {
        var id = Guid.NewGuid().ToString();
        var reply = replies[id] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (detached)
                throw new InvalidOperationException("claude kører ikke.");
            await Task.Run(() => Send(ClaudeProtocol.Control(request, id)));
            return await reply.Task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("claude svarede ikke.");
        }
        finally
        {
            replies.TryRemove(id, out _);
        }
    }

    public void Send(string json)
    {
        input.Write(json + '\n');
        input.Flush();
    }

    public void Detach()
    {
        detached = true;
        CancelOpen();
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
            case ControlReply { Error: { } error } reply when replies.TryRemove(reply.RequestId, out var waiter):
                waiter.TrySetException(new InvalidOperationException(error));
                break;
            case ControlReply reply when replies.TryRemove(reply.RequestId, out var waiter):
                waiter.TrySetResult(reply.Response);
                break;
        }
    }

    async Task AnswerAsync(PermissionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await listener.AskPermissionAsync(request, cancellationToken);
            if (questions.TryRemove(request.RequestId, out _))
                Send(answer == PermissionAnswer.Deny ? ClaudeProtocol.Deny(request) : ClaudeProtocol.Allow(request, answer == PermissionAnswer.AllowAlways));
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    void CancelOpen()
    {
        foreach (var id in questions.Keys)
            if (questions.TryRemove(id, out var question))
                question.Cancel();
        foreach (var id in replies.Keys)
            if (replies.TryRemove(id, out var reply))
                reply.TrySetCanceled();
    }
}
