using System.IO;

namespace Hamster;

public static class DataFolder
{
    public const string Variable = "HAMSTER_DATA_DIR";

    public static string Current => Of(Environment.GetEnvironmentVariable(Variable));

    public static string Of(string? overridden) =>
        string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hamster")
            : overridden;
}
