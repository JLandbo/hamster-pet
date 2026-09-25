using System.IO;

namespace Hamster;

public sealed class CharacterLibrary(string folder)
{
    public IEnumerable<string> Names =>
        [Character.Hamster.Name, .. Folders.Select(Path.GetFileName).OfType<string>().Where(name => name != Character.Hamster.Name).Order()];

    IEnumerable<string> Folders => Directory.Exists(folder) ? Directory.EnumerateDirectories(folder) : [];

    public Character Load(string name) => name == Character.Hamster.Name ? Character.Hamster : Character.FromFolder(Path.Combine(folder, name));

    public string AddCopy()
    {
        var target = Enumerable.Range(1, int.MaxValue).Select(number => Path.Combine(folder, $"Karakter {number}")).First(path => !Directory.Exists(path));
        Directory.CreateDirectory(target);
        foreach (var file in Character.Files)
            File.WriteAllText(Path.Combine(target, file), Character.ReadBuiltIn(file));
        return target;
    }
}
