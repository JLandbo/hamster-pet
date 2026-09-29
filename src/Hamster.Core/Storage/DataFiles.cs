using Hamster.Core.Chats;
using Hamster.Core.Claude;
using Hamster.Core.Pet;

namespace Hamster.Core.Storage;

public sealed class DataFiles : IDisposable
{
    const string _settings = "settings.json";
    readonly ChatOwner _settingsOwner = new();
    readonly ChatOwner _chatsOwner = new();

    public DataFiles(string folder)
    {
        Folder = folder;
        ChatOnly = !_settingsOwner.TryOwn(Path.Combine(folder, _settings));
    }

    public string Folder { get; }

    public bool ChatOnly { get; }

    public string Workspace => Path.Combine(Folder, "workspace");

    public string Instructions => Path.Combine(Folder, "instructions.txt");

    public string Characters => Path.Combine(Folder, "Characters");

    public string Prompts => Path.Combine(Folder, "Prompts");

    public string Themes => Path.Combine(Folder, "Themes");

    public string WebLogin => Path.Combine(Folder, "WebLogin");

    public JsonFile<ClaudeSettings> Settings => Store(_settings, ClaudeSettings.Default);

    public JsonFile<PetSettings> Pet => Store("pet.json", PetSettings.Default);

    public JsonFile<T> Store<T>(string name, T empty) => new(Path.Combine(Folder, name), empty, ChatOnly);

    public JsonFile<SavedChats>? Chats(string? folder) => SavedChats.FileFor(Folder, folder) is var file && _chatsOwner.TryOwn(file) ? new JsonFile<SavedChats>(file, SavedChats.Empty) : null;

    public void Dispose()
    {
        _settingsOwner.Dispose();
        _chatsOwner.Dispose();
    }
}
