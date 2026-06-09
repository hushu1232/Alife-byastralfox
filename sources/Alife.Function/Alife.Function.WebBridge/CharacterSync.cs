using Alife.Framework;

namespace Alife.Function.WebBridge;

public static class CharacterSync
{
    public static Character ToCharacter(WebAvatarConfig avatarConfig)
    {
        return new Character
        {
            Name = string.IsNullOrWhiteSpace(avatarConfig.Name) ? "FOXD角色" : avatarConfig.Name.Trim(),
            Description = avatarConfig.Description,
            Prompt = avatarConfig.Prompt,
            Modules = [.. avatarConfig.Modules]
        };
    }

    public static WebAvatarConfig ToAvatarConfig(Character character)
    {
        return new WebAvatarConfig
        {
            Id = character.StorageKey,
            Name = character.Name,
            Description = character.Description,
            Prompt = character.Prompt,
            Modules = [.. character.Modules]
        };
    }
}
