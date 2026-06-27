using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Alife.Framework;

namespace Alife.Function.Agent;

public sealed class AgentExternalRagStore
{
    const int MaxChunkChars = 1200;
    static readonly Regex TokenRegex = new("[\\p{L}\\p{N}]+", RegexOptions.Compiled);
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    static readonly ConcurrentDictionary<string, object> StorageLocks = new(StringComparer.OrdinalIgnoreCase);

    readonly object syncRoot;
    readonly string sourcesPath;
    readonly string chunksPath;

    public AgentExternalRagStore(string storageRootPath)
    {
        if (string.IsNullOrWhiteSpace(storageRootPath))
            throw new ArgumentException("storageRootPath is required.", nameof(storageRootPath));

        Directory.CreateDirectory(storageRootPath);
        string fullStorageRootPath = Path.GetFullPath(storageRootPath);
        syncRoot = StorageLocks.GetOrAdd(fullStorageRootPath, _ => new object());
        sourcesPath = Path.Combine(fullStorageRootPath, "external-rag-sources.jsonl");
        chunksPath = Path.Combine(fullStorageRootPath, "external-rag-chunks.jsonl");
    }

    public AgentExternalRagSource AddOrReplaceSource(
        string url,
        string title,
        string content,
        bool addedByOwner)
    {
        if (addedByOwner == false)
            throw new InvalidOperationException("external_rag_owner_required");

        string normalizedUrl = NormalizeRequired(url, nameof(url));
        string normalizedTitle = string.IsNullOrWhiteSpace(title) ? normalizedUrl : title.Trim();
        string normalizedContent = CleanContent(content);

        lock (syncRoot)
        {
            List<AgentExternalRagSource> sources = ReadJsonLines<AgentExternalRagSource>(sourcesPath);
            List<AgentExternalRagChunk> chunks = ReadJsonLines<AgentExternalRagChunk>(chunksPath);
            HashSet<string> replacedSourceIds = sources
                .Where(source => string.Equals(source.Url, normalizedUrl, StringComparison.OrdinalIgnoreCase))
                .Select(source => source.Id)
                .ToHashSet(StringComparer.Ordinal);

            sources.RemoveAll(source => replacedSourceIds.Contains(source.Id));
            chunks.RemoveAll(chunk => replacedSourceIds.Contains(chunk.SourceId));

            AgentExternalRagSource newSource = new(
                Guid.NewGuid().ToString("N"),
                normalizedUrl,
                normalizedTitle,
                DateTimeOffset.UtcNow);

            sources.Add(newSource);
            chunks.AddRange(CreateChunks(newSource, normalizedContent));

            WriteJsonLines(sourcesPath, sources);
            WriteJsonLines(chunksPath, chunks);

            return newSource;
        }
    }

    public IReadOnlyList<AgentExternalRagSource> ListSources(int limit)
    {
        int take = Math.Clamp(limit, 1, 100);
        lock (syncRoot)
        {
            return ReadJsonLines<AgentExternalRagSource>(sourcesPath)
                .OrderByDescending(source => source.CreatedAtUtc)
                .ThenBy(source => source.Title, StringComparer.OrdinalIgnoreCase)
                .Take(take)
                .ToArray();
        }
    }

    public bool DeleteSource(string urlOrId, bool deletedByOwner)
    {
        if (deletedByOwner == false)
            throw new InvalidOperationException("external_rag_owner_required");

        string normalized = NormalizeRequired(urlOrId, nameof(urlOrId));
        lock (syncRoot)
        {
            List<AgentExternalRagSource> sources = ReadJsonLines<AgentExternalRagSource>(sourcesPath);
            List<AgentExternalRagChunk> chunks = ReadJsonLines<AgentExternalRagChunk>(chunksPath);
            HashSet<string> removedSourceIds = sources
                .Where(source =>
                    string.Equals(source.Id, normalized, StringComparison.Ordinal) ||
                    string.Equals(source.Url, normalized, StringComparison.OrdinalIgnoreCase))
                .Select(source => source.Id)
                .ToHashSet(StringComparer.Ordinal);
            if (removedSourceIds.Count == 0)
                return false;

            sources.RemoveAll(source => removedSourceIds.Contains(source.Id));
            chunks.RemoveAll(chunk => removedSourceIds.Contains(chunk.SourceId));
            WriteJsonLines(sourcesPath, sources);
            WriteJsonLines(chunksPath, chunks);
            return true;
        }
    }

    public IReadOnlyList<AgentExternalRagChunk> Query(string query, int maxChunks)
    {
        string normalized = (query ?? "").Trim();
        if (normalized.Length == 0)
            return [];

        string[] queryTokens = Tokenize(normalized).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (queryTokens.Length == 0)
            return [];

        int limit = Math.Max(1, maxChunks);
        lock (syncRoot)
        {
            HashSet<string> sourceIds = ReadJsonLines<AgentExternalRagSource>(sourcesPath)
                .Select(source => source.Id)
                .ToHashSet(StringComparer.Ordinal);

            return ReadJsonLines<AgentExternalRagChunk>(chunksPath)
                .Where(chunk => sourceIds.Contains(chunk.SourceId))
                .Select(chunk => new
                {
                    Chunk = chunk,
                    Score = Score(chunk, queryTokens)
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Chunk.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Chunk.Index)
                .ThenBy(item => item.Chunk.Url, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Chunk.SourceId, StringComparer.Ordinal)
                .ThenBy(item => item.Chunk.Id, StringComparer.Ordinal)
                .Take(limit)
                .Select(item => item.Chunk)
                .ToArray();
        }
    }

    public static string FormatQueryContext(IReadOnlyList<AgentExternalRagChunk> chunks)
    {
        StringBuilder builder = new();
        for (int i = 0; i < chunks.Count; i++)
        {
            AgentExternalRagChunk chunk = chunks[i];
            if (i > 0)
                builder.AppendLine();

            builder.AppendLine($"source={chunk.Title}");
            builder.AppendLine($"url={chunk.Url}");
            builder.AppendLine($"chunk={chunk.Index}");
            builder.AppendLine(chunk.Text);
        }

        return ExternalContextFormatter.WrapUntrusted(
            "external-rag",
            builder.ToString().TrimEnd());
    }

    static IEnumerable<AgentExternalRagChunk> CreateChunks(
        AgentExternalRagSource source,
        string content)
    {
        int index = 0;
        foreach (string paragraph in SplitParagraphs(content))
        {
            foreach (string text in SplitLongText(paragraph, MaxChunkChars))
            {
                string trimmed = text.Trim();
                if (trimmed.Length == 0)
                    continue;

                yield return new AgentExternalRagChunk(
                    Guid.NewGuid().ToString("N"),
                    source.Id,
                    source.Url,
                    source.Title,
                    trimmed,
                    index++);
            }
        }
    }

    static string CleanContent(string? content)
    {
        string text = content ?? "";
        text = Regex.Replace(text, @"<script\b[^>]*>.*?</script>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<style\b[^>]*>.*?</style>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<[^>]+>", " ");
        text = Regex.Replace(
            text,
            @"\b(cookie banner|subscribe now|navigation|footer|advertisement|privacy policy)\b",
            " ",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    static IEnumerable<string> SplitParagraphs(string content) => content
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    static IEnumerable<string> SplitLongText(string text, int maxChars)
    {
        for (int start = 0; start < text.Length; start += maxChars)
            yield return text.Substring(start, Math.Min(maxChars, text.Length - start));
    }

    static int Score(AgentExternalRagChunk chunk, string[] queryTokens)
    {
        string searchable = $"{chunk.Title} {chunk.Url} {chunk.Text}";
        string[] chunkTokens = Tokenize(searchable);
        if (chunkTokens.Length == 0)
            return 0;

        return queryTokens.Sum(queryToken =>
            chunkTokens.Count(chunkToken => string.Equals(chunkToken, queryToken, StringComparison.OrdinalIgnoreCase)));
    }

    static string[] Tokenize(string value) => TokenRegex
        .Matches(value)
        .Select(match => match.Value)
        .ToArray();

    static string NormalizeRequired(string value, string parameterName)
    {
        string normalized = (value ?? "").Trim();
        if (normalized.Length == 0)
            throw new ArgumentException($"{parameterName} is required.", parameterName);

        return normalized;
    }

    static List<T> ReadJsonLines<T>(string path)
    {
        if (File.Exists(path) == false)
            return [];

        List<T> records = [];
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                T? record = JsonSerializer.Deserialize<T>(line, JsonOptions);
                if (record != null)
                    records.Add(record);
            }
            catch (JsonException)
            {
            }
        }

        return records;
    }

    static void WriteJsonLines<T>(string path, IReadOnlyList<T> records)
    {
        string directory = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (StreamWriter writer = new(tempPath, append: false, Encoding.UTF8))
            {
                foreach (T record in records)
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
