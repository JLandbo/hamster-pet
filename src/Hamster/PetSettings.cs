namespace Hamster;

public sealed record PetSettings(string CharacterName, string? ThemeName = null, string? LanguageName = null)
{
    public static PetSettings Default { get; } = new(Character.Hamster.Name);
}
