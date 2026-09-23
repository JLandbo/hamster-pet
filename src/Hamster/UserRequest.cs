namespace Hamster;

public sealed class UserRequest(string title, string details)
{
    readonly TaskCompletionSource<bool> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Title { get; } = title;
    public string Details { get; } = details;
    public Task<bool> Answer => answer.Task;

    public void Respond(bool allowed) => answer.TrySetResult(allowed);

    public void Cancel() => answer.TrySetCanceled();
}
