namespace Hamster;

public static class ChatExport
{
    public static string ToMarkdown(IEnumerable<ChatRecord> chats) =>
        string.Join("\n\n---\n\n", chats.Select(chat => $"## Du\n\n{chat.Prompt}\n\n## Claude\n\n{(chat.Status == ChatStatus.Busy ? "(svarer stadig)" : chat.Answer)}")) + "\n";
}
