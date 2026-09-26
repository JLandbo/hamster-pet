namespace Hamster;

public static class ChatExport
{
    public static string ToMarkdown(IEnumerable<ChatRecord> chats) =>
        string.Join("\n\n---\n\n", chats.Select(chat => $"## {Strings.Of("Export.You")}\n\n{chat.Prompt}\n\n## Claude\n\n{(chat.Status == ChatStatus.Busy ? Strings.Of("Export.StillAnswering") : chat.Answer)}")) + "\n";
}
