using System.IO;

namespace Alife.Function.DesktopControl;

public sealed record DesktopFileAccessDecision(
    bool Allowed,
    string Reason,
    string NormalizedPath);

public sealed class DesktopFileAccessPolicy
{
    readonly IReadOnlyList<DesktopFileAccessPathRule> readBlacklistRules;
    readonly IReadOnlyList<DesktopFileAccessPathRule> writeDenyRules;

    public DesktopFileAccessPolicy(
        IEnumerable<string> readBlacklistPaths,
        IEnumerable<string> writeDenyPaths,
        bool allowFileMutationByDefault = false)
    {
        ArgumentNullException.ThrowIfNull(readBlacklistPaths);
        ArgumentNullException.ThrowIfNull(writeDenyPaths);

        readBlacklistRules = [.. readBlacklistPaths.SelectMany(CreateRule)];
        writeDenyRules = [.. writeDenyPaths.SelectMany(CreateRule)];
        AllowFileMutationByDefault = allowFileMutationByDefault;
    }

    public bool AllowFileMutationByDefault { get; }
    public int ReadBlacklistEntryCount => readBlacklistRules.Count;
    public int WriteDenyEntryCount => writeDenyRules.Count;

    public static DesktopFileAccessPolicy CreateDefault()
    {
        return new DesktopFileAccessPolicy(
            CreateDefaultReadBlacklistPaths(),
            CreateDefaultWriteDenyPaths(),
            allowFileMutationByDefault: false);
    }

    public DesktopFileAccessDecision CanRead(string? path)
    {
        if (TryNormalize(path, out string normalizedPath) == false)
            return new DesktopFileAccessDecision(false, "invalid_path", string.Empty);

        return MatchesAny(normalizedPath, readBlacklistRules)
            ? new DesktopFileAccessDecision(false, "read_blacklisted_path", normalizedPath)
            : new DesktopFileAccessDecision(true, "allowed", normalizedPath);
    }

    public DesktopFileAccessDecision CanWrite(string? path)
    {
        if (TryNormalize(path, out string normalizedPath) == false)
            return new DesktopFileAccessDecision(false, "invalid_path", string.Empty);

        if (MatchesAny(normalizedPath, writeDenyRules))
            return new DesktopFileAccessDecision(false, "write_denied_path", normalizedPath);

        return AllowFileMutationByDefault
            ? new DesktopFileAccessDecision(true, "allowed", normalizedPath)
            : new DesktopFileAccessDecision(false, "file_mutation_disabled", normalizedPath);
    }

    public DesktopFileAccessDecision CanModify(string? path) => CanWrite(path);

    public string FormatForOwner()
    {
        return string.Join(Environment.NewLine,
            "file_policy=enabled",
            $"read_blacklist_entries={ReadBlacklistEntryCount}",
            $"write_deny_entries={WriteDenyEntryCount}",
            $"default_file_mutation={(AllowFileMutationByDefault ? "allowed" : "denied")}");
    }

    static IEnumerable<string> CreateDefaultReadBlacklistPaths()
    {
        List<string> paths = [];
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        AddChildren(paths, userProfile,
            ".ssh",
            ".gnupg",
            ".aws",
            ".azure",
            ".docker",
            ".kube",
            ".nuget\\NuGet.Config",
            ".git-credentials",
            ".npmrc",
            ".pypirc",
            ".codex");
        AddChildren(paths, appData,
            "Microsoft\\Credentials",
            "Code\\User\\globalStorage",
            "Code\\User\\settings.json");
        AddChildren(paths, localAppData, "Microsoft\\Credentials");
        paths.AddRange([
            @"D:\Alife\Storage",
            @"D:\Alife\Runtime",
            @"D:\Alife\Models"
        ]);
        return paths;
    }

    static IEnumerable<string> CreateDefaultWriteDenyPaths()
    {
        List<string> paths = [];
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddIfNotWhiteSpace(paths, userProfile);
        paths.AddRange([
            @"C:\",
            @"C:\Windows",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\Users",
            @"D:\Alife\.git",
            @"D:\Alife\Storage",
            @"D:\Alife\Runtime",
            @"D:\Alife\Models",
            @"D:\Alife\Outputs",
            @"D:\NapCat",
            @"D:\FOXD\.git"
        ]);
        AddChildren(paths, userProfile,
            "Desktop",
            "Documents",
            "Downloads");
        return paths;
    }

    static void AddChildren(List<string> paths, string root, params string[] children)
    {
        if (string.IsNullOrWhiteSpace(root))
            return;

        foreach (string child in children)
            paths.Add(Path.Combine(root, child));
    }

    static void AddIfNotWhiteSpace(List<string> paths, string path)
    {
        if (string.IsNullOrWhiteSpace(path) == false)
            paths.Add(path);
    }

    static bool MatchesAny(string normalizedPath, IReadOnlyList<DesktopFileAccessPathRule> rules)
    {
        return rules.Any(rule => rule.Matches(normalizedPath));
    }

    static IEnumerable<DesktopFileAccessPathRule> CreateRule(string path)
    {
        if (TryNormalize(path, out string normalizedPath) == false)
            yield break;

        yield return new DesktopFileAccessPathRule(normalizedPath);
    }

    static bool TryNormalize(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            normalizedPath = TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    static string TrimEndingDirectorySeparator(string path)
    {
        string root = Path.GetPathRoot(path) ?? string.Empty;
        while (path.Length > root.Length &&
               (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)))
        {
            path = path[..^1];
        }

        return path;
    }

    sealed class DesktopFileAccessPathRule(string normalizedPath)
    {
        readonly string normalizedPath = normalizedPath;
        readonly string normalizedPathWithSeparator = normalizedPath.EndsWith(Path.DirectorySeparatorChar) ||
                                                      normalizedPath.EndsWith(Path.AltDirectorySeparatorChar)
            ? normalizedPath
            : normalizedPath + Path.DirectorySeparatorChar;

        public bool Matches(string candidate)
        {
            return string.Equals(candidate, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                   candidate.StartsWith(normalizedPathWithSeparator, StringComparison.OrdinalIgnoreCase);
        }
    }
}
