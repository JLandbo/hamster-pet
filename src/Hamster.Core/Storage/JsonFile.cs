using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hamster.Core.Storage;

public sealed class JsonFile<T>(string path, T empty, bool readOnly = false)
{
    const int _attempts = 5;
    static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(50);
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
        // Virus scanners and search indexing can lock a file for a moment right after it was written, so replacing it is retried.
        for (var attempt = 1; !TryWrite(value) && attempt < _attempts; attempt++)
        {
            Thread.Sleep(_retryDelay);
        }
    }

    bool TryWrite(T value)
    {
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
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
