using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Platform;

namespace Alife.Function.Agent;

public sealed record AgentWorkspacePolicy(
    IReadOnlyList<string> AllowedRoots,
    int DefaultMaxReadChars = 6000,
    int DefaultMaxSearchMatches = 20);

public sealed record AgentWorkspaceReadResult(
    string FullPath,
    string RelativePath,
    string Content,
    bool Truncated);

public sealed record AgentWorkspaceLine(
    int LineNumber,
    string Text);

public sealed record AgentWorkspaceLineReadResult(
    string FullPath,
    string RelativePath,
    int StartLine,
    int EndLine,
    int TotalLines,
    IReadOnlyList<AgentWorkspaceLine> Lines,
    bool Truncated);

public sealed record AgentWorkspaceSearchMatch(
    string FullPath,
    string RelativePath,
    int LineNumber,
    string Preview);

public sealed record AgentWorkspaceEntry(
    string FullPath,
    string RelativePath,
    bool IsDirectory,
    long? SizeBytes);

public sealed record AgentWorkspaceWriteResult(
    string FullPath,
    string RelativePath,
    bool Created,
    int WrittenChars);

public sealed record AgentWorkspaceReplaceResult(
    string FullPath,
    string RelativePath,
    int ReplacedCount,
    int WrittenChars);

public sealed record AgentWorkspaceApplyProposalResult(
    bool Applied,
    AgentExecutionGatewayDecision GatewayDecision,
    AgentWorkspaceReplaceResult? Result,
    string Message);

public sealed record AgentWorkspacePatchProposal(
    string Id,
    string FullPath,
    string RelativePath,
    string OldText,
    string NewText,
    string Preview,
    DateTimeOffset CreatedAt);

[Module(
    "Agent Workspace",
    "Provides restricted workspace file read, search, generation, and exact text replacement tools.",
    defaultCategory: "Alife Official/Agent",
    LaunchOrder = -64)]
public class AgentWorkspaceService(
    AgentWorkspacePolicy? policy = null,
    XmlFunctionCaller? functionCaller = null,
    AgentAuditLogService? auditLog = null)
    : InteractiveModule<AgentWorkspaceService>
{
    readonly AgentWorkspacePolicy policy = NormalizePolicy(policy ?? CreateDefaultPolicy());
    readonly Dictionary<string, AgentWorkspacePatchProposal> patchProposals = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> AllowedRoots => policy.AllowedRoots;

    [XmlFunction(FunctionMode.OneShot, name: "workspace_read")]
    [Description("Read a text file inside the allowed agent workspace roots.")]
    public void WorkspaceRead(string path, int? maxChars = null)
    {
        AgentWorkspaceReadResult result = ReadText(path, maxChars ?? policy.DefaultMaxReadChars);
        Poke($"""
              Workspace read: {result.RelativePath}
              Truncated: {result.Truncated}
              ```
              {result.Content}
              ```
              """);
    }

    [XmlFunction(FunctionMode.OneShot, name: "workspace_read_lines")]
    [Description("Read a line-numbered range from a text file inside the allowed agent workspace roots.")]
    public void WorkspaceReadLines(string path, int startLine = 1, int lineCount = 80)
    {
        AgentWorkspaceLineReadResult result = ReadLines(path, startLine, lineCount);
        StringBuilder builder = new();
        builder.AppendLine($"Workspace read lines: {result.RelativePath}");
        builder.AppendLine($"Lines: {result.StartLine}-{result.EndLine} / {result.TotalLines}");
        builder.AppendLine($"Truncated: {result.Truncated}");
        builder.AppendLine("```");
        foreach (AgentWorkspaceLine line in result.Lines)
            builder.AppendLine($"{line.LineNumber}: {line.Text}");
        builder.AppendLine("```");
        Poke(builder.ToString().TrimEnd());
    }

    [XmlFunction(FunctionMode.OneShot, name: "workspace_search")]
    [Description("Search text files inside the allowed agent workspace roots.")]
    public void WorkspaceSearch(string query, string path = ".", int? maxMatches = null)
    {
        IReadOnlyList<AgentWorkspaceSearchMatch> matches = SearchText(query, path, maxMatches ?? policy.DefaultMaxSearchMatches);
        if (matches.Count == 0)
        {
            Poke("Workspace search found no matches.");
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine("Workspace search matches:");
        foreach (AgentWorkspaceSearchMatch match in matches)
            builder.AppendLine($"- {match.RelativePath}:{match.LineNumber}: {match.Preview}");
        Poke(builder.ToString().TrimEnd());
    }

    [XmlFunction(FunctionMode.OneShot, name: "workspace_list")]
    [Description("List direct child files and directories inside the allowed agent workspace roots.")]
    public void WorkspaceList(string path = ".", int maxEntries = 80)
    {
        IReadOnlyList<AgentWorkspaceEntry> entries = ListEntries(path, maxEntries);
        if (entries.Count == 0)
        {
            Poke("Workspace directory is empty.");
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine("Workspace entries:");
        foreach (AgentWorkspaceEntry entry in entries)
        {
            string kind = entry.IsDirectory ? "dir" : "file";
            string size = entry.SizeBytes == null ? "" : $" {entry.SizeBytes} bytes";
            builder.AppendLine($"- [{kind}] {entry.RelativePath}{size}");
        }
        Poke(builder.ToString().TrimEnd());
    }

    [XmlFunction(FunctionMode.Content, name: "workspace_write", riskLevel: XmlFunctionRiskLevel.High, budgetCost: 8)]
    [Description("Create or overwrite a text file inside allowed workspace roots. Existing files require overwrite=true.")]
    public void WorkspaceWrite(
        XmlExecutorContext context,
        [XmlContent] string content,
        string path,
        bool overwrite = false)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        AgentWorkspaceWriteResult result = WriteText(path, context.FullContent.Trim(), overwrite);
        Poke($"Workspace write: {result.RelativePath}; created: {result.Created}; chars: {result.WrittenChars}");
    }

    [XmlFunction(FunctionMode.OneShot, name: "workspace_replace", riskLevel: XmlFunctionRiskLevel.High, budgetCost: 6)]
    [Description("Replace exactly one text occurrence inside an allowed workspace file.")]
    public void WorkspaceReplace(string path, string oldText, string newText)
    {
        AgentWorkspaceReplaceResult result = ReplaceText(path, oldText, newText);
        Poke($"Workspace replace: {result.RelativePath}; replacements: 1; chars: {result.WrittenChars}");
    }

    [XmlFunction(FunctionMode.OneShot, name: "workspace_propose_replace", budgetCost: 3)]
    [Description("Preview an exact text replacement inside an allowed workspace file without changing the file.")]
    public void WorkspaceProposeReplace(string path, string oldText, string newText)
    {
        AgentWorkspacePatchProposal proposal = ProposeReplace(path, oldText, newText);
        Poke($"""
              Workspace replace proposal: {proposal.Id}
              Path: {proposal.RelativePath}
              Preview:
              {proposal.Preview}
              """);
    }

    [XmlFunction(FunctionMode.OneShot, name: "workspace_apply_proposal", riskLevel: XmlFunctionRiskLevel.High, budgetCost: 6)]
    [Description("Apply a previously previewed workspace replacement proposal.")]
    public void WorkspaceApplyProposal(string id)
    {
        AgentWorkspaceReplaceResult result = ApplyProposedReplace(id);
        Poke($"Workspace proposal applied: {id}; path: {result.RelativePath}; replacements: {result.ReplacedCount}; chars: {result.WrittenChars}");
    }

    public AgentWorkspaceReadResult ReadText(string path, int? maxChars = null)
    {
        WorkspacePath workspacePath = ResolvePath(path);
        if (File.Exists(workspacePath.FullPath) == false)
            throw new FileNotFoundException("Workspace file does not exist.", workspacePath.RelativePath);

        int limit = Math.Max(1, maxChars ?? policy.DefaultMaxReadChars);
        string content = File.ReadAllText(workspacePath.FullPath);
        bool truncated = content.Length > limit;
        if (truncated)
            content = content[..limit];

        return new AgentWorkspaceReadResult(
            workspacePath.FullPath,
            workspacePath.RelativePath,
            content,
            truncated);
    }

    public AgentWorkspaceLineReadResult ReadLines(string path, int startLine = 1, int lineCount = 80)
    {
        WorkspacePath workspacePath = ResolvePath(path);
        if (File.Exists(workspacePath.FullPath) == false)
            throw new FileNotFoundException("Workspace file does not exist.", workspacePath.RelativePath);

        int firstLine = Math.Max(1, startLine);
        int limit = Math.Max(1, lineCount);
        string[] allLines = File.ReadAllLines(workspacePath.FullPath);
        AgentWorkspaceLine[] lines = allLines
            .Skip(firstLine - 1)
            .Take(limit)
            .Select((line, index) => new AgentWorkspaceLine(firstLine + index, line))
            .ToArray();
        int endLine = lines.Length == 0 ? firstLine - 1 : lines[^1].LineNumber;
        bool truncated = endLine < allLines.Length;

        return new AgentWorkspaceLineReadResult(
            workspacePath.FullPath,
            workspacePath.RelativePath,
            firstLine,
            endLine,
            allLines.Length,
            lines,
            truncated);
    }

    public IReadOnlyList<AgentWorkspaceSearchMatch> SearchText(string query, string path = ".", int maxMatches = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Search query cannot be empty.", nameof(query));

        WorkspacePath workspacePath = ResolvePath(path);
        string start = Directory.Exists(workspacePath.FullPath)
            ? workspacePath.FullPath
            : Path.GetDirectoryName(workspacePath.FullPath) ?? workspacePath.Root;

        if (Directory.Exists(start) == false)
            return [];

        List<AgentWorkspaceSearchMatch> matches = new();
        foreach (string file in Directory.EnumerateFiles(start, "*", SearchOption.AllDirectories))
        {
            if (IsUnderAnyAllowedRoot(file) == false)
                continue;
            if (IsLikelyTextFile(file) == false)
                continue;

            int lineNumber = 0;
            foreach (string line in File.ReadLines(file))
            {
                lineNumber++;
                if (line.Contains(query, StringComparison.OrdinalIgnoreCase) == false)
                    continue;

                matches.Add(new AgentWorkspaceSearchMatch(
                    file,
                    GetRelativePath(file),
                    lineNumber,
                    line.Trim()));
                if (matches.Count >= Math.Max(1, maxMatches))
                    return matches;
            }
        }

        return matches;
    }

    public IReadOnlyList<AgentWorkspaceEntry> ListEntries(string path = ".", int maxEntries = 80)
    {
        WorkspacePath workspacePath = ResolvePath(path);
        if (Directory.Exists(workspacePath.FullPath) == false)
            throw new DirectoryNotFoundException($"Workspace directory does not exist: {workspacePath.RelativePath}");

        int limit = Math.Max(1, maxEntries);
        IEnumerable<AgentWorkspaceEntry> directories = Directory.EnumerateDirectories(workspacePath.FullPath)
            .Where(IsUnderAnyAllowedRoot)
            .Select(directory => new AgentWorkspaceEntry(
                directory,
                GetRelativePath(directory),
                IsDirectory: true,
                SizeBytes: null))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);

        IEnumerable<AgentWorkspaceEntry> files = Directory.EnumerateFiles(workspacePath.FullPath)
            .Where(IsUnderAnyAllowedRoot)
            .Select(file => new AgentWorkspaceEntry(
                file,
                GetRelativePath(file),
                IsDirectory: false,
                SizeBytes: new FileInfo(file).Length))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);

        return directories.Concat(files).Take(limit).ToArray();
    }

    public AgentWorkspaceWriteResult WriteText(string path, string content, bool overwrite)
    {
        WorkspacePath? workspacePath = null;
        try
        {
            workspacePath = ResolvePath(path);
            bool exists = File.Exists(workspacePath.FullPath);
            if (exists && overwrite == false)
                throw new InvalidOperationException("Workspace file already exists; pass overwrite=true to replace it.");

            Directory.CreateDirectory(Path.GetDirectoryName(workspacePath.FullPath)!);
            File.WriteAllText(workspacePath.FullPath, content);
            AgentWorkspaceWriteResult result = new(
                workspacePath.FullPath,
                workspacePath.RelativePath,
                Created: exists == false,
                WrittenChars: content.Length);

            RecordWorkspaceAudit(
                "workspace.write",
                $"path={result.RelativePath}; created={result.Created}; chars={result.WrittenChars}",
                succeeded: true);
            return result;
        }
        catch (Exception ex)
        {
            RecordWorkspaceAudit(
                "workspace.write",
                $"path={workspacePath?.RelativePath ?? NormalizeAuditPath(path)}",
                succeeded: false,
                ex);
            throw;
        }
    }

    public AgentWorkspaceReplaceResult ReplaceText(string path, string oldText, string newText)
    {
        WorkspacePath? workspacePath = null;
        try
        {
            if (string.IsNullOrEmpty(oldText))
                throw new ArgumentException("Old text cannot be empty.", nameof(oldText));

            workspacePath = ResolvePath(path);
            if (File.Exists(workspacePath.FullPath) == false)
                throw new FileNotFoundException("Workspace file does not exist.", workspacePath.RelativePath);

            string content = File.ReadAllText(workspacePath.FullPath);
            int first = content.IndexOf(oldText, StringComparison.Ordinal);
            if (first < 0)
                throw new InvalidOperationException("Old text was not found in workspace file.");
            int second = content.IndexOf(oldText, first + oldText.Length, StringComparison.Ordinal);
            if (second >= 0)
                throw new InvalidOperationException("Old text appears more than once; use a more specific match.");

            string updated = content.Remove(first, oldText.Length).Insert(first, newText);
            File.WriteAllText(workspacePath.FullPath, updated);
            AgentWorkspaceReplaceResult result = new(
                workspacePath.FullPath,
                workspacePath.RelativePath,
                ReplacedCount: 1,
                WrittenChars: updated.Length);

            RecordWorkspaceAudit(
                "workspace.replace",
                $"path={result.RelativePath}; replacements={result.ReplacedCount}; chars={result.WrittenChars}",
                succeeded: true);
            return result;
        }
        catch (Exception ex)
        {
            RecordWorkspaceAudit(
                "workspace.replace",
                $"path={workspacePath?.RelativePath ?? NormalizeAuditPath(path)}",
                succeeded: false,
                ex);
            throw;
        }
    }

    public AgentWorkspacePatchProposal ProposeReplace(string path, string oldText, string newText)
    {
        if (string.IsNullOrEmpty(oldText))
            throw new ArgumentException("Old text cannot be empty.", nameof(oldText));

        WorkspacePath workspacePath = ResolvePath(path);
        if (File.Exists(workspacePath.FullPath) == false)
            throw new FileNotFoundException("Workspace file does not exist.", workspacePath.RelativePath);

        string content = File.ReadAllText(workspacePath.FullPath);
        ValidateSingleOccurrence(content, oldText);

        AgentWorkspacePatchProposal proposal = new(
            Guid.NewGuid().ToString("N"),
            workspacePath.FullPath,
            workspacePath.RelativePath,
            oldText,
            newText,
            BuildReplacePreview(workspacePath.RelativePath, oldText, newText),
            DateTimeOffset.Now);

        lock (patchProposals)
        {
            patchProposals[proposal.Id] = proposal;
        }

        return proposal;
    }

    public IReadOnlyList<AgentWorkspacePatchProposal> GetPendingProposals()
    {
        lock (patchProposals)
        {
            return patchProposals.Values
                .OrderByDescending(proposal => proposal.CreatedAt)
                .ToArray();
        }
    }

    public AgentWorkspaceReplaceResult ApplyProposedReplace(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Proposal id cannot be empty.", nameof(id));

        AgentWorkspacePatchProposal proposal;
        lock (patchProposals)
        {
            proposal = patchProposals.GetValueOrDefault(id)
                       ?? throw new KeyNotFoundException($"Workspace proposal was not found: {id}");
        }

        AgentWorkspaceReplaceResult result = ReplaceText(proposal.RelativePath, proposal.OldText, proposal.NewText);
        lock (patchProposals)
        {
            patchProposals.Remove(id);
        }

        return result;
    }

    public AgentWorkspaceApplyProposalResult ApplyProposedReplace(
        string id,
        AgentPermissionRequest request,
        AgentPermissionConfig config)
    {
        AgentExecutionGatewayDecision decision = new AgentActionAuthorizationService().EvaluateExecution(request with
        {
            RiskLevel = AgentRiskLevel.High,
            Action = string.IsNullOrWhiteSpace(request.Action) ? "workspace.apply" : request.Action.Trim()
        }, config);

        if (decision.AllowedNow == false)
        {
            string prefix = decision.Status == AgentExecutionDecisionStatus.OwnerConfirmationRequired
                ? "Owner confirmation required"
                : "Blocked";
            return new AgentWorkspaceApplyProposalResult(
                Applied: false,
                decision,
                Result: null,
                Message: $"{prefix}: {decision.Reason}");
        }

        AgentWorkspaceReplaceResult result = ApplyProposedReplace(id);
        return new AgentWorkspaceApplyProposalResult(
            Applied: true,
            decision,
            result,
            $"Applied workspace proposal {id}.");
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionCaller?.RegisterHandler(this, nameof(WorkspaceWrite));
    }

    WorkspacePath ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Workspace path cannot be empty.", nameof(path));

        string candidate = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(policy.AllowedRoots[0], path));

        foreach (string root in policy.AllowedRoots)
        {
            if (IsUnderRoot(candidate, root))
                return new WorkspacePath(candidate, root, NormalizeRelativePath(Path.GetRelativePath(root, candidate)));
        }

        throw new UnauthorizedAccessException($"Workspace path is outside allowed roots: {path}");
    }

    bool IsUnderAnyAllowedRoot(string path) => policy.AllowedRoots.Any(root => IsUnderRoot(path, root));

    string GetRelativePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = policy.AllowedRoots.First(allowedRoot => IsUnderRoot(fullPath, allowedRoot));
        return NormalizeRelativePath(Path.GetRelativePath(root, fullPath));
    }

    static string NormalizeRelativePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

    static string NormalizeAuditPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "(empty)";

        return path.Trim()
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    void RecordWorkspaceAudit(string action, string detail, bool succeeded, Exception? error = null)
    {
        auditLog?.Record(
            action,
            "agent",
            detail,
            AgentAuditRiskLevel.High,
            succeeded,
            error?.Message);
    }

    static void ValidateSingleOccurrence(string content, string oldText)
    {
        int first = content.IndexOf(oldText, StringComparison.Ordinal);
        if (first < 0)
            throw new InvalidOperationException("Old text was not found in workspace file.");
        int second = content.IndexOf(oldText, first + oldText.Length, StringComparison.Ordinal);
        if (second >= 0)
            throw new InvalidOperationException("Old text appears more than once; use a more specific match.");
    }

    static string BuildReplacePreview(string relativePath, string oldText, string newText)
    {
        StringBuilder builder = new();
        builder.AppendLine($"--- {relativePath}");
        builder.AppendLine($"+++ {relativePath}");
        foreach (string line in NormalizePreviewLines(oldText))
            builder.AppendLine($"- {line}");
        foreach (string line in NormalizePreviewLines(newText))
            builder.AppendLine($"+ {line}");
        return builder.ToString().TrimEnd();
    }

    static string[] NormalizePreviewLines(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    static bool IsUnderRoot(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(root);
        if (fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        string normalizedRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsLikelyTextFile(string path)
    {
        string extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
            return true;

        return TextExtensions.Contains(extension);
    }

    static AgentWorkspacePolicy NormalizePolicy(AgentWorkspacePolicy rawPolicy)
    {
        string[] roots = rawPolicy.AllowedRoots
            .Where(root => string.IsNullOrWhiteSpace(root) == false)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (roots.Length == 0)
            throw new ArgumentException("At least one workspace root is required.", nameof(rawPolicy));

        foreach (string root in roots)
            Directory.CreateDirectory(root);

        return rawPolicy with { AllowedRoots = roots };
    }

    static AgentWorkspacePolicy CreateDefaultPolicy()
    {
        string root = Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace");
        List<string> roots = [Environment.CurrentDirectory, root, AlifePath.TempFolderPath];
        string? projectRoot = FindProjectRoot(Environment.CurrentDirectory);
        if (projectRoot != null)
            roots.Insert(0, projectRoot);

        return new AgentWorkspacePolicy(roots);
    }

    static string? FindProjectRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(startDirectory));
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".razor", ".csproj", ".sln", ".slnx", ".json", ".jsonl", ".md", ".txt", ".xml", ".yml", ".yaml",
        ".ps1", ".js", ".mjs", ".ts", ".tsx", ".css", ".html", ".py"
    };

    sealed record WorkspacePath(string FullPath, string Root, string RelativePath);
}
