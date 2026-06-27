using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public sealed record QChatAgentIdentity(
    string AgentId,
    long BotAccountId,
    QChatAgentProfile Profile,
    IReadOnlyList<string> CharacterNameAliases);

public sealed class QChatAgentIdentityRegistry
{
    readonly Dictionary<long, QChatAgentIdentity> byBotId;
    readonly Dictionary<string, QChatAgentIdentity> byAgentId;
    readonly IReadOnlyList<QChatAgentIdentity> identities;

    public QChatAgentIdentityRegistry(IEnumerable<QChatAgentIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        this.identities = identities.ToArray();
        byBotId = this.identities
            .Where(identity => identity.BotAccountId > 0)
            .ToDictionary(identity => identity.BotAccountId);
        byAgentId = this.identities
            .ToDictionary(identity => identity.AgentId, StringComparer.OrdinalIgnoreCase);
    }

    public static QChatAgentIdentityRegistry CreateDefault()
    {
        QChatAgentCapabilities defaultCapabilities = new(
            AllowComputerFileTools: true,
            AllowProjectModification: true,
            AllowRecall: true,
            AllowPoke: true);

        QChatAgentProfile xiayu = new(
            "xiayu",
            "\u590f\u7fbd",
            @"C:\Users\hu shu\Desktop\personalitysetting",
            "qchat/xiayu",
            "deepseek-v4-flash",
            "\u672f\u672f",
            ["17-year-old-girl", "high-intelligence", "cold-to-others", "warm-to-owner"],
            defaultCapabilities);
        QChatAgentProfile mixu = new(
            "mixu",
            "\u54aa\u7eea",
            string.Empty,
            "qchat/mixu",
            "deepseek-v4-flash",
            "\u4e3b\u4eba",
            ["catgirl"],
            defaultCapabilities);

        return new QChatAgentIdentityRegistry(
        [
            new QChatAgentIdentity("xiayu", 2905391496, xiayu, ["xiayu", "\u590f\u7fbd"]),
            new QChatAgentIdentity("mixu", 3340947887, mixu, ["mixu", "mio", "\u54aa\u7eea"])
        ]);
    }

    public IReadOnlyList<QChatAgentIdentity> GetAll() => identities;

    public QChatAgentIdentity? ResolveByBotId(long botAccountId)
    {
        return byBotId.GetValueOrDefault(botAccountId);
    }

    public QChatAgentIdentity? ResolveByAgentId(string? agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return null;
        return byAgentId.GetValueOrDefault(agentId.Trim());
    }

    public QChatAgentIdentity? ResolveByCharacterName(string? characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName))
            return null;

        string normalized = characterName.Trim().ToLowerInvariant();
        return identities.FirstOrDefault(identity => identity.CharacterNameAliases
            .Select(alias => alias.Trim().ToLowerInvariant())
            .Any(alias => normalized == alias || normalized.Contains(alias, StringComparison.Ordinal)));
    }
}
