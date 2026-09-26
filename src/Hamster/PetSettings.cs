namespace Hamster;

public sealed record PetSettings(string CharacterName, string? ThemeName = null)
{
    public static PetSettings Default { get; } = new(Character.Hamster.Name);
}
