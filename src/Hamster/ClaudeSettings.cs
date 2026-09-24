namespace Hamster;

public sealed record ClaudeSettings(string Model, string Effort, string PermissionMode, string? WorkingDirectory = null)
{
    public static ClaudeSettings Default { get; } = new("claude-opus-5-5", "xhigh", "default");
}
