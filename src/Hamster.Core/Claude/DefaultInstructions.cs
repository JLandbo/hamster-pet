namespace Hamster.Core.Claude;

// Every user starts with the same instructions and changes them in their own file, which is never overwritten.
public static class DefaultInstructions
{
    public static string Text { get; } = Read();

    public static void WriteIfMissing(string path)
    {
        if (File.Exists(path))
        {
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Text);
    }

    static string Read()
    {
        using var reader = new StreamReader(typeof(DefaultInstructions).Assembly.GetManifestResourceStream("DefaultInstructions.md")!);
        return reader.ReadToEnd();
    }
}
