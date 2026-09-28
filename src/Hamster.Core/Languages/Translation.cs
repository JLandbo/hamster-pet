namespace Hamster.Core.Languages;

public sealed record Translation(string Name, IReadOnlyDictionary<string, string> Texts)
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static Translation Danish { get; } = BuiltIn("Dansk");

    public static Translation English { get; } = BuiltIn("English");

    public static IReadOnlyList<Translation> All { get; } = [Danish, English];

    public static Translation Parse(string name, string json) => new(name, JsonSerializer.Deserialize<SavedTranslation>(json, Options)?.Texts ?? []);

    public string Of(string key) => Texts.GetValueOrDefault(key) ?? Danish.Texts.GetValueOrDefault(key) ?? key;

    public string Format(string key, params object?[] values)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, Of(key), values);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.CurrentCulture, Danish.Of(key), values);
        }
    }

    static Translation BuiltIn(string name)
    {
        using var reader = new StreamReader(typeof(Translation).Assembly.GetManifestResourceStream($"Languages/{name}.json")!);
        return Parse(name, reader.ReadToEnd());
    }

    sealed record SavedTranslation(Dictionary<string, string>? Texts);
}
