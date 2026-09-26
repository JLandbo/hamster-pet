namespace Hamster;

public sealed record ClaudeSettings(string Model, string Effort, string PermissionMode, string? WorkingDirectory = null, IReadOnlyList<string>? ClaudeAiConnectors = null, IReadOnlyList<string>? OffConnectors = null, IReadOnlyList<string>? LoginConnectors = null, string? LastFolder = null)
{
    public static ClaudeSettings Default { get; } = new("claude-opus-5-5", "xhigh", "default");
}
