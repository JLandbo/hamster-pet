using System.IO;

namespace Hamster;

/// <summary>An image sent to Claude inside the message itself, so it doesn't have to read (and ask for) the file.</summary>
public sealed record ImageAttachment(string Name, string MediaType, byte[] Data)
{
    static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp",
    };

    /// <returns>null when the file is not an image Claude can see, or cannot be read; it is then sent as a path instead.</returns>
    public static ImageAttachment? FromFile(string path)
    {
        if (!MediaTypes.TryGetValue(Path.GetExtension(path), out var mediaType))
            return null;
        try
        {
            return new ImageAttachment(Path.GetFileName(path), mediaType, File.ReadAllBytes(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
