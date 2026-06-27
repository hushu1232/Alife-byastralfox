using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Alife.Platform;

namespace Alife.Function.QChat;

public sealed record QChatRiskThresholds(
    int LocalBlockThreshold = 120,
    int AutoDeleteFriendThreshold = 160,
    int CriticalAutoDeleteFriendThreshold = 220);

public sealed record QChatRiskUserState(
    string AgentId,
    long BotId,
    long UserId,
    int Score,
    int EventCount,
    bool IsLocallyBlocked,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    IReadOnlyList<string> Reasons);

public sealed record QChatRiskScoreUpdate(
    QChatRiskUserState State,
    bool CrossedLocalBlockThreshold);

public sealed class QChatRiskScoreService
{
    readonly object syncRoot = new();
    readonly Dictionary<string, QChatRiskUserState> states = new(StringComparer.Ordinal);
    readonly string filePath;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public QChatRiskScoreService(string? rootPath = null)
    {
        string root = string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace")
            : rootPath;
        filePath = Path.Combine(root, "qchat-risk-scores.json");
        Load();
    }

    public QChatRiskScoreUpdate AddEvents(
        string agentId,
        long botId,
        long userId,
        IReadOnlyList<QChatRiskEvent> events,
        QChatRiskThresholds thresholds)
    {
        lock (syncRoot)
        {
            string key = BuildKey(agentId, botId, userId);
            states.TryGetValue(key, out QChatRiskUserState? existing);
            DateTimeOffset now = DateTimeOffset.Now;
            int score = (existing?.Score ?? 0) + events.Sum(item => item.Score);
            bool wasBlocked = existing?.IsLocallyBlocked == true;
            bool isBlocked = wasBlocked || score >= thresholds.LocalBlockThreshold;
            string[] reasons = (existing?.Reasons ?? [])
                .Concat(events.Select(item => item.Reason))
                .Where(reason => string.IsNullOrWhiteSpace(reason) == false)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            QChatRiskUserState state = new(
                NormalizeAgentId(agentId),
                Math.Max(0, botId),
                userId,
                score,
                (existing?.EventCount ?? 0) + events.Count,
                isBlocked,
                existing?.FirstSeenAt ?? now,
                now,
                reasons);

            states[key] = state;
            SaveNoLock();
            return new QChatRiskScoreUpdate(state, isBlocked && wasBlocked == false);
        }
    }

    public bool TryGetState(string agentId, long botId, long userId, out QChatRiskUserState? state)
    {
        lock (syncRoot)
        {
            return states.TryGetValue(BuildKey(agentId, botId, userId), out state);
        }
    }

    void Load()
    {
        lock (syncRoot)
        {
            states.Clear();
            if (File.Exists(filePath) == false)
                return;

            QChatRiskUserState[] loaded =
                JsonSerializer.Deserialize<QChatRiskUserState[]>(File.ReadAllText(filePath), JsonOptions) ?? [];
            foreach (QChatRiskUserState state in loaded)
                states[BuildKey(state.AgentId, state.BotId, state.UserId)] = state;
        }
    }

    void SaveNoLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        QChatRiskUserState[] snapshot = states.Values
            .OrderBy(item => item.AgentId, StringComparer.Ordinal)
            .ThenBy(item => item.BotId)
            .ThenBy(item => item.UserId)
            .ToArray();
        File.WriteAllText(filePath, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    static string BuildKey(string agentId, long botId, long userId)
    {
        return $"{NormalizeAgentId(agentId)}:{Math.Max(0, botId)}:{userId}";
    }

    static string NormalizeAgentId(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();
    }
}
