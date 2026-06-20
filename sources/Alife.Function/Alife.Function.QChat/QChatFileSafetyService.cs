using System;
using System.Collections.Generic;
using System.IO;

namespace Alife.Function.QChat;

public sealed class QChatFileSafetyService
{
    static readonly HashSet<string> SupportedTextLikeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx",
        ".txt",
        ".md",
        ".pdf",
        ".csv",
        ".json"
    };

    readonly string rootDirectory;
    readonly string rootDirectoryWithSeparator;

    public QChatFileSafetyService(string rootDirectory)
    {
        this.rootDirectory = TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        rootDirectoryWithSeparator = IncludeTrailingSeparator(this.rootDirectory);
    }

    public bool IsSupportedTextLikeFile(string fileName)
    {
        return SupportedTextLikeExtensions.Contains(Path.GetExtension(fileName));
    }

    public bool IsInsideRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            string fullPath = TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return string.Equals(fullPath, rootDirectory, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(rootDirectoryWithSeparator, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public string BuildDownloadFolder(QChatAgentRoute route, string category)
    {
        ArgumentNullException.ThrowIfNull(route);

        string agentId = SanitizePathSegment(route.AgentId, "agent", stripLeadingDots: false);
        string peer = route.ConversationKind == QChatConversationKind.Group
            ? $"group-{route.PeerId}"
            : $"private-{route.PeerId}";
        string cleanCategory = SanitizePathSegment(category, "file", stripLeadingDots: true);
        string folder = Path.GetFullPath(Path.Combine(rootDirectory, agentId, peer, cleanCategory));
        if (IsInsideRoot(folder) == false)
            throw new InvalidOperationException("QChat download folder escaped the managed root.");

        return folder;
    }

    static string SanitizePathSegment(string? value, string fallback, bool stripLeadingDots)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        string clean = value.Trim();
        if (Path.IsPathRooted(clean))
            return fallback;

        if (stripLeadingDots)
            clean = clean.TrimStart('.');

        clean = clean
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');

        foreach (char invalid in Path.GetInvalidFileNameChars())
            clean = clean.Replace(invalid, '_');

        clean = clean.Trim();
        if (clean == "." || clean == "..")
            return fallback;

        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
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

    static string IncludeTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
