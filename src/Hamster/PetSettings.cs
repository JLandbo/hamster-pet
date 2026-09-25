namespace Hamster;

public sealed record PetSettings(string CharacterName)
{
    public static PetSettings Default { get; } = new(Character.Hamster.Name);
}
