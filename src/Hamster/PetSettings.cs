namespace Hamster;

public sealed record PetSettings(string CharacterName, string? ThemeName = null, string? LanguageName = null, int SizePercent = 100, bool AutoHide = true)
{
    public static PetSettings Default { get; } = new(Character.Hamster.Name);
}
