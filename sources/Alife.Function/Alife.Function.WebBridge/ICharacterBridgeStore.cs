using Alife.Framework;

namespace Alife.Function.WebBridge;

public interface ICharacterBridgeStore
{
    Character UpsertCharacter(WebAvatarConfig avatarConfig);
}
