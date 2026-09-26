using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Hamster;

public enum ChatStatus { Busy, Done, Error }

public sealed record ChatRecord(string Prompt, string Answer, ChatStatus Status, string? Title = null, IReadOnlyList<string>? Commands = null, IReadOnlyList<string>? Sources = null);

public sealed record SavedChats(string? SessionId, IReadOnlyList<ChatRecord> Chats, decimal Cost = 0, Usage? Usage = null)
{
    public static SavedChats Empty { get; } = new(null, []);

    public static string FileFor(string data, string? folder) =>
        folder is null
            ? Path.Combine(data, "chats.json")
            : Path.Combine(data, "Chats", $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder.TrimEnd('\\', '/').ToUpperInvariant())))[..16]}.json");

    public static void Migrate(string data, string? folder)
    {
        var chats = FileFor(data, null);
        if (folder is null || !File.Exists(chats) || Directory.Exists(Path.Combine(data, "Chats")))
            return;
        var target = FileFor(data, folder);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(chats, target);
    }
}
