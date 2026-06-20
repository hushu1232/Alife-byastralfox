using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Alife.Platform;

namespace Alife.Function.QChat;

public sealed record QChatUserProfile(
    long UserId,
    string PreferredNickname = "",
    IReadOnlyList<string>? CuteNicknames = null,
    string FormalName = "",
    string RelationshipLabel = "",
    string AddressStyle = "",
    string Source = "",
    float Confidence = 0f,
    long? LastSeenGroupId = null,
    DateTimeOffset? LastSeenAt = null,
    string Notes = "");

public sealed class QChatUserProfileService
{
    readonly object syncRoot = new();
    readonly Dictionary<long, QChatUserProfile> profiles = new();
    readonly string filePath;
    DateTime lastLoadedWriteTimeUtc = DateTime.MinValue;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public QChatUserProfileService(string? rootPath = null)
    {
        string resolvedRoot = string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace")
            : rootPath;
        filePath = Path.Combine(resolvedRoot, "qchat-user-profiles.json");
        Load();
    }

    public void SetProfile(QChatUserProfile profile)
    {
        if (profile.UserId <= 0)
            throw new ArgumentOutOfRangeException(nameof(profile), "QQ user id must be positive.");

        QChatUserProfile normalized = Normalize(profile);
        lock (syncRoot)
        {
            profiles[normalized.UserId] = normalized;
            SaveNoLock();
        }
    }

    public bool TryGetProfile(long userId, out QChatUserProfile profile)
    {
        lock (syncRoot)
        {
            ReloadIfChangedNoLock();
            return profiles.TryGetValue(userId, out profile!);
        }
    }

    public string ResolvePreferredAddress(long userId, string? displayName = null)
    {
        lock (syncRoot)
        {
            ReloadIfChangedNoLock();
            if (profiles.TryGetValue(userId, out QChatUserProfile? profile))
            {
                string fromProfile = FirstUsable(
                    profile.PreferredNickname,
                    profile.CuteNicknames?.FirstOrDefault(),
                    profile.FormalName);
                if (string.IsNullOrWhiteSpace(fromProfile) == false)
                    return fromProfile;
            }
        }

        string fallback = CleanAddress(displayName);
        return string.IsNullOrWhiteSpace(fallback) ? userId.ToString() : fallback;
    }

    void Load()
    {
        lock (syncRoot)
        {
            LoadNoLock();
        }
    }

    void SaveNoLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        QChatUserProfile[] snapshot = profiles.Values
            .OrderBy(profile => profile.UserId)
            .ToArray();
        File.WriteAllText(filePath, JsonSerializer.Serialize(snapshot, JsonOptions));
        lastLoadedWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
    }

    void ReloadIfChangedNoLock()
    {
        if (File.Exists(filePath) == false)
            return;

        DateTime currentWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
        if (currentWriteTimeUtc == lastLoadedWriteTimeUtc)
            return;

        LoadNoLock();
    }

    void LoadNoLock()
    {
        profiles.Clear();
        if (File.Exists(filePath) == false)
        {
            lastLoadedWriteTimeUtc = DateTime.MinValue;
            return;
        }

        string json = File.ReadAllText(filePath);
        QChatUserProfile[]? loaded = JsonSerializer.Deserialize<QChatUserProfile[]>(json, JsonOptions);
        if (loaded != null)
        {
            foreach (QChatUserProfile profile in loaded.Where(item => item.UserId > 0))
                profiles[profile.UserId] = Normalize(profile);
        }

        lastLoadedWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
    }

    static QChatUserProfile Normalize(QChatUserProfile profile)
    {
        string[] cuteNicknames = (profile.CuteNicknames ?? [])
            .Select(CleanAddress)
            .Where(value => string.IsNullOrWhiteSpace(value) == false)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return profile with
        {
            PreferredNickname = CleanAddress(profile.PreferredNickname),
            CuteNicknames = cuteNicknames,
            FormalName = CleanAddress(profile.FormalName),
            RelationshipLabel = profile.RelationshipLabel?.Trim() ?? "",
            AddressStyle = profile.AddressStyle?.Trim() ?? "",
            Source = profile.Source?.Trim() ?? "",
            Notes = profile.Notes?.Trim() ?? ""
        };
    }

    static string FirstUsable(params string?[] values)
    {
        foreach (string? value in values)
        {
            string cleaned = CleanAddress(value);
            if (string.IsNullOrWhiteSpace(cleaned) == false)
                return cleaned;
        }

        return "";
    }

    static string CleanAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        string trimmed = value.Trim();
        return trimmed.Length <= 24 ? trimmed : trimmed[..24];
    }
}
