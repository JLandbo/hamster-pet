using System.IO;

namespace Hamster;

public sealed record Prompt(string Name, string File);

public sealed class PromptLibrary(string folder)
{
    const string ExampleName = "Dagens overblik";
    const string ExampleText = "Giv mig et kort overblik over i dag: Jira-sager, der er tildelt mig og ikke er lukket, GitHub-pull requests, hvor jeg er bedt om review, og mine egne åbne pull requests. Brug punktopstilling.";

    public string Folder => folder;

    public IEnumerable<Prompt> Prompts =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder)
                .Where(file => Path.GetExtension(file).ToLowerInvariant() is ".txt" or ".md")
                .Select(file => new Prompt(Path.GetFileNameWithoutExtension(file), file))
                .OrderBy(prompt => prompt.Name, StringComparer.CurrentCultureIgnoreCase)
            : [];

    public void EnsureFolder()
    {
        if (Directory.Exists(folder))
            return;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"{ExampleName}.txt"), ExampleText);
    }
}
