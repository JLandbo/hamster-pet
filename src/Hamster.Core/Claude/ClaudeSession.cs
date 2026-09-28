using System.Collections.Concurrent;
using System.IO;
using System.Text.Json.Nodes;
using Hamster.Core.Chats;
using Hamster.Core.Languages;

namespace Hamster.Core.Claude;

public sealed class ClaudeSession(Task<(TextReader Output, TextWriter Input)> streams, IClaudeListener listener)
{
    readonly Lock order = new();
    readonly ConcurrentDictionary<string, CancellationTokenSource> questions = new();
    readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject?>> replies = new();
    Task writing = streams;
    volatile bool detached;

    public ClaudeSession(TextReader output, TextWriter input, IClaudeListener listener) : this(Task.FromResult((output, input)), listener)
    {
    }

    public int Results { get; private set; }

    TextWriter Input => streams.IsCompletedSuccessfully ? streams.Result.Input : throw NotRunning();

    public async Task ReadAsync()
    {
        try
        {
            var (output, _) = await streams;
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

    public Task SendAsync(string id, string prompt, IReadOnlyList<ImageAttachment> images) => Queue(() => Write(detached ? throw NotRunning() : ClaudeProtocol.UserMessage(prompt, images, id)));

    public async Task<JsonObject?> RequestAsync(JsonObject request, TimeSpan timeout)
    {
        var id = Guid.NewGuid().ToString();
        var reply = replies[id] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (detached)
            {
                throw NotRunning();
            }
            await SendAsync(ClaudeProtocol.Control(request, id));
            return await reply.Task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(Strings.Of("Claude.NoAnswer"));
        }
        finally
        {
            replies.TryRemove(id, out _);
        }
    }

    public Task SendAsync(string json) => Queue(() => Write(json));

    public Task CloseAsync() => Queue(() => Input.Close());

    Task Queue(Action write)
    {
        lock (order)
        {
            return writing = writing.ContinueWith(_ => write(), TaskScheduler.Default);
        }
    }

    void Write(string json)
    {
        var input = Input;
        input.Write(json + '\n');
        input.Flush();
    }

    InvalidOperationException NotRunning() => new(streams.Exception?.InnerException?.Message ?? Strings.Of("Claude.NotRunning"));

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
            {
                await SendAsync(answer == PermissionAnswer.Deny ? ClaudeProtocol.Deny(request) : ClaudeProtocol.Allow(request, answer == PermissionAnswer.AllowAlways));
            }
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
