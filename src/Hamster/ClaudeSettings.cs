namespace Hamster;

public sealed record ClaudeSettings(string Model, string Effort, string PermissionMode, string? WorkingDirectory = null, IReadOnlyList<string>? ClaudeAiConnectors = null)
{
    public static ClaudeSettings Default { get; } = new("claude-opus-5-5", "xhigh", "default");
}
