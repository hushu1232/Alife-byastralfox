using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DuckDB.NET.Data;

namespace Alife.Function.Memory;

public record SearchResult(string Name, int Level, string Summary, string Content, DateTimeOffset StartTime, DateTimeOffset EndTime, float Score);

public enum MemoryStorageConsistencyIssueKind
{
    MissingArchiveFile,
    MissingIndexRecord,
    ContentMismatch,
}

public record MemoryStorageConsistencyIssue(
    MemoryStorageConsistencyIssueKind Kind,
    string Name,
    int Level,
    string Path);

public record MemoryStorageConsistencyReport(
    int MissingArchiveFiles,
    int MissingIndexRecords,
    int ContentMismatches,
    int RepairedArchiveFiles,
    int RepairedIndexRecords,
    int RepairedContentMismatches,
    IReadOnlyList<MemoryStorageConsistencyIssue> Issues)
{
    public static MemoryStorageConsistencyReport Empty { get; } = new(0, 0, 0, 0, 0, 0, []);
}

public record MemoryStorageSanitizationReport(
    int SanitizedArchiveRecords,
    int RemovedSegments,
    int BackupFilesCreated)
{
    public static MemoryStorageSanitizationReport Empty { get; } = new(0, 0, 0);
}

public enum MemorySearchMode
{
    Keyword,
    Vector,
    Hybrid,
}

/// <summary>
/// 向量记忆存储容器（带物理分离设计）。
/// 文本内容作为真实文件存储在硬盘树中，便于直接管理/浏览。
/// 文本向量、检索标引等元数据则存放到 DuckDB 中。
/// 利用 DuckDB 原生强大的单文件分析性能及 array_cosine_similarity()，无需插件即可执行数百万级的极速相似度搜索并与标量过滤联动。
/// </summary>
public class MemoryStorage : IAsyncDisposable
{
    public MemoryStorage(string rootPath, ITextVectorizer vectorizer)
    {
        this.rootPath = rootPath;
        this.vectorizer = vectorizer;
        if (!Directory.Exists(rootPath))
            Directory.CreateDirectory(rootPath);

        dbPath = Path.Combine(rootPath, "memory_index.duckdb");
        connection = new DuckDBConnection($"Data Source={dbPath}");
        connection.Open();
        InitializeDatabase();
        LastConsistencyReport = ScanConsistencyNoLock();

        void InitializeDatabase()
        {
            using DuckDBCommand command = connection.CreateCommand();

            // 1. 尝试以 Name 为唯一主键创建表
            // 2. 动态增加可能缺失的字段（用于旧库升级）
            command.CommandText = @"
            CREATE TABLE IF NOT EXISTS MemoryStorage (
                Name VARCHAR PRIMARY KEY, 
                Level INTEGER,
                Summary VARCHAR,
                Content VARCHAR,
                StartTime BIGINT,
                EndTime BIGINT,
                Vector FLOAT[512]
            );
            CREATE INDEX IF NOT EXISTS idx_level_time ON MemoryStorage(Level DESC, EndTime DESC);
        ";
            command.ExecuteNonQuery();
        }
    }

    public MemoryStorageConsistencyReport LastConsistencyReport { get; private set; } = MemoryStorageConsistencyReport.Empty;

    public async Task SaveAsync(string name, int level, string summary, string content, DateTimeOffset startTime, DateTimeOffset endTime)
    {
        //向量化概述，便于实现语义搜索
        float[] vector = await vectorizer.VectorizeAsync(summary);
        string vectorLiteral = ToVectorLiteral(vector);

        await WriteArchiveFileAtomicAsync(name, level, summary, content, startTime, endTime);

        await databaseLock.WaitAsync();
        try
        {
            using DuckDBCommand command = connection.CreateCommand();
            command.CommandText = $@"
                INSERT INTO MemoryStorage (Name, Level, Summary, Content, StartTime, EndTime, Vector)
                VALUES ($1, $2, $3, $4, $5, $6, {vectorLiteral})
                ON CONFLICT (Name) DO UPDATE SET
                Level = excluded.Level,
                Summary = excluded.Summary,
                Content = excluded.Content,
                StartTime = excluded.StartTime,
                EndTime = excluded.EndTime,
                Vector = excluded.Vector;
            ";//由于是先保存后压缩，且是异步执行，所以如果程序中断，第二次启动时可能重复压缩
            command.Parameters.Add(new DuckDBParameter(name));
            command.Parameters.Add(new DuckDBParameter(level));
            command.Parameters.Add(new DuckDBParameter(summary));
            command.Parameters.Add(new DuckDBParameter(content));
            command.Parameters.Add(new DuckDBParameter(startTime.ToUnixTimeMilliseconds()));
            command.Parameters.Add(new DuckDBParameter(endTime.ToUnixTimeMilliseconds()));
            command.ExecuteNonQuery();
        }
        finally
        {
            databaseLock.Release();
        }

    }

    /// <summary>
    /// 功能2：根据层级与名称读取出硬盘上的源文本
    /// </summary>
    public async Task<string?> LoadAsync(int level, string name)
    {
        string filePath = Path.Combine(rootPath, $"L{level}", $"{name}.txt");
        if (!File.Exists(filePath))
            return null;

        return await File.ReadAllTextAsync(filePath);
    }

    public async Task<MemoryStorageConsistencyReport> RepairConsistencyAsync()
    {
        int repairedArchiveFiles = 0;
        int repairedIndexRecords = 0;
        int repairedContentMismatches = 0;

        await databaseLock.WaitAsync();
        try
        {
            Dictionary<string, IndexedMemoryRecord> indexedRecords = ReadIndexedRecordsNoLock();
            List<ArchiveMemoryRecord> archiveRecords = ReadArchiveRecords();
            MemoryStorageConsistencyReport beforeRepair = BuildConsistencyReport(indexedRecords, archiveRecords);

            foreach (MemoryStorageConsistencyIssue issue in beforeRepair.Issues)
            {
                if (issue.Kind == MemoryStorageConsistencyIssueKind.MissingArchiveFile &&
                    indexedRecords.TryGetValue(MemoryKey(issue.Level, issue.Name), out IndexedMemoryRecord? indexedRecord))
                {
                    await WriteArchiveFileAtomicAsync(
                        indexedRecord.Name,
                        indexedRecord.Level,
                        indexedRecord.Summary,
                        indexedRecord.Content,
                        indexedRecord.StartTime,
                        indexedRecord.EndTime);
                    repairedArchiveFiles++;
                    continue;
                }

                if (issue.Kind == MemoryStorageConsistencyIssueKind.ContentMismatch &&
                    indexedRecords.TryGetValue(MemoryKey(issue.Level, issue.Name), out IndexedMemoryRecord? mismatchedRecord))
                {
                    await WriteArchiveFileAtomicAsync(
                        mismatchedRecord.Name,
                        mismatchedRecord.Level,
                        mismatchedRecord.Summary,
                        mismatchedRecord.Content,
                        mismatchedRecord.StartTime,
                        mismatchedRecord.EndTime);
                    repairedContentMismatches++;
                    continue;
                }

                if (issue.Kind != MemoryStorageConsistencyIssueKind.MissingIndexRecord)
                    continue;

                ArchiveMemoryRecord? archiveRecord = archiveRecords.FirstOrDefault(record =>
                    record.Name == issue.Name && record.Level == issue.Level);
                if (archiveRecord == null)
                    continue;

                float[] vector = await vectorizer.VectorizeAsync(archiveRecord.Summary);
                InsertIndexRecordNoLock(
                    archiveRecord.Name,
                    archiveRecord.Level,
                    archiveRecord.Summary,
                    archiveRecord.Content,
                    archiveRecord.StartTime,
                    archiveRecord.EndTime,
                    ToVectorLiteral(vector));
                repairedIndexRecords++;
            }

            LastConsistencyReport = ScanConsistencyNoLock() with {
                RepairedArchiveFiles = repairedArchiveFiles,
                RepairedIndexRecords = repairedIndexRecords,
                RepairedContentMismatches = repairedContentMismatches
            };

            return beforeRepair with {
                RepairedArchiveFiles = repairedArchiveFiles,
                RepairedIndexRecords = repairedIndexRecords,
                RepairedContentMismatches = repairedContentMismatches
            };
        }
        finally
        {
            databaseLock.Release();
        }
    }

    public async Task<MemoryStorageSanitizationReport> SanitizeAsync(
        MemoryTextSanitizer sanitizer,
        bool createBackups = true,
        bool revectorize = true)
    {
        int sanitizedArchiveRecords = 0;
        int removedSegments = 0;
        int backupFilesCreated = 0;
        string backupSuffix = $".bak-sanitize-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";

        await databaseLock.WaitAsync();
        try
        {
            Dictionary<string, IndexedMemoryRecord> indexedRecords = ReadIndexedRecordsNoLock();
            foreach (IndexedMemoryRecord record in indexedRecords.Values)
            {
                MemoryTextSanitizationResult summary = sanitizer.SanitizeText(record.Summary);
                MemoryTextSanitizationResult content = sanitizer.SanitizeText(record.Content);
                if (summary.Changed == false && content.Changed == false)
                    continue;

                string sanitizedSummary = string.IsNullOrWhiteSpace(summary.Text)
                    ? record.Summary
                    : summary.Text;
                string sanitizedContent = content.Text;
                string archivePath = GetArchivePath(record.Level, record.Name);
                if (createBackups && File.Exists(archivePath))
                {
                    File.Copy(archivePath, archivePath + backupSuffix, overwrite: false);
                    backupFilesCreated++;
                }

                await WriteArchiveFileAtomicAsync(
                    record.Name,
                    record.Level,
                    sanitizedSummary,
                    sanitizedContent,
                    record.StartTime,
                    record.EndTime);
                if (revectorize)
                {
                    float[] vector = await vectorizer.VectorizeAsync(sanitizedSummary);
                    InsertIndexRecordNoLock(
                        record.Name,
                        record.Level,
                        sanitizedSummary,
                        sanitizedContent,
                        record.StartTime,
                        record.EndTime,
                        ToVectorLiteral(vector));
                }
                else
                {
                    UpdateIndexedTextNoLock(
                        record.Name,
                        record.Level,
                        sanitizedSummary,
                        sanitizedContent);
                }

                sanitizedArchiveRecords++;
                removedSegments += summary.RemovedSegments + content.RemovedSegments;
            }

            LastConsistencyReport = ScanConsistencyNoLock();
            return new MemoryStorageSanitizationReport(
                sanitizedArchiveRecords,
                removedSegments,
                backupFilesCreated);
        }
        finally
        {
            databaseLock.Release();
        }
    }

    /// <summary>
    /// 功能3：原生的 DuckDB 侧全库综合高能搜索。直接下推余弦计算并依靠索引剪枝！
    /// 当 question 为空时，退化为纯关键词搜索并按时间从早到晚排序。
    /// </summary>
    public async Task<(List<SearchResult> Results, int Total)> SearchAsync(
        int level,
        string keyword,
        string? question,
        int topK = 5,
        int offset = 0,
        DateTimeOffset? minTime = null,
        DateTimeOffset? maxTime = null,
        MemorySearchMode searchMode = MemorySearchMode.Hybrid,
        bool includePermanent = true)
    {
        object minVal = minTime.HasValue ? minTime.Value.ToUnixTimeMilliseconds() : DBNull.Value;
        object maxVal = maxTime.HasValue ? maxTime.Value.ToUnixTimeMilliseconds() : DBNull.Value;
        float[]? queryVector = null;
        string? vectorPrompt = string.IsNullOrWhiteSpace(question) ? keyword : question;
        if (searchMode != MemorySearchMode.Keyword && !string.IsNullOrWhiteSpace(vectorPrompt))
            queryVector = await vectorizer.VectorizeAsync(vectorPrompt);

        await databaseLock.WaitAsync();
        try
        {
            using DuckDBCommand command = connection.CreateCommand();
            command.Parameters.Add(new DuckDBParameter(minVal));
            command.Parameters.Add(new DuckDBParameter(maxVal));
            command.Parameters.Add(new DuckDBParameter(level));
            command.Parameters.Add(new DuckDBParameter($"%{keyword}%"));
            command.Parameters.Add(new DuckDBParameter(includePermanent));

            const string LevelCondition = "AND (Level = $3 OR ($5 AND Level = 100))";
            bool requireKeywordMatch = searchMode == MemorySearchMode.Keyword || queryVector == null;

            if (queryVector != null)
            {
                string vectorLiteral = ToVectorLiteral(queryVector);
                string keywordCondition = requireKeywordMatch ? "AND Summary ILIKE $4" : "";
                command.CommandText = $@"
                    SELECT Name, Level, Summary, Content, StartTime, EndTime,
                           (
                             array_cosine_similarity(Vector, {vectorLiteral}::FLOAT[512])
                             + (CASE WHEN Summary ILIKE $4 THEN 1.0 ELSE 0.0 END)
                            )::REAL as Score,
                           COUNT(*) OVER() as Total
                    FROM MemoryStorage
                    WHERE ($1 IS NULL OR EndTime >= $1)
                      AND ($2 IS NULL OR StartTime <= $2)
                      {LevelCondition}
                      {keywordCondition}
                    ORDER BY Score DESC
                    LIMIT {topK} OFFSET {offset}
                ";
            }
            else
            {
                command.CommandText = $@"
                    SELECT Name, Level, Summary, Content, StartTime, EndTime,
                           0.0::REAL as Score,
                           COUNT(*) OVER() as Total
                    FROM MemoryStorage
                    WHERE ($1 IS NULL OR EndTime >= $1)
                      AND ($2 IS NULL OR StartTime <= $2)
                      {LevelCondition}
                      AND Summary ILIKE $4
                    ORDER BY EndTime ASC
                    LIMIT {topK} OFFSET {offset}
                ";
            }

            using DuckDBDataReader reader = command.ExecuteReader();

            List<SearchResult> results = new();
            int total = 0;
            while (reader.Read())
            {
                string name = reader.GetString(0);
                int resultLevel = reader.GetInt32(1);
                string summary = reader.GetString(2);
                string content = reader.GetString(3);
                long startMs = reader.GetInt64(4);
                long endMs = reader.GetInt64(5);
                float score = reader.GetFloat(6);
                total = reader.GetInt32(7);
                results.Add(new SearchResult(name, resultLevel, summary, content,
                DateTimeOffset.FromUnixTimeMilliseconds(startMs),
                DateTimeOffset.FromUnixTimeMilliseconds(endMs), score));
            }
            return (results, total);
        }
        finally
        {
            databaseLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await databaseLock.WaitAsync();
        try
        {
            await connection.DisposeAsync();
        }
        finally
        {
            databaseLock.Release();
            databaseLock.Dispose();
        }
    }

    void InsertIndexRecordNoLock(
        string name,
        int level,
        string summary,
        string content,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        string vectorLiteral)
    {
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = $@"
                INSERT INTO MemoryStorage (Name, Level, Summary, Content, StartTime, EndTime, Vector)
                VALUES ($1, $2, $3, $4, $5, $6, {vectorLiteral})
                ON CONFLICT (Name) DO UPDATE SET
                Level = excluded.Level,
                Summary = excluded.Summary,
                Content = excluded.Content,
                StartTime = excluded.StartTime,
                EndTime = excluded.EndTime,
                Vector = excluded.Vector;
            ";
        command.Parameters.Add(new DuckDBParameter(name));
        command.Parameters.Add(new DuckDBParameter(level));
        command.Parameters.Add(new DuckDBParameter(summary));
        command.Parameters.Add(new DuckDBParameter(content));
        command.Parameters.Add(new DuckDBParameter(startTime.ToUnixTimeMilliseconds()));
        command.Parameters.Add(new DuckDBParameter(endTime.ToUnixTimeMilliseconds()));
        command.ExecuteNonQuery();
    }

    void UpdateIndexedTextNoLock(
        string name,
        int level,
        string summary,
        string content)
    {
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = @"
                UPDATE MemoryStorage
                SET Summary = $1, Content = $2
                WHERE Name = $3 AND Level = $4;
            ";
        command.Parameters.Add(new DuckDBParameter(summary));
        command.Parameters.Add(new DuckDBParameter(content));
        command.Parameters.Add(new DuckDBParameter(name));
        command.Parameters.Add(new DuckDBParameter(level));
        command.ExecuteNonQuery();
    }

    MemoryStorageConsistencyReport ScanConsistencyNoLock()
    {
        return BuildConsistencyReport(ReadIndexedRecordsNoLock(), ReadArchiveRecords());
    }

    MemoryStorageConsistencyReport BuildConsistencyReport(
        Dictionary<string, IndexedMemoryRecord> indexedRecords,
        List<ArchiveMemoryRecord> archiveRecords)
    {
        HashSet<string> archiveKeys = archiveRecords.Select(record => MemoryKey(record.Level, record.Name)).ToHashSet();
        HashSet<string> indexKeys = indexedRecords.Values.Select(record => MemoryKey(record.Level, record.Name)).ToHashSet();
        Dictionary<string, ArchiveMemoryRecord> archiveRecordsByKey = archiveRecords.ToDictionary(
            record => MemoryKey(record.Level, record.Name),
            StringComparer.Ordinal);
        List<MemoryStorageConsistencyIssue> issues = new();

        foreach (IndexedMemoryRecord record in indexedRecords.Values)
        {
            string key = MemoryKey(record.Level, record.Name);
            if (archiveKeys.Contains(key) == false)
            {
                issues.Add(new MemoryStorageConsistencyIssue(
                    MemoryStorageConsistencyIssueKind.MissingArchiveFile,
                    record.Name,
                    record.Level,
                    GetArchivePath(record.Level, record.Name)));
                continue;
            }

            ArchiveMemoryRecord archiveRecord = archiveRecordsByKey[key];
            if (record.Summary != archiveRecord.Summary || record.Content != archiveRecord.Content)
            {
                issues.Add(new MemoryStorageConsistencyIssue(
                    MemoryStorageConsistencyIssueKind.ContentMismatch,
                    record.Name,
                    record.Level,
                    archiveRecord.Path));
            }
        }

        foreach (ArchiveMemoryRecord record in archiveRecords)
        {
            if (indexKeys.Contains(MemoryKey(record.Level, record.Name)))
                continue;

            issues.Add(new MemoryStorageConsistencyIssue(
                MemoryStorageConsistencyIssueKind.MissingIndexRecord,
                record.Name,
                record.Level,
                record.Path));
        }

        return new MemoryStorageConsistencyReport(
            issues.Count(issue => issue.Kind == MemoryStorageConsistencyIssueKind.MissingArchiveFile),
            issues.Count(issue => issue.Kind == MemoryStorageConsistencyIssueKind.MissingIndexRecord),
            issues.Count(issue => issue.Kind == MemoryStorageConsistencyIssueKind.ContentMismatch),
            0,
            0,
            0,
            issues);
    }

    Dictionary<string, IndexedMemoryRecord> ReadIndexedRecordsNoLock()
    {
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Name, Level, Summary, Content, StartTime, EndTime FROM MemoryStorage";
        using DuckDBDataReader reader = command.ExecuteReader();
        Dictionary<string, IndexedMemoryRecord> records = new();
        while (reader.Read())
        {
            string name = reader.GetString(0);
            int level = reader.GetInt32(1);
            records[MemoryKey(level, name)] = new IndexedMemoryRecord(
                name,
                level,
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)));
        }

        return records;
    }

    List<ArchiveMemoryRecord> ReadArchiveRecords()
    {
        if (!Directory.Exists(rootPath))
            return [];

        List<ArchiveMemoryRecord> records = new();
        foreach (string levelDirectory in Directory.GetDirectories(rootPath, "L*"))
        {
            string levelName = Path.GetFileName(levelDirectory);
            if (!int.TryParse(levelName.TrimStart('L'), out int level))
                continue;

            foreach (string archivePath in Directory.GetFiles(levelDirectory, "*.txt"))
            {
                string name = Path.GetFileNameWithoutExtension(archivePath);
                string archiveText = File.ReadAllText(archivePath);
                records.Add(ParseArchiveRecord(name, level, archivePath, archiveText));
            }
        }

        return records;
    }

    ArchiveMemoryRecord ParseArchiveRecord(string name, int level, string archivePath, string archiveText)
    {
        MatchCollection blocks = Regex.Matches(
            archiveText,
            @"```\s*(?<body>.*?)\s*```",
            RegexOptions.Singleline);

        string summary = blocks.Count > 0 ? blocks[0].Groups["body"].Value.Trim() : "";
        string content = blocks.Count > 1 ? blocks[1].Groups["body"].Value.Trim() : archiveText.Trim();
        DateTimeOffset timestamp = File.GetLastWriteTime(archivePath);

        return new ArchiveMemoryRecord(name, level, summary, content, timestamp, timestamp, archivePath);
    }

    async Task WriteArchiveFileAtomicAsync(
        string name,
        int level,
        string summary,
        string content,
        DateTimeOffset startTime,
        DateTimeOffset endTime)
    {
        string dir = Path.Combine(rootPath, $"L{level}");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string filePath = GetArchivePath(level, name);
        string tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath,
                $"""
                 压缩级别：{level}
                 时间范围：{startTime} 到 {endTime}
                 内容概述：
                 ```
                 {summary}
                 ```
                 原始内容：
                 ```
                 {content}
                 ```
                 """);
            File.Move(tempPath, filePath, true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    string GetArchivePath(int level, string name) => Path.Combine(rootPath, $"L{level}", $"{name}.txt");
    static string MemoryKey(int level, string name) => $"{level}:{name}";
    static string ToVectorLiteral(float[] vector) => "[" + string.Join(",", vector.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]";

    readonly string rootPath;
    readonly string dbPath;
    readonly ITextVectorizer vectorizer;
    readonly DuckDBConnection connection;
    readonly SemaphoreSlim databaseLock = new(1, 1);

    record IndexedMemoryRecord(string Name, int Level, string Summary, string Content, DateTimeOffset StartTime, DateTimeOffset EndTime);
    record ArchiveMemoryRecord(string Name, int Level, string Summary, string Content, DateTimeOffset StartTime, DateTimeOffset EndTime, string Path);
}
