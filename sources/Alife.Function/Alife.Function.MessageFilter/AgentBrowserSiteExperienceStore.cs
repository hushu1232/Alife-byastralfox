using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Alife.Function.Agent;

public enum AgentBrowserSiteStrategy
{
    Unknown,
    PublicFetch,
    BrowserSnapshot,
    DynamicBrowser,
    Blocked
}

public enum AgentBrowserSiteRiskLevel
{
    Low,
    Medium,
    High
}

public sealed record AgentBrowserSiteExperience(
    string Host,
    AgentBrowserSiteStrategy PreferredStrategy,
    bool NeedsBrowser,
    bool NeedsLogin,
    bool HasAntiBotSignals,
    bool LastSuccess,
    string LastReason,
    AgentBrowserSiteRiskLevel RiskLevel,
    DateTimeOffset UpdatedAt);

public sealed class AgentBrowserSiteExperienceStore
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    static readonly ConcurrentDictionary<string, object> StorageLocks = new(StringComparer.OrdinalIgnoreCase);

    readonly object syncRoot;
    readonly string path;

    public AgentBrowserSiteExperienceStore(string storageRootPath)
    {
        if (string.IsNullOrWhiteSpace(storageRootPath))
            throw new ArgumentException("storageRootPath is required.", nameof(storageRootPath));

        Directory.CreateDirectory(storageRootPath);
        string fullStorageRootPath = Path.GetFullPath(storageRootPath);
        syncRoot = StorageLocks.GetOrAdd(fullStorageRootPath, _ => new object());
        path = Path.Combine(fullStorageRootPath, "browser-site-experience.jsonl");
    }

    public bool RecordSnapshotResult(
        string url,
        bool success,
        string reason,
        DateTimeOffset? now = null)
    {
        if (TryNormalizeHttpHost(url, out string host) == false)
            return false;

        AgentBrowserSiteExperience experience = BuildSnapshotExperience(
            host,
            success,
            reason,
            now ?? DateTimeOffset.UtcNow);

        lock (syncRoot)
        {
            List<AgentBrowserSiteExperience> records = ReadJsonLines(path);
            records.RemoveAll(record => string.Equals(record.Host, host, StringComparison.OrdinalIgnoreCase));
            records.Add(experience);
            WriteJsonLines(path, records);
        }

        return true;
    }

    public AgentBrowserSiteExperience? Get(string host)
    {
        string normalized = NormalizeHost(host);
        if (normalized.Length == 0)
            return null;

        lock (syncRoot)
        {
            return ReadJsonLines(path)
                .LastOrDefault(record => string.Equals(record.Host, normalized, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<AgentBrowserSiteExperience> ListRecent(int limit)
    {
        int take = Math.Clamp(limit, 1, 100);
        lock (syncRoot)
        {
            return ReadJsonLines(path)
                .OrderByDescending(record => record.UpdatedAt)
                .ThenBy(record => record.Host, StringComparer.OrdinalIgnoreCase)
                .Take(take)
                .ToArray();
        }
    }

    public string FormatStatus(int limit = 8)
    {
        IReadOnlyList<AgentBrowserSiteExperience> records = ListRecent(limit);
        if (records.Count == 0)
            return "browser_site_experience=empty";

        StringBuilder builder = new();
        builder.AppendLine($"browser_site_experience recent={records.Count}");
        foreach (AgentBrowserSiteExperience record in records)
        {
            builder.AppendLine(
                $"host={record.Host} strategy={record.PreferredStrategy} success={FormatBool(record.LastSuccess)} risk={record.RiskLevel} needs_login={FormatBool(record.NeedsLogin)} anti_bot={FormatBool(record.HasAntiBotSignals)} reason={record.LastReason} updated={record.UpdatedAt:O}");
        }

        return builder.ToString().TrimEnd();
    }

    public string FormatDoctor(
        bool internetAccessEnabled,
        bool browserProviderConfigured,
        int limit = 8)
    {
        IReadOnlyList<AgentBrowserSiteExperience> records = ListRecent(limit);
        StringBuilder builder = new();
        builder.AppendLine(
            $"web_doctor browser_provider={FormatConfigured(browserProviderConfigured)} internet={FormatEnabled(internetAccessEnabled)} recent_sites={records.Count}");
        if (records.Count == 0)
        {
            builder.AppendLine("site_experience=empty");
            return builder.ToString().TrimEnd();
        }

        foreach (AgentBrowserSiteExperience record in records)
        {
            builder.AppendLine(
                $"host={record.Host} strategy={record.PreferredStrategy} success={FormatBool(record.LastSuccess)} risk={record.RiskLevel} needs_login={FormatBool(record.NeedsLogin)} anti_bot={FormatBool(record.HasAntiBotSignals)} reason={record.LastReason}");
        }

        return builder.ToString().TrimEnd();
    }

    static AgentBrowserSiteExperience BuildSnapshotExperience(
        string host,
        bool success,
        string reason,
        DateTimeOffset updatedAt)
    {
        string normalizedReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
        bool needsLogin = ContainsAny(normalizedReason, "login", "auth", "unauthorized", "401", "signin", "sign in");
        bool hasAntiBotSignals = ContainsAny(normalizedReason, "captcha", "cloudflare", "anti_bot", "anti-bot", "403", "forbidden");

        AgentBrowserSiteStrategy strategy;
        AgentBrowserSiteRiskLevel riskLevel;
        if (success)
        {
            strategy = AgentBrowserSiteStrategy.BrowserSnapshot;
            riskLevel = AgentBrowserSiteRiskLevel.Low;
        }
        else if (needsLogin)
        {
            strategy = AgentBrowserSiteStrategy.Blocked;
            riskLevel = AgentBrowserSiteRiskLevel.High;
        }
        else if (hasAntiBotSignals)
        {
            strategy = AgentBrowserSiteStrategy.DynamicBrowser;
            riskLevel = AgentBrowserSiteRiskLevel.Medium;
        }
        else
        {
            strategy = AgentBrowserSiteStrategy.BrowserSnapshot;
            riskLevel = AgentBrowserSiteRiskLevel.Medium;
        }

        return new AgentBrowserSiteExperience(
            host,
            strategy,
            NeedsBrowser: true,
            needsLogin,
            hasAntiBotSignals,
            success,
            normalizedReason,
            riskLevel,
            updatedAt);
    }

    public static bool TryNormalizeHttpHost(string value, out string host)
    {
        host = "";
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) == false)
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        string normalized = NormalizeHost(uri.DnsSafeHost);
        if (normalized.Length == 0 || IsUnsafeHost(normalized))
            return false;

        host = normalized;
        return true;
    }

    public static string NormalizeHost(string value)
    {
        string normalized = (value ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[4..];
        return normalized;
    }

    static bool IsUnsafeHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out IPAddress? address) && IsPrivateAddress(address);
    }

    static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 10
                   || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168)
                   || (bytes[0] == 169 && bytes[1] == 254);
        }

        if (bytes.Length == 16)
            return (bytes[0] & 0xfe) == 0xfc || bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80;

        return false;
    }

    static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    static string FormatBool(bool value) => value ? "true" : "false";

    static string FormatConfigured(bool value) => value ? "configured" : "missing";

    static string FormatEnabled(bool value) => value ? "enabled" : "disabled";

    static List<AgentBrowserSiteExperience> ReadJsonLines(string path)
    {
        if (File.Exists(path) == false)
            return [];

        List<AgentBrowserSiteExperience> records = [];
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                AgentBrowserSiteExperience? record = JsonSerializer.Deserialize<AgentBrowserSiteExperience>(line, JsonOptions);
                if (record != null)
                    records.Add(record);
            }
            catch (JsonException)
            {
            }
        }

        return records;
    }

    static void WriteJsonLines(string path, IReadOnlyList<AgentBrowserSiteExperience> records)
    {
        string directory = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (StreamWriter writer = new(tempPath, append: false, Encoding.UTF8))
            {
                foreach (AgentBrowserSiteExperience record in records)
                    writer.WriteLine(JsonSerializer.Serialize(record, JsonOptions));
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
