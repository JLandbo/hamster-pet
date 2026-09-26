namespace Hamster;

public static class Strings
{
    public static Translation Current { get; private set; } = Translation.Danish;

    public static event Action? Changed;

    public static void Use(Translation translation)
    {
        Current = translation;
        Changed?.Invoke();
    }

    public static string Of(string key) => Current.Of(key);

    public static string Format(string key, params object?[] values) => Current.Format(key, values);
}
