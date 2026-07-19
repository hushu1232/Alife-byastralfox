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
                new AgentPublicSearchResult("Release notes from Bing", "https://example.test:443/news/", "second")),
            new AgentPublicSearchCandidate("bing", 1, 1,
                new AgentPublicSearchResult("Other", "https://example.test/other", "other"))
        ], maxResults: 5);

        Assert.That(merged, Is.EqualTo(new[]
        {
            new AgentPublicSearchResult("Release notes", "https://example.test/news", "first"),
            new AgentPublicSearchResult("Other", "https://example.test/other", "other")
        }));
    }

    [Test]
    public void Merge_RemovesNearIdenticalTitles_AndKeepsDeterministicSourceOrder()
    {
        IReadOnlyList<AgentPublicSearchResult> merged = AgentPublicSearchResultMerger.Merge(
        [
            new AgentPublicSearchCandidate("duckduckgo", 0, 0,
                new AgentPublicSearchResult(".NET 9 release notes July", "https://example.test/a", "a")),
            new AgentPublicSearchCandidate("bing", 1, 0,
                new AgentPublicSearchResult(".NET 9 release note July", "https://example.test/b", "b")),
            new AgentPublicSearchCandidate("bing", 1, 1,
                new AgentPublicSearchResult("Independent source", "https://example.test/c", "c"))
        ], maxResults: 2);

        Assert.That(merged, Is.EqualTo(new[]
        {
            new AgentPublicSearchResult(".NET 9 release notes July", "https://example.test/a", "a"),
            new AgentPublicSearchResult("Independent source", "https://example.test/c", "c")
        }));
    }

    [Test]
    public void Merge_DeduplicatesExactTitlesButRetainsDistinctEmptyTitles()
    {
        IReadOnlyList<AgentPublicSearchResult> merged = AgentPublicSearchResultMerger.Merge(
        [
            new AgentPublicSearchCandidate("duckduckgo", 0, 0,
                new AgentPublicSearchResult("Release notes", "https://example.test/a", "a")),
            new AgentPublicSearchCandidate("bing", 1, 0,
                new AgentPublicSearchResult("Release notes", "https://example.test/b", "b")),
            new AgentPublicSearchCandidate("bing", 1, 1,
                new AgentPublicSearchResult("", "https://example.test/c", "c")),
            new AgentPublicSearchCandidate("bing", 1, 2,
                new AgentPublicSearchResult("", "https://example.test/d", "d"))
        ], maxResults: 5);

        Assert.That(merged, Is.EqualTo(new[]
        {
            new AgentPublicSearchResult("Release notes", "https://example.test/a", "a"),
            new AgentPublicSearchResult("", "https://example.test/c", "c"),
            new AgentPublicSearchResult("", "https://example.test/d", "d")
        }));
    }

    [Test]
    public void Merge_DoesNotReserveUrlForCandidateRejectedByDuplicateTitle()
    {
        IReadOnlyList<AgentPublicSearchResult> merged = AgentPublicSearchResultMerger.Merge(
        [
            new AgentPublicSearchCandidate("duckduckgo", 0, 0,
                new AgentPublicSearchResult("Release notes", "https://example.test/a", "first")),
            new AgentPublicSearchCandidate("bing", 1, 0,
                new AgentPublicSearchResult("Release notes", "https://example.test/b", "duplicate title")),
            new AgentPublicSearchCandidate("bing", 1, 1,
                new AgentPublicSearchResult("Independent source", "https://example.test/b", "valid later result"))
        ], maxResults: 5);

        Assert.That(merged, Is.EqualTo(new[]
        {
            new AgentPublicSearchResult("Release notes", "https://example.test/a", "first"),
            new AgentPublicSearchResult("Independent source", "https://example.test/b", "valid later result")
        }));
    }

    [Test]
    public void Merge_DropsInvalidOrNonHttpUrls()
    {
        IReadOnlyList<AgentPublicSearchResult> merged = AgentPublicSearchResultMerger.Merge(
        [
            new AgentPublicSearchCandidate("duckduckgo", 0, 0,
                new AgentPublicSearchResult("FTP", "ftp://example.test/file", "ftp")),
            new AgentPublicSearchCandidate("bing", 1, 0,
                new AgentPublicSearchResult("Invalid", "not a URL", "invalid")),
            new AgentPublicSearchCandidate("bing", 1, 1,
                new AgentPublicSearchResult("Valid", "https://example.test/valid", "valid"))
        ], maxResults: 5);

        Assert.That(merged, Is.EqualTo(new[]
        {
            new AgentPublicSearchResult("Valid", "https://example.test/valid", "valid")
        }));
    }

    [Test]
    public void Merge_NormalizesRootUrlWithoutTrailingSlash()
    {
        IReadOnlyList<AgentPublicSearchResult> merged = AgentPublicSearchResultMerger.Merge(
        [
            new AgentPublicSearchCandidate("duckduckgo", 0, 0,
                new AgentPublicSearchResult("Root", "https://Example.test:443/#top", "first")),
            new AgentPublicSearchCandidate("bing", 1, 0,
                new AgentPublicSearchResult("Root mirror", "https://example.test/", "second"))
        ], maxResults: 5);

        Assert.That(merged, Is.EqualTo(new[]
        {
            new AgentPublicSearchResult("Root", "https://example.test", "first")
        }));
    }

    [Test]
    public void Merge_DropsRuntimeNullResultBeforeSorting()
    {
        IReadOnlyList<AgentPublicSearchResult> merged = AgentPublicSearchResultMerger.Merge(
        [
            new AgentPublicSearchCandidate("duckduckgo", 0, 0, null!),
            new AgentPublicSearchCandidate("bing", 1, 0,
                new AgentPublicSearchResult("Valid", "https://example.test/valid", "valid"))
        ], maxResults: 5);

        Assert.That(merged, Is.EqualTo(new[]
        {
            new AgentPublicSearchResult("Valid", "https://example.test/valid", "valid")
        }));
    }

    [Test]
    public void Merge_ClampsOutputToFiveResultsInStableOrder()
    {
        IReadOnlyList<AgentPublicSearchResult> merged = AgentPublicSearchResultMerger.Merge(
        [
            new AgentPublicSearchCandidate("duckduckgo", 0, 0, new AgentPublicSearchResult("One", "https://example.test/1", "1")),
            new AgentPublicSearchCandidate("duckduckgo", 0, 1, new AgentPublicSearchResult("Two", "https://example.test/2", "2")),
            new AgentPublicSearchCandidate("duckduckgo", 0, 2, new AgentPublicSearchResult("Three", "https://example.test/3", "3")),
            new AgentPublicSearchCandidate("bing", 1, 0, new AgentPublicSearchResult("Four", "https://example.test/4", "4")),
            new AgentPublicSearchCandidate("bing", 1, 1, new AgentPublicSearchResult("Five", "https://example.test/5", "5")),
            new AgentPublicSearchCandidate("bing", 1, 2, new AgentPublicSearchResult("Six", "https://example.test/6", "6"))
        ], maxResults: 99);

        Assert.Multiple(() =>
        {
            Assert.That(merged, Has.Count.EqualTo(5));
            Assert.That(merged[0].Url, Is.EqualTo("https://example.test/1"));
            Assert.That(merged[^1].Url, Is.EqualTo("https://example.test/5"));
        });
    }
}
