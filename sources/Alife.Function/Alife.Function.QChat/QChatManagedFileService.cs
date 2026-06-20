using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Alife.Function.Agent;

namespace Alife.Function.QChat;

public enum QChatManagedFileStatus
{
    Pending,
    Downloaded,
    Deleted,
    Failed
}

public sealed record QChatManagedFileRegistration(
    OneBotMessageType MessageType,
    long SenderId,
    long GroupId,
    string FileId,
    string OriginalName,
    long? Size,
    string? Url);

public sealed record QChatManagedFileOperationResult(
    bool Success,
    string Message,
    QChatManagedFileRecord? Record = null,
    string? TextPreview = null);

public sealed class QChatManagedFileRecord
{
    public string Id { get; set; } = "";
    public string FileId { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string SafeName { get; set; } = "";
    public OneBotMessageType MessageType { get; set; }
    public long SenderId { get; set; }
    public long GroupId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public long? Size { get; set; }
    public string? Url { get; set; }
    public QChatManagedFileStatus Status { get; set; }
    public string? LocalPath { get; set; }
    public string? LastError { get; set; }
}

public sealed class QChatManagedFileService
{
    const long MaxDownloadBytes = 10 * 1024 * 1024;
    const int MaxPreviewChars = 20_000;
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    readonly string rootDirectory;
    readonly string rootDirectoryWithSeparator;
    readonly string registryPath;
    readonly Func<Uri, CancellationToken, Task<byte[]>> downloadBytesAsync;

    public QChatManagedFileService(
        string rootDirectory,
        Func<Uri, CancellationToken, Task<byte[]>>? downloadBytesAsync = null)
    {
        this.rootDirectory = TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        rootDirectoryWithSeparator = IncludeTrailingSeparator(this.rootDirectory);
        registryPath = Path.Combine(this.rootDirectory, "pending-index.json");
        this.downloadBytesAsync = downloadBytesAsync ?? DownloadBytesWithHttpClientAsync;
    }

    public async Task<QChatManagedFileRecord> RegisterAsync(
        QChatManagedFileRegistration registration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(registration.FileId))
            throw new InvalidOperationException("QQ file id is required.");

        Directory.CreateDirectory(rootDirectory);
        List<QChatManagedFileRecord> records = await LoadRecordsAsync(cancellationToken);
        string id = BuildId(registration);
        QChatManagedFileRecord? existing = records.FirstOrDefault(record => record.Id == id);
        if (existing != null)
            return existing;

        QChatManagedFileRecord record = new()
        {
            Id = id,
            FileId = registration.FileId,
            OriginalName = registration.OriginalName,
            SafeName = SanitizeFileName(registration.OriginalName),
            MessageType = registration.MessageType,
            SenderId = registration.SenderId,
            GroupId = registration.GroupId,
            ReceivedAt = DateTimeOffset.UtcNow,
            Size = registration.Size,
            Url = registration.Url,
            Status = QChatManagedFileStatus.Pending
        };

        records.Add(record);
        await SaveRecordsAsync(records, cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<QChatManagedFileRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await LoadRecordsAsync(cancellationToken);
    }

    public async Task<QChatManagedFileOperationResult> DownloadAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        List<QChatManagedFileRecord> records = await LoadRecordsAsync(cancellationToken);
        QChatManagedFileRecord? record = FindRecord(records, id);
        if (record == null)
            return new QChatManagedFileOperationResult(false, $"QQ file '{id}' was not found.");

        if (record.Status == QChatManagedFileStatus.Deleted)
            return new QChatManagedFileOperationResult(false, $"QQ file '{record.Id}' has already been deleted.", record);

        if (record.Status == QChatManagedFileStatus.Downloaded &&
            string.IsNullOrWhiteSpace(record.LocalPath) == false &&
            IsPathUnderRoot(record.LocalPath) &&
            File.Exists(record.LocalPath))
        {
            string? existingPreview = await TryExtractTextPreviewAsync(record.LocalPath, cancellationToken);
            return new QChatManagedFileOperationResult(true, "QQ file is already downloaded in the managed workspace.", record, existingPreview);
        }

        if (record.Size is > MaxDownloadBytes)
            return await MarkFailedAsync(records, record, $"QQ file is too large. Limit is {MaxDownloadBytes} bytes.", cancellationToken);

        if (Uri.TryCreate(record.Url, UriKind.Absolute, out Uri? uri) == false ||
            uri.Scheme is not ("http" or "https"))
        {
            return await MarkFailedAsync(records, record, "QQ file download URL is missing or unsupported.", cancellationToken);
        }

        try
        {
            byte[] bytes = await downloadBytesAsync(uri, cancellationToken);
            if (bytes.LongLength > MaxDownloadBytes)
                return await MarkFailedAsync(records, record, $"Downloaded QQ file is too large. Limit is {MaxDownloadBytes} bytes.", cancellationToken);

            string destination = BuildDownloadPath(record);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, bytes, cancellationToken);

            record.Status = QChatManagedFileStatus.Downloaded;
            record.LocalPath = destination;
            record.LastError = null;
            await SaveRecordsAsync(records, cancellationToken);

            string? preview = await TryExtractTextPreviewAsync(destination, cancellationToken);
            return new QChatManagedFileOperationResult(true, "QQ file downloaded into the managed workspace.", record, preview);
        }
        catch (Exception ex)
        {
            return await MarkFailedAsync(records, record, ex.Message, cancellationToken);
        }
    }

    public async Task<QChatManagedFileOperationResult> ReadAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        List<QChatManagedFileRecord> records = await LoadRecordsAsync(cancellationToken);
        QChatManagedFileRecord? record = FindRecord(records, id);
        if (record == null)
            return new QChatManagedFileOperationResult(false, $"QQ file '{id}' was not found.");
        if (record.Status != QChatManagedFileStatus.Downloaded || string.IsNullOrWhiteSpace(record.LocalPath))
            return new QChatManagedFileOperationResult(false, $"QQ file '{record.Id}' is not downloaded.", record);
        if (IsPathUnderRoot(record.LocalPath) == false || File.Exists(record.LocalPath) == false)
            return new QChatManagedFileOperationResult(false, $"QQ file '{record.Id}' local file is unavailable.", record);

        string? preview = await TryExtractTextPreviewAsync(record.LocalPath, cancellationToken);
        return new QChatManagedFileOperationResult(preview != null, preview == null ? "QQ file text preview is unsupported." : "QQ file text preview extracted.", record, preview);
    }

    public async Task<QChatManagedFileOperationResult> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        return await DeleteCoreAsync(id, cancellationToken);
    }

    public async Task<QChatManagedFileOperationResult> DeleteAsync(
        string id,
        long actorUserId,
        AgentRequestSource source,
        AgentPermissionGate permissionGate,
        CancellationToken cancellationToken = default)
    {
        List<QChatManagedFileRecord> records = await LoadRecordsAsync(cancellationToken);
        QChatManagedFileRecord? record = FindRecord(records, id);
        if (record == null)
            return new QChatManagedFileOperationResult(false, $"QQ file '{id}' was not found.");

        AgentPermissionGateDecision decision = permissionGate.Evaluate(new AgentPermissionRequest(
            ActorUserId: actorUserId,
            Source: source,
            IsMentioned: false,
            RiskLevel: AgentRiskLevel.Medium,
            HasExplicitConfirmation: false,
            Action: "delete-managed-qq-file"));
        if (decision.Kind != AgentPermissionDecisionKind.Allow)
            return new QChatManagedFileOperationResult(false, decision.Reason, record);

        return await DeleteCoreAsync(id, cancellationToken);
    }

    async Task<QChatManagedFileOperationResult> DeleteCoreAsync(
        string id,
        CancellationToken cancellationToken)
    {
        List<QChatManagedFileRecord> records = await LoadRecordsAsync(cancellationToken);
        QChatManagedFileRecord? record = FindRecord(records, id);
        if (record == null)
            return new QChatManagedFileOperationResult(false, $"QQ file '{id}' was not found.");

        try
        {
            if (string.IsNullOrWhiteSpace(record.LocalPath) == false &&
                IsPathUnderRoot(record.LocalPath) &&
                File.Exists(record.LocalPath))
            {
                File.Delete(record.LocalPath);
            }

            record.Status = QChatManagedFileStatus.Deleted;
            record.LastError = null;
            await SaveRecordsAsync(records, cancellationToken);
            return new QChatManagedFileOperationResult(true, "QQ file deleted from the managed workspace.", record);
        }
        catch (Exception ex)
        {
            return await MarkFailedAsync(records, record, ex.Message, cancellationToken);
        }
    }

    async Task<QChatManagedFileOperationResult> MarkFailedAsync(
        List<QChatManagedFileRecord> records,
        QChatManagedFileRecord record,
        string error,
        CancellationToken cancellationToken)
    {
        record.Status = QChatManagedFileStatus.Failed;
        record.LastError = error;
        await SaveRecordsAsync(records, cancellationToken);
        return new QChatManagedFileOperationResult(false, error, record);
    }

    async Task<List<QChatManagedFileRecord>> LoadRecordsAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(registryPath) == false)
            return [];

        await using FileStream stream = File.OpenRead(registryPath);
        List<QChatManagedFileRecord>? records = await JsonSerializer.DeserializeAsync<List<QChatManagedFileRecord>>(
            stream,
            JsonOptions,
            cancellationToken);
        return records ?? [];
    }

    async Task SaveRecordsAsync(List<QChatManagedFileRecord> records, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(rootDirectory);
        await using FileStream stream = File.Create(registryPath);
        await JsonSerializer.SerializeAsync(stream, records, JsonOptions, cancellationToken);
    }

    string BuildDownloadPath(QChatManagedFileRecord record)
    {
        string scope = record.MessageType == OneBotMessageType.Group
            ? $"group-{record.GroupId}"
            : $"private-{record.SenderId}";
        string extension = Path.GetExtension(record.SafeName).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension))
            extension = "file";

        string directory = Path.Combine(rootDirectory, "downloads", scope, extension);
        string destination = Path.Combine(directory, record.SafeName);
        if (File.Exists(destination) == false)
            return destination;

        string name = Path.GetFileNameWithoutExtension(record.SafeName);
        string fileExtension = Path.GetExtension(record.SafeName);
        string idPrefix = record.Id[..Math.Min(8, record.Id.Length)];
        int suffix = 0;
        while (true)
        {
            string suffixText = suffix == 0 ? idPrefix : $"{idPrefix}-{suffix}";
            string candidate = Path.Combine(directory, $"{name}-{suffixText}{fileExtension}");
            if (File.Exists(candidate) == false)
                return candidate;

            suffix++;
        }
    }

    bool IsPathUnderRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string fullPath = TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(fullPath, rootDirectory, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(rootDirectoryWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    static QChatManagedFileRecord? FindRecord(List<QChatManagedFileRecord> records, string id)
    {
        return records.FirstOrDefault(record => string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    static string BuildId(QChatManagedFileRegistration registration)
    {
        string source = $"{registration.MessageType}|{registration.SenderId}|{registration.GroupId}|{registration.FileId}|{registration.Url}|{registration.OriginalName}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash).ToLowerInvariant()[..12];
    }

    static string SanitizeFileName(string fileName)
    {
        string name = Path.GetFileName(fileName.Replace('\\', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
            name = "qq-file";

        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        name = Regex.Replace(name, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(name) ? "qq-file" : name;
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

    static async Task<byte[]> DownloadBytesWithHttpClientAsync(Uri uri, CancellationToken cancellationToken)
    {
        using HttpClient client = new();
        return await client.GetByteArrayAsync(uri, cancellationToken);
    }

    static async Task<string?> TryExtractTextPreviewAsync(string path, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        string? text = extension switch
        {
            ".docx" => ExtractDocxText(path),
            _ when IsPlainTextExtension(extension) => await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken),
            _ => null
        };

        if (string.IsNullOrEmpty(text))
            return text;

        return text.Length <= MaxPreviewChars ? text : text[..MaxPreviewChars];
    }

    static string? ExtractDocxText(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        ZipArchiveEntry? document = archive.GetEntry("word/document.xml");
        if (document == null)
            return null;

        using Stream stream = document.Open();
        XDocument xml = XDocument.Load(stream);
        string[] textRuns = xml.Descendants()
            .Where(element => element.Name.LocalName == "t")
            .Select(element => WebUtility.HtmlDecode(element.Value))
            .Where(value => string.IsNullOrEmpty(value) == false)
            .ToArray();
        return string.Join("", textRuns);
    }

    static bool IsPlainTextExtension(string extension)
    {
        return extension is ".txt" or ".md" or ".markdown" or ".log" or ".csv" or ".tsv"
            or ".json" or ".jsonl" or ".xml" or ".html" or ".htm"
            or ".ini" or ".conf" or ".cfg" or ".yaml" or ".yml" or ".toml"
            or ".cs" or ".py" or ".js" or ".ts" or ".tsx" or ".jsx"
            or ".java" or ".cpp" or ".c" or ".h" or ".hpp" or ".go" or ".rs"
            or ".php" or ".css" or ".scss" or ".sql" or ".ps1" or ".bat"
            or ".cmd" or ".sh";
    }
}
