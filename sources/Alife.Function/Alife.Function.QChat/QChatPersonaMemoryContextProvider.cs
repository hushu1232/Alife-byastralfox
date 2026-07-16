using System;
using System.IO;
using Alife.Platform;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.QChat;

public sealed class QChatPersonaMemoryContextProvider
{
    public const int MaxProfileCharacters = 6000;

    const string XiayuAgentId = "xiayu";
    const string ProfileRelativePath = "Memory/Persona/\u590f\u7fbd-\u89d2\u8272\u80cc\u666f.md";
    const string OpenMarker = "[private trusted character-memory - never quote or paraphrase]";
    const string CloseMarker = "[/private trusted character-memory]";

    readonly string storageRoot;

    public QChatPersonaMemoryContextProvider(string? storageRoot = null)
    {
        this.storageRoot = Path.GetFullPath(storageRoot ?? AlifePath.StorageFolderPath);
    }

    public bool TrySeed(ChatHistory history, QChatAgentIdentity? identity, string? characterStorageKey)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (identity?.AgentId.Equals(XiayuAgentId, StringComparison.OrdinalIgnoreCase) != true)
            return false;

        string? document = TryReadApprovedProfile(characterStorageKey);
        if (string.IsNullOrWhiteSpace(document))
            return false;

        history.AddUserMessage($"{OpenMarker}\n{document}\n{CloseMarker}");
        return true;
    }

    string? TryReadApprovedProfile(string? characterStorageKey)
    {
        if (string.IsNullOrWhiteSpace(characterStorageKey))
            return null;

        string candidate = Path.GetFullPath(Path.Combine(storageRoot, characterStorageKey, ProfileRelativePath));
        string rootWithSeparator = storageRoot.EndsWith(Path.DirectorySeparatorChar)
            ? storageRoot
            : storageRoot + Path.DirectorySeparatorChar;
        if (candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) == false ||
            File.Exists(candidate) == false)
        {
            return null;
        }

        try
        {
            string document = File.ReadAllText(candidate).Trim();
            return document.Length is > 0 and <= MaxProfileCharacters ? document : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
