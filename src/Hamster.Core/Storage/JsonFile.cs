using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hamster.Core.Storage;

public sealed class JsonFile<T>(string path, T empty, bool readOnly = false)
{
    static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public T Load()
    {
        if (!File.Exists(path))
        {
            return empty;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), _options) ?? empty;
        }
        catch (JsonException)
        {
            return empty;
        }
    }

    public void Save(T value)
    {
        if (readOnly)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            using (var file = File.Create(temporary))
            {
                JsonSerializer.Serialize(file, value, _options);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
