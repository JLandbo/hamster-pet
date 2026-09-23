namespace Hamster;

public enum ChatStatus { Busy, Done, Error }

public sealed record ChatRecord(string Prompt, string Answer, ChatStatus Status);

public sealed record SavedChats(string? SessionId, IReadOnlyList<ChatRecord> Chats, decimal Cost = 0)
{
    public static SavedChats Empty { get; } = new(null, []);
}
