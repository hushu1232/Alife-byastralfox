using Alife.Function.Memory;

namespace Alife.Test.Framework;

public class MemoryStorageSearchTests
{
    [Test]
    public async Task ConstructorCreatesRootDirectoryBeforeOpeningDuckDb()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "alife-memory-search-tests", Guid.NewGuid().ToString("N"));

        await using MemoryStorage storage = new(rootPath, new FakeVectorizer());

        Assert.That(Directory.Exists(rootPath), Is.True);
        Assert.That(File.Exists(Path.Combine(rootPath, "memory_index.duckdb")), Is.True);
    }

    [Test]
    public async Task SearchAsync_VectorModeCanRecallWithoutKeywordMatch()
    {
        string rootPath = CreateTempRoot();
        await using MemoryStorage storage = new(rootPath, new FakeVectorizer());
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T10:00:00+08:00");

        await storage.SaveAsync("3-semantic", 3, "semantic moon festival memory", "content", now, now);
        await storage.SaveAsync("3-unrelated", 3, "plain cooking memory", "content", now, now.AddMinutes(1));

        (List<SearchResult> results, int total) = await storage.SearchAsync(
            3,
            "keyword-that-does-not-exist",
            "moon prompt",
            topK: 5,
            offset: 0,
            searchMode: MemorySearchMode.Vector,
            includePermanent: false);

        Assert.That(total, Is.EqualTo(2));
        Assert.That(results, Is.Not.Empty);
        Assert.That(results[0].Name, Is.EqualTo("3-semantic"));
    }

    [Test]
    public async Task SearchAsync_CanIncludePermanentAutobiographicalLevel()
    {
        string rootPath = CreateTempRoot();
        await using MemoryStorage storage = new(rootPath, new FakeVectorizer());
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T10:00:00+08:00");

        await storage.SaveAsync("3-normal", 3, "ordinary project memory", "content", now, now);
        await storage.SaveAsync("100-life", 100, "autobiographical project memory", "content", now, now.AddMinutes(1));

        (List<SearchResult> results, int total) = await storage.SearchAsync(
            3,
            "autobiographical",
            null,
            topK: 5,
            offset: 0,
            searchMode: MemorySearchMode.Keyword,
            includePermanent: true);

        Assert.That(total, Is.EqualTo(1));
        Assert.That(results.Single().Name, Is.EqualTo("100-life"));
        Assert.That(results.Single().Level, Is.EqualTo(100));
    }

    static string CreateTempRoot()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "alife-memory-search-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        return rootPath;
    }

    sealed class FakeVectorizer : ITextVectorizer
    {
        public Task<float[]> VectorizeAsync(string text)
        {
            float[] vector = new float[512];
            if (text.Contains("moon", StringComparison.OrdinalIgnoreCase))
                vector[0] = 1;
            else
                vector[1] = 1;
            return Task.FromResult(vector);
        }
    }
}
