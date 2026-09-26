namespace Hamster;

public enum PermissionAnswer { Deny, Allow, AllowAlways }

public sealed class UserRequest(string title, string details, string? alwaysScope = null)
{
    readonly TaskCompletionSource<PermissionAnswer> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Title { get; } = title;
    public string Details { get; } = details;
    public string? AlwaysScope { get; } = alwaysScope;
    public bool CanAllowAlways => AlwaysScope is not null;
    public Task<PermissionAnswer> Answer => answer.Task;

    public void Respond(bool allowed) => answer.TrySetResult(allowed ? PermissionAnswer.Allow : PermissionAnswer.Deny);

    public void RespondAlways() => answer.TrySetResult(PermissionAnswer.AllowAlways);

    public void Cancel() => answer.TrySetCanceled();
}
