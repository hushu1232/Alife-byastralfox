using System.Linq;
using Alife.Framework;

namespace Alife.Function.WebBridge;

public class CharacterSystemBridgeStore(CharacterSystem characterSystem) : ICharacterBridgeStore
{
    public Character UpsertCharacter(WebAvatarConfig avatarConfig)
    {
        Character? character = characterSystem.GetAllCharacters()
            .FirstOrDefault(item => item.StorageKey == avatarConfig.Id || item.Name == avatarConfig.Name);

        if (character == null)
            character = characterSystem.CreateCharacter(avatarConfig.Name);

        character.Description = avatarConfig.Description;
        character.Prompt = avatarConfig.Prompt;
        character.Modules = [.. avatarConfig.Modules];
        characterSystem.SaveCharacter(character);
        return character;
    }
}
