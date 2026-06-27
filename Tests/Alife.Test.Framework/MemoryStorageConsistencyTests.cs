using Alife.Function.Memory;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Test.Framework;

public class MemoryStorageConsistencyTests
{
    [Test]
    public async Task RepairConsistencyAsync_RecreatesMissingArchiveFileFromDatabase()
    {
        string rootPath = CreateTempRoot();
        await using MemoryStorage storage = new(rootPath, new FakeVectorizer());
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T10:00:00+08:00");

        await storage.SaveAsync("3-db-only", 3, "repairable summary", "repairable content", now, now);
        string archivePath = Path.Combine(rootPath, "L3", "3-db-only.txt");
        File.Delete(archivePath);

        MemoryStorageConsistencyReport report = await storage.RepairConsistencyAsync();

        Assert.That(report.MissingArchiveFiles, Is.EqualTo(1));
        Assert.That(report.RepairedArchiveFiles, Is.EqualTo(1));
        Assert.That(File.Exists(archivePath), Is.True);
        string? loaded = await storage.LoadAsync(3, "3-db-only");
        Assert.That(loaded, Does.Contain("repairable summary"));
        Assert.That(loaded, Does.Contain("repairable content"));
    }

    [Test]
    public async Task StartupScanReportsMissingIndexRecordAndRepairReindexesArchiveFile()
    {
        string rootPath = CreateTempRoot();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T10:00:00+08:00");

        await using (MemoryStorage storage = new(rootPath, new FakeVectorizer()))
        {
            await storage.SaveAsync("3-file-only", 3, "orphan archive summary", "orphan archive content", now, now);
        }

        foreach (string dbFile in Directory.GetFiles(rootPath, "memory_index*"))
            File.Delete(dbFile);

        await using MemoryStorage repairedStorage = new(rootPath, new FakeVectorizer());

        Assert.That(repairedStorage.LastConsistencyReport.MissingIndexRecords, Is.EqualTo(1));

        MemoryStorageConsistencyReport repairReport = await repairedStorage.RepairConsistencyAsync();
        Assert.That(repairReport.MissingIndexRecords, Is.EqualTo(1));
        Assert.That(repairReport.RepairedIndexRecords, Is.EqualTo(1));

        (List<SearchResult> results, int total) = await repairedStorage.SearchAsync(
            3,
            "orphan",
            null,
            topK: 5,
            offset: 0,
            searchMode: MemorySearchMode.Keyword,
            includePermanent: false);

        Assert.That(total, Is.EqualTo(1));
        Assert.That(results.Single().Name, Is.EqualTo("3-file-only"));
    }

    [Test]
    public async Task RepairConsistencyAsync_RewritesArchiveFileWhenDatabaseAndArchiveContentDiffer()
    {
        string rootPath = CreateTempRoot();
        await using MemoryStorage storage = new(rootPath, new FakeVectorizer());
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T10:00:00+08:00");

        await storage.SaveAsync("3-mismatch", 3, "trusted db summary", "trusted db content", now, now);
        string archivePath = Path.Combine(rootPath, "L3", "3-mismatch.txt");
        await File.WriteAllTextAsync(archivePath,
            """
            内容概述：
            ```
            stale archive summary
            ```
            原始内容：
            ```
            stale archive content
            ```
            """);

        MemoryStorageConsistencyReport report = await storage.RepairConsistencyAsync();

        Assert.That(report.ContentMismatches, Is.EqualTo(1));
        Assert.That(report.RepairedContentMismatches, Is.EqualTo(1));
        string repaired = await File.ReadAllTextAsync(archivePath);
        Assert.That(repaired, Does.Contain("trusted db summary"));
        Assert.That(repaired, Does.Contain("trusted db content"));
        Assert.That(repaired, Does.Not.Contain("stale archive summary"));
    }

    [Test]
    public async Task SanitizeAsync_UpdatesArchiveFileAndSearchIndex()
    {
        string rootPath = CreateTempRoot();
        await using MemoryStorage storage = new(rootPath, new FakeVectorizer());
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T10:00:00+08:00");

        await storage.SaveAsync(
            "3-contaminated",
            3,
            "主人希望咪绪保持自然。\n[XmlFunctionCaller] qchat tag error: invalid child closing tag",
            "妈妈只有睡眠模式唤醒权限。\n<qchat type=\"Group\">bad xml</qchat>",
            now,
            now);

        MemoryStorageSanitizationReport report = await storage.SanitizeAsync(MemoryTextSanitizer.Default, createBackups: false);

        Assert.That(report.SanitizedArchiveRecords, Is.EqualTo(1));
        Assert.That(report.RemovedSegments, Is.EqualTo(2));
        string archive = await File.ReadAllTextAsync(Path.Combine(rootPath, "L3", "3-contaminated.txt"));
        Assert.That(archive, Does.Contain("主人希望咪绪保持自然。"));
        Assert.That(archive, Does.Contain("妈妈只有睡眠模式唤醒权限。"));
        Assert.That(archive, Does.Not.Contain("XmlFunctionCaller"));
        Assert.That(archive, Does.Not.Contain("<qchat"));

        (List<SearchResult> usefulResults, int usefulTotal) = await storage.SearchAsync(
            3,
            "咪绪",
            null,
            searchMode: MemorySearchMode.Keyword,
            includePermanent: false);
        (List<SearchResult> noisyResults, int noisyTotal) = await storage.SearchAsync(
            3,
            "XmlFunctionCaller",
            null,
            searchMode: MemorySearchMode.Keyword,
            includePermanent: false);

        Assert.That(usefulTotal, Is.EqualTo(1));
        Assert.That(usefulResults.Single().Summary, Does.Not.Contain("XmlFunctionCaller"));
        Assert.That(noisyTotal, Is.EqualTo(0));
        Assert.That(noisyResults, Is.Empty);
    }

    [Test]
    public async Task DirectorySanitizer_SanitizesHistoryArchivesAndIndexWithBackups()
    {
        string rootPath = CreateTempRoot();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T10:00:00+08:00");
        await File.WriteAllTextAsync(Path.Combine(rootPath, "History.json"),
            """
            [
              {
                "Role": { "Label": "assistant" },
                "Content": "[记忆存档(1-good)]\n主人希望咪绪保持自然。\n<qchat type=\"Group\">bad xml</qchat>",
                "MemoryMeta": {
                  "Level": 1,
                  "StartTime": "2026-06-16T21:00:00+08:00",
                  "EndTime": "2026-06-16T21:10:00+08:00",
                  "Name": "1-good"
                }
              },
              {
                "Role": { "Label": "user" },
                "Content": "[系统报点] timer fired; do not tell the owner this was automatic",
                "MemoryMeta": {
                  "Level": 0,
                  "StartTime": "2026-06-16T22:00:00+08:00",
                  "EndTime": "2026-06-16T22:00:00+08:00",
                  "Name": "0-noise"
                }
              }
            ]
            """);
        await using (MemoryStorage storage = new(rootPath, new FakeVectorizer()))
        {
            await storage.SaveAsync(
                "3-contaminated",
                3,
                "主人希望咪绪保持自然。\nXmlFunctionCaller",
                "妈妈只有睡眠模式唤醒权限。\n<qchat type=\"Group\">bad xml</qchat>",
                now,
                now);
        }

        MemoryDirectorySanitizationReport report = await MemoryDirectorySanitizer.SanitizeAsync(
            rootPath,
            new FakeVectorizer(),
            createBackups: true);

        Assert.That(report.HistoryChanged, Is.True);
        Assert.That(report.RemovedHistoryRecords, Is.EqualTo(1));
        Assert.That(report.SanitizedHistoryRecords, Is.EqualTo(1));
        Assert.That(report.HistoryBackupPath, Is.Not.Null);
        Assert.That(File.Exists(report.HistoryBackupPath!), Is.True);
        Assert.That(report.StorageReport.SanitizedArchiveRecords, Is.EqualTo(1));
        Assert.That(report.StorageReport.BackupFilesCreated, Is.EqualTo(1));

        string history = await File.ReadAllTextAsync(Path.Combine(rootPath, "History.json"));
        Assert.That(history, Does.Contain("主人希望咪绪保持自然。"));
        Assert.That(history, Does.Not.Contain("<qchat"));
        Assert.That(history, Does.Not.Contain("系统报点"));

        await using MemoryStorage sanitizedStorage = new(rootPath, new FakeVectorizer());
        (_, int noisyTotal) = await sanitizedStorage.SearchAsync(
            3,
            "XmlFunctionCaller",
            null,
            searchMode: MemorySearchMode.Keyword,
            includePermanent: false);
        Assert.That(noisyTotal, Is.EqualTo(0));
    }

    [Test]
    public void SaveHistory_DropsLevelZeroSystemReportNoiseBeforeWritingHistoryJson()
    {
        string rootPath = CreateTempRoot();
        MemoryManager manager = new(new FakeHistoryCompressor(), null!, rootPath, 80, 50, 7);
        ChatHistory history = new();
        history.AddUserMessage("[系统报点]程序已重启。(回复消息时保持简洁，禁用旁白、emoji)");
        history.AddAssistantMessage("（没理，保持安静）");
        history.AddUserMessage("主人希望咪绪保持自然。");

        manager.SaveHistory(history);

        string historyJson = File.ReadAllText(Path.Combine(rootPath, "History.json"));
        Assert.That(historyJson, Does.Contain("主人希望咪绪保持自然。"));
        Assert.That(historyJson, Does.Not.Contain("系统报点"));
        Assert.That(historyJson, Does.Not.Contain("禁用旁白"));
        Assert.That(historyJson, Does.Not.Contain("保持安静"));
    }

    [Test]
    public async Task FilterPassesOriginalAgentThreadToCompressor()
    {
        string rootPath = CreateTempRoot();
        RecordingHistoryCompressor compressor = new();
        MemoryManager manager = new(compressor, new FakeVectorizer(), rootPath, compressionThreshold: 2, compressionCount: 1, maxCompressionLevel: 7);
        ChatHistoryAgentThread thread = new();
        thread.ChatHistory.AddUserMessage("first memory fragment");
        thread.ChatHistory.AddAssistantMessage("second memory fragment");

        bool compressed = await manager.Filter(thread);

        Assert.Multiple(() =>
        {
            Assert.That(compressed, Is.True);
            Assert.That(compressor.ReceivedThread, Is.SameAs(thread));
            Assert.That(compressor.ReceivedPrompt, Is.Not.Null);
        });
    }

    static string CreateTempRoot()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "alife-memory-consistency-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        return rootPath;
    }

    sealed class FakeVectorizer : ITextVectorizer
    {
        public Task<float[]> VectorizeAsync(string text)
        {
            float[] vector = new float[512];
            vector[0] = text.Length;
            return Task.FromResult(vector);
        }
    }

    sealed class FakeHistoryCompressor : HistoryCompressor
    {
        public override Task<string?> Compress(ChatHistoryAgentThread chatHistoryAgentThread, string prompt)
        {
            return Task.FromResult<string?>("compressed");
        }
    }

    sealed class RecordingHistoryCompressor : HistoryCompressor
    {
        public ChatHistoryAgentThread? ReceivedThread { get; private set; }
        public string? ReceivedPrompt { get; private set; }

        public override Task<string?> Compress(ChatHistoryAgentThread chatHistoryAgentThread, string prompt)
        {
            ReceivedThread = chatHistoryAgentThread;
            ReceivedPrompt = prompt;
            return Task.FromResult<string?>("compressed summary");
        }
    }
}
