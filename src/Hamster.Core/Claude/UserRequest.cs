namespace Hamster.Core.Claude;

public enum PermissionAnswer { Deny, Allow, AllowAlways }

public sealed class UserRequest(string title, string details, string? alwaysScope = null)
{
    static readonly TimeSpan _clickDelay = TimeSpan.FromSeconds(1);

    readonly TaskCompletionSource<PermissionAnswer> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly DateTime _shownAt = DateTime.UtcNow;

    public string Title { get; } = title;
    public string Details { get; } = details;
    public string? AlwaysScope { get; } = alwaysScope;
    public bool CanAllowAlways => AlwaysScope is not null;
    public Task<PermissionAnswer> Answer => _answer.Task;

    public bool CanBeClicked(DateTime now) => now - _shownAt >= _clickDelay;

    public void Respond(bool allowed) => _answer.TrySetResult(allowed ? PermissionAnswer.Allow : PermissionAnswer.Deny);

    public void RespondAlways() => _answer.TrySetResult(PermissionAnswer.AllowAlways);

    public void Cancel() => _answer.TrySetCanceled();
}
