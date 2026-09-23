namespace Hamster;

public sealed record ClaudeSettings(string Model, string Effort, string PermissionMode)
{
    // Full model id rather than the "opus" alias, so a newer Opus doesn't replace it silently.
    public static ClaudeSettings Default { get; } = new("claude-opus-5-5", "xhigh", "default");
}
