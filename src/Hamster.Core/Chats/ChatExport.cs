using Hamster.Core.Languages;

namespace Hamster.Core.Chats;

public static class ChatExport
{
    public static string ToMarkdown(IEnumerable<ChatRecord> chats)
    {
        var sections = chats.Select(chat => $"## {Strings.Of("Export.You")}\n\n{chat.Prompt}\n\n## Claude\n\n{(chat.Status == ChatStatus.Busy ? Strings.Of("Export.StillAnswering") : chat.Answer)}");
        return string.Join("\n\n---\n\n", sections) + "\n";
    }
}
