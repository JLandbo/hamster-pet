using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hamster;

public sealed class JsonFile<T>(string path, T empty)
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter() },
    };

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
            return empty;
        }
    }

    public void Save(T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            using (var file = File.Create(temporary))
            {
                JsonSerializer.Serialize(file, value, Options);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
