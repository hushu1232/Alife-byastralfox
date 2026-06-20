using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.Agent;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.QChat;

public sealed record QChatGroupMemberCacheSnapshot(
    long GroupId,
    DateTimeOffset RefreshedAt,
    IReadOnlyList<OneBotGroupMember> Members);

public sealed record QChatGroupListCacheSnapshot(
    DateTimeOffset RefreshedAt,
    IReadOnlyList<OneBotGroupInfo> Groups);

[Module(
    "QQ Relation Cache",
    "Caches QQ group member lists for safer context-aware interaction planning.",
    defaultCategory: "Alife Official/Interaction",
    LaunchOrder = 9)]
public class QChatRelationCacheService(
    IOneBotRuntime? oneBotRuntime = null,
    XmlFunctionCaller? functionCaller = null)
    : InteractiveModule<QChatRelationCacheService>, IContextContributor, IModuleHealthReporter, IAgentQChatJoinedGroupProvider
{
    IOneBotRuntime? oneBotRuntime = oneBotRuntime;
    readonly Dictionary<long, QChatGroupMemberCacheSnapshot> groupMemberCache = new();
    QChatGroupListCacheSnapshot joinedGroupsCache = new(DateTimeOffset.MinValue, []);
    readonly object syncRoot = new();
    public Action<string, string, object?, Exception?>? DiagnosticWriter { get; set; }
    public Func<string, Task>? ToolResultSink { get; set; }

    public void AttachOneBotRuntime(IOneBotRuntime runtime)
    {
        oneBotRuntime ??= runtime;
    }

    [XmlFunction(FunctionMode.OneShot, name: "qchat_joined_groups_refresh")]
    [Description("Refresh and cache the QQ groups this bot has joined. This is read-only and does not send messages.")]
    public async Task RefreshJoinedGroups()
    {
        try
        {
            QChatGroupListCacheSnapshot snapshot = await RefreshJoinedGroupsAsync();
            await PublishToolResultAsync(FormatGroupList(snapshot, maxGroups: 30));
        }
        catch (Exception exception)
        {
            await PublishToolResultAsync($"QQ joined group refresh failed: {exception.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot, name: "qchat_joined_groups_cache")]
    [Description("Show the cached QQ groups this bot has joined without contacting OneBot.")]
    public Task ShowCachedJoinedGroups()
    {
        return PublishToolResultAsync(FormatGroupList(GetCachedJoinedGroups(), maxGroups: 30));
    }

    [XmlFunction(FunctionMode.OneShot, name: "qchat_group_members_refresh")]
    [Description("Refresh and cache the QQ group member list for a group. This is read-only and does not send messages.")]
    public async Task RefreshGroupMembers(long groupId)
    {
        try
        {
            QChatGroupMemberCacheSnapshot snapshot = await RefreshGroupMembersAsync(groupId);
            await PublishToolResultAsync(FormatSnapshot(snapshot, maxMembers: 20));
        }
        catch (Exception exception)
        {
            await PublishToolResultAsync($"QQ group member refresh failed: {exception.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot, name: "qchat_group_members_cache")]
    [Description("Show the cached QQ group member list for a group without contacting OneBot.")]
    public Task ShowCachedGroupMembers(long groupId)
    {
        return PublishToolResultAsync(FormatSnapshot(GetCachedGroupMembers(groupId), maxMembers: 20));
    }

    async Task PublishToolResultAsync(string message)
    {
        Poke(message);
        if (ToolResultSink != null)
            await ToolResultSink(message);
    }

    public async Task<QChatGroupListCacheSnapshot> RefreshJoinedGroupsAsync()
    {
        DiagnosticWriter?.Invoke(
            "qchat-joined-groups-refresh-start",
            "QQ joined group refresh started.",
            null,
            null);

        try
        {
            if (oneBotRuntime == null)
                throw new InvalidOperationException("OneBot runtime is unavailable.");

            IReadOnlyList<OneBotGroupInfo> groups = await oneBotRuntime.GetGroupList();
            OneBotGroupInfo[] normalizedGroups = groups
                .Where(group => group.GroupId != 0)
                .OrderBy(group => group.GroupId)
                .ToArray();
            QChatGroupListCacheSnapshot snapshot = new(DateTimeOffset.Now, normalizedGroups);

            lock (syncRoot)
                joinedGroupsCache = snapshot;

            DiagnosticWriter?.Invoke(
                "qchat-joined-groups-refresh-succeeded",
                "QQ joined group refresh succeeded.",
                new {
                    count = normalizedGroups.Length,
                    groups = normalizedGroups.Select(group => new {
                        group.GroupId,
                        group.GroupName,
                        group.MemberCount,
                        group.MaxMemberCount
                    }).ToArray()
                },
                null);

            return snapshot;
        }
        catch (Exception exception)
        {
            DiagnosticWriter?.Invoke(
                "qchat-joined-groups-refresh-failed",
                exception.Message,
                null,
                exception);
            throw;
        }
    }

    public async Task<QChatGroupMemberCacheSnapshot> RefreshGroupMembersAsync(long groupId)
    {
        if (groupId == 0)
            throw new ArgumentNullException(nameof(groupId));
        if (oneBotRuntime == null)
            throw new InvalidOperationException("OneBot runtime is unavailable.");

        IReadOnlyList<OneBotGroupMember> members = await oneBotRuntime.GetGroupMemberList(groupId);
        OneBotGroupMember[] normalizedMembers = members
            .Where(member => member.UserId != 0)
            .Select(member => member.GroupId == 0 ? member with { GroupId = groupId } : member)
            .ToArray();
        QChatGroupMemberCacheSnapshot snapshot = new(groupId, DateTimeOffset.Now, normalizedMembers);

        lock (syncRoot)
            groupMemberCache[groupId] = snapshot;

        return snapshot;
    }

    public QChatGroupMemberCacheSnapshot GetCachedGroupMembers(long groupId)
    {
        lock (syncRoot)
        {
            if (groupMemberCache.TryGetValue(groupId, out QChatGroupMemberCacheSnapshot? snapshot))
                return snapshot;
        }

        return new QChatGroupMemberCacheSnapshot(groupId, DateTimeOffset.MinValue, []);
    }

    public IReadOnlyList<QChatGroupMemberCacheSnapshot> GetCachedGroups()
    {
        lock (syncRoot)
            return groupMemberCache.Values.OrderByDescending(snapshot => snapshot.RefreshedAt).ToArray();
    }

    public QChatGroupListCacheSnapshot GetCachedJoinedGroups()
    {
        lock (syncRoot)
            return joinedGroupsCache;
    }

    public async Task<AgentQChatJoinedGroupSourceSnapshot> RefreshAgentJoinedGroupsAsync()
    {
        return ToAgentJoinedGroupSourceSnapshot(await RefreshJoinedGroupsAsync());
    }

    public AgentQChatJoinedGroupSourceSnapshot GetCachedAgentJoinedGroups()
    {
        return ToAgentJoinedGroupSourceSnapshot(GetCachedJoinedGroups());
    }

    public OneBotGroupMember? TryGetMember(long groupId, long userId)
    {
        return GetCachedGroupMembers(groupId).Members.FirstOrDefault(member => member.UserId == userId);
    }

    public IEnumerable<ContextContribution> GetContextContributions()
    {
        QChatGroupMemberCacheSnapshot[] snapshots;
        lock (syncRoot)
            snapshots = groupMemberCache.Values.OrderByDescending(snapshot => snapshot.RefreshedAt).Take(3).ToArray();

        if (snapshots.Length == 0)
        {
            QChatGroupListCacheSnapshot groupSnapshot = GetCachedJoinedGroups();
            if (groupSnapshot.Groups.Count == 0)
                return [];

            return [
                new ContextContribution(
                    "qchat-relation-cache",
                    FormatGroupList(groupSnapshot),
                    Priority: 720,
                    MaxLength: 1800,
                    TrustLevel: ContextTrustLevel.UntrustedExternal)
            ];
        }

        StringBuilder builder = new();
        builder.AppendLine("[QQ relation cache]");
        QChatGroupListCacheSnapshot joinedGroups = GetCachedJoinedGroups();
        if (joinedGroups.Groups.Count > 0)
        {
            builder.AppendLine($"Joined groups: {joinedGroups.Groups.Count}");
            foreach (OneBotGroupInfo group in joinedGroups.Groups.Take(12))
                builder.AppendLine($"- {group.GroupId} {FormatGroupName(group)} members={group.MemberCount}/{group.MaxMemberCount}");
        }

        foreach (QChatGroupMemberCacheSnapshot snapshot in snapshots)
        {
            string groupName = joinedGroups.Groups.FirstOrDefault(group => group.GroupId == snapshot.GroupId)?.GroupName ?? "";
            builder.AppendLine($"Group {snapshot.GroupId}{(string.IsNullOrWhiteSpace(groupName) ? "" : $" {groupName}")}: {snapshot.Members.Count} cached members");
            foreach (OneBotGroupMember member in snapshot.Members.Take(12))
            {
                builder.Append("- ");
                builder.Append(member.UserId);
                builder.Append(' ');
                builder.Append(member.DisplayName);
                if (string.IsNullOrWhiteSpace(member.Role) == false)
                    builder.Append($" ({member.Role})");
                builder.AppendLine();
            }
        }
        builder.Append("[/QQ relation cache]");

        return [
            new ContextContribution(
                "qchat-relation-cache",
                builder.ToString(),
                Priority: 720,
                MaxLength: 1800,
                TrustLevel: ContextTrustLevel.UntrustedExternal)
        ];
    }

    public ModuleHealth GetHealth()
    {
        if (oneBotRuntime == null)
            return new ModuleHealth("QChatRelationCache", ModuleHealthStatus.Unavailable, "OneBot runtime is unavailable.");

        int cachedGroups;
        int cachedMembers;
        int joinedGroups;
        lock (syncRoot)
        {
            cachedGroups = groupMemberCache.Count;
            cachedMembers = groupMemberCache.Values.Sum(snapshot => snapshot.Members.Count);
            joinedGroups = joinedGroupsCache.Groups.Count;
        }

        if (oneBotRuntime.IsConnected == false)
        {
            return new ModuleHealth(
                "QChatRelationCache",
                ModuleHealthStatus.Degraded,
                $"OneBot is disconnected; {FormatCacheCounts(cachedGroups, cachedMembers, joinedGroups)}.");
        }

        return new ModuleHealth(
            "QChatRelationCache",
            ModuleHealthStatus.Healthy,
            $"OneBot is connected; {FormatCacheCounts(cachedGroups, cachedMembers, joinedGroups)}.");
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionCaller?.RegisterHandler(this);
    }

    public static string FormatSnapshot(QChatGroupMemberCacheSnapshot snapshot, int maxMembers = 30)
    {
        StringBuilder builder = new();
        builder.AppendLine($"QQ group members: {snapshot.GroupId}");
        builder.AppendLine(snapshot.RefreshedAt == DateTimeOffset.MinValue
            ? "- cache: empty"
            : $"- refreshed: {snapshot.RefreshedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"- count: {snapshot.Members.Count}");

        foreach (OneBotGroupMember member in snapshot.Members.Take(Math.Max(0, maxMembers)))
        {
            builder.Append("- ");
            builder.Append(member.UserId);
            builder.Append(' ');
            builder.Append(member.DisplayName);
            if (string.IsNullOrWhiteSpace(member.Role) == false)
                builder.Append($" role={member.Role}");
            if (string.IsNullOrWhiteSpace(member.Title) == false)
                builder.Append($" title={member.Title}");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatGroupList(QChatGroupListCacheSnapshot snapshot, int maxGroups = 30)
    {
        StringBuilder builder = new();
        builder.AppendLine("QQ joined groups");
        builder.AppendLine(snapshot.RefreshedAt == DateTimeOffset.MinValue
            ? "- cache: empty"
            : $"- refreshed: {snapshot.RefreshedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"- count: {snapshot.Groups.Count}");

        foreach (OneBotGroupInfo group in snapshot.Groups.Take(Math.Max(0, maxGroups)))
            builder.AppendLine($"- {group.GroupId} {FormatGroupName(group)} members={group.MemberCount}/{group.MaxMemberCount}");

        return builder.ToString().TrimEnd();
    }

    static string FormatGroupName(OneBotGroupInfo group)
    {
        return string.IsNullOrWhiteSpace(group.GroupName)
            ? "(unnamed)"
            : group.GroupName.Trim();
    }

    static AgentQChatJoinedGroupSourceSnapshot ToAgentJoinedGroupSourceSnapshot(QChatGroupListCacheSnapshot snapshot)
    {
        return new AgentQChatJoinedGroupSourceSnapshot(
            snapshot.RefreshedAt,
            snapshot.Groups
                .Select(group => new AgentQChatJoinedGroupSourceItem(
                    group.GroupId,
                    FormatGroupName(group),
                    group.MemberCount,
                    group.MaxMemberCount))
                .ToArray());
    }

    static string FormatCacheCounts(int groups, int members, int joinedGroups)
    {
        return $"{joinedGroups} joined {(joinedGroups == 1 ? "group" : "groups")}, {groups} member-list {(groups == 1 ? "group" : "groups")}, {members} {(members == 1 ? "member" : "members")} cached";
    }
}
