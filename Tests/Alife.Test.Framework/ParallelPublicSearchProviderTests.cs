using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class ParallelPublicSearchProviderTests
{
    [Test]
    public void Merge_RemovesFragmentAndTrailingSlashUrlDuplicates_AndKeepsFirstStableResult()
    {
        IReadOnlyList<AgentPublicSearchResult> merged = AgentPublicSearchResultMerger.Merge(
        [
            new AgentPublicSearchCandidate("duckduckgo", 0, 0,
                new AgentPublicSearchResult("Release notes", "HTTPS://Example.test/news/#section", "first")),
            new AgentPublicSearchCandidate("bing", 1, 0,
                new AgentPublicSearchResult("Release notes from Bing", "https://example.test/news/", "second")),
            new AgentPublicSearchCandidate("bing", 1, 1,
                new AgentPublicSearchResult("Other", "https://example.test/other", "other"))
        ], maxResults: 5);

        Assert.That(merged, Is.EqualTo(new[]
        {
            new AgentPublicSearchResult("Release notes", "https://example.test/news", "first"),
            new AgentPublicSearchResult("Other", "https://example.test/other", "other")
        }));
    }
}
