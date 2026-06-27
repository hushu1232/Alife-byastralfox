using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public sealed record QChatAgentCapabilities(
    bool AllowComputerFileTools,
    bool AllowProjectModification,
    bool AllowRecall,
    bool AllowPoke);

public sealed record QChatAgentProfile(
    string AgentId,
    string DisplayName,
    string PersonaPath,
    string MemoryScope,
    string Model,
    string OwnerAddressName,
    IReadOnlyList<string> PersonaTags,
    QChatAgentCapabilities Capabilities);

public sealed class QChatProfileService
{
    readonly Dictionary<string, QChatAgentProfile> profiles;

    public QChatProfileService(IReadOnlyDictionary<string, QChatAgentProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        this.profiles = new Dictionary<string, QChatAgentProfile>(profiles, StringComparer.OrdinalIgnoreCase);
    }

    public static QChatProfileService CreateDefault()
    {
        Dictionary<string, QChatAgentProfile> profiles = QChatAgentIdentityRegistry.CreateDefault()
            .GetAll()
            .ToDictionary(identity => identity.AgentId, identity => identity.Profile, StringComparer.OrdinalIgnoreCase);

        return new QChatProfileService(profiles);
    }

    public QChatAgentProfile Get(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            throw new InvalidOperationException("QChat agent id is required.");

        if (profiles.TryGetValue(agentId.Trim(), out QChatAgentProfile? profile))
            return profile;

        throw new InvalidOperationException($"QChat profile '{agentId}' is not configured.");
    }

    public QChatAgentProfile Get(QChatAgentRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return Get(route.AgentId);
    }
}
