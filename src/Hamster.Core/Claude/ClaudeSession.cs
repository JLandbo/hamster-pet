using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Hamster.Core.Chats;
using Hamster.Core.Languages;

namespace Hamster.Core.Claude;

public sealed class ClaudeSession(Task<(TextReader Output, TextWriter Input)> streams, IClaudeListener listener)
{
    readonly Lock _order = new();
    readonly ConcurrentDictionary<string, CancellationTokenSource> _questions = new();
    readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject?>> _replies = new();
    Task _writing = streams;
    volatile bool _detached;

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
                    {
                        Results++;
                    }
                    if (!_detached)
                    {
                        Dispatch(message);
                    }
                }
            }
        }
        finally
        {
            Detach();
        }
    }

    public Task SendAsync(string id, string prompt, IReadOnlyList<ImageAttachment> images) => Queue(() => Write(_detached ? throw NotRunning() : ClaudeProtocol.UserMessage(prompt, images, id)));

    public async Task<JsonObject?> RequestAsync(JsonObject request, TimeSpan timeout)
    {
        var id = Guid.NewGuid().ToString();
        var reply = _replies[id] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (_detached)
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
            _replies.TryRemove(id, out _);
        }
    }

    public Task SendAsync(string json) => Queue(() => Write(json));

    public Task CloseAsync() => Queue(() => Input.Close());

    Task Queue(Action write)
    {
        lock (_order)
        {
            return _writing = _writing.ContinueWith(_ => write(), TaskScheduler.Default);
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
        _detached = true;
        CancelOpen();
    }

    void Dispatch(ClaudeEvent message)
    {
        switch (message)
        {
            case TurnStarted turn:
                listener.TurnStarted(turn.MessageId);
                break;
            case PartialMessageStarted partial:
                listener.PartialMessageStarted(partial.MessageId);
                break;
            case PartialMessageText partial:
                listener.PartialMessageReceived(partial.Text);
                break;
            case ToolUse tool:
                listener.ToolStarted(tool);
                break;
            case ToolResult toolResult:
                listener.ToolFinished(toolResult);
                break;
            case PermissionRequest request:
                var withdrawal = _questions[request.RequestId] = new CancellationTokenSource();
                _ = AnswerAsync(request, withdrawal.Token);
                break;
            case CancelRequest cancel when _questions.TryRemove(cancel.RequestId, out var withdrawn):
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
            case ControlReply { Error: { } error } reply when _replies.TryRemove(reply.RequestId, out var waiter):
                waiter.TrySetException(new InvalidOperationException(error));
                break;
            case ControlReply reply when _replies.TryRemove(reply.RequestId, out var waiter):
                waiter.TrySetResult(reply.Response);
                break;
        }
    }

    async Task AnswerAsync(PermissionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await listener.AskPermissionAsync(request, cancellationToken);
            if (_questions.TryRemove(request.RequestId, out _))
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
        foreach (var id in _questions.Keys)
        {
            if (_questions.TryRemove(id, out var question))
            {
                question.Cancel();
            }
        }
        foreach (var id in _replies.Keys)
        {
            if (_replies.TryRemove(id, out var reply))
            {
                reply.TrySetCanceled();
            }
        }
    }
}
