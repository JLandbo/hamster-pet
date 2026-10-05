namespace Hamster.Core.Claude;

// Every user starts with the same instructions and changes them in their own file, which is never overwritten.
public static class DefaultInstructions
{
    public static string Text { get; } = Read();

    // A file that is there, or that another instance writes at the same time, is left alone,
    // and a folder that cannot be written to only means Claude starts without instructions, so the app still opens.
    public static void WriteIfMissing(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(file);
            writer.Write(Text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    static string Read()
    {
        using var reader = new StreamReader(typeof(DefaultInstructions).Assembly.GetManifestResourceStream("DefaultInstructions.md")!);
        return reader.ReadToEnd();
    }
}
