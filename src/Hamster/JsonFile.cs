using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hamster;

public sealed class JsonFile<T>(string path, T empty)
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public T Load()
    {
        if (!File.Exists(path))
            return empty;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? empty;
        }
        catch (JsonException)
        {
            // A half-written file must not stop the pet from starting.
            return empty;
        }
    }

    public void Save(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
    }
}
