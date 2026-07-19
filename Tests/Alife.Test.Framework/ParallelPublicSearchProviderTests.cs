using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    public void Merge_PreservesTrailingSlashInsideQueryValue()
    {
        IReadOnlyList<AgentPublicSearchResult> merged = AgentPublicSearchResultMerger.Merge(
        [
            new AgentPublicSearchCandidate("duckduckgo", 0, 0,
                new AgentPublicSearchResult("Slash query", "https://Example.test/search?q=/", "slash")),
            new AgentPublicSearchCandidate("bing", 1, 0,
                new AgentPublicSearchResult("Empty query", "https://example.test/search?q=", "empty"))
        ], maxResults: 5);

        Assert.That(merged, Is.EqualTo(new[]
        {
            new AgentPublicSearchResult("Slash query", "https://example.test/search?q=/", "slash"),
            new AgentPublicSearchResult("Empty query", "https://example.test/search?q=", "empty")
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

    [Test]
    public async Task SearchAsync_StartsBothProvidersAndReturnsFastEvidenceWithoutWaitingForSlowPeer()
    {
        GateProvider slow = new(waitForRelease: true);
        GateProvider fast = new([
            new AgentPublicSearchResult("Fast", "https://example.test/fast", "fast")
        ]);
        fast.BeforeReturn = slow.WaitForStartAsync;
        ParallelPublicSearchProvider provider = new(
            slow,
            fast,
            new AgentMultiSourceSearchConfig { Enabled = true, PerProviderTimeoutMilliseconds = 1000 });

        IReadOnlyList<AgentPublicSearchResult> results = await provider.SearchAsync("latest test", 5)
            .WaitAsync(TimeSpan.FromMilliseconds(250));

        await slow.WaitForCancellationAsync();
        Assert.Multiple(() =>
        {
            Assert.That(slow.Calls, Is.EqualTo(1));
            Assert.That(fast.Calls, Is.EqualTo(1));
            Assert.That(results, Is.EqualTo(new[]
            {
                new AgentPublicSearchResult("Fast", "https://example.test/fast", "fast")
            }));
        });
    }

    [Test]
    public async Task SearchAsync_OneFailureReturnsOtherProviderEvidence()
    {
        ParallelPublicSearchProvider provider = new(
            new GateProvider(exception: new InvalidOperationException("duck failed")),
            new GateProvider([
                new AgentPublicSearchResult("Bing", "https://example.test/bing", "ok")
            ]),
            new AgentMultiSourceSearchConfig { Enabled = true });

        IReadOnlyList<AgentPublicSearchResult> results = await provider.SearchAsync("query", 5);

        Assert.That(results, Is.EqualTo(new[]
        {
            new AgentPublicSearchResult("Bing", "https://example.test/bing", "ok")
        }));
    }

    [Test]
    public void SearchAsync_BothProvidersFail_ThrowsSearchFailureWithoutEvidence()
    {
        ParallelPublicSearchProvider provider = new(
            new GateProvider(exception: new InvalidOperationException("duck failed")),
            new GateProvider(exception: new InvalidOperationException("bing failed")),
            new AgentMultiSourceSearchConfig { Enabled = true });

        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SearchAsync("query", 5))!;

        Assert.That(exception.Message, Is.EqualTo("public_search_all_providers_failed"));
    }

    [Test]
    public async Task SearchAsync_CallerCancellationCancelsBothProviders()
    {
        GateProvider duck = new(waitForRelease: true);
        GateProvider bing = new(waitForRelease: true);
        ParallelPublicSearchProvider provider = new(duck, bing, new AgentMultiSourceSearchConfig { Enabled = true });
        using CancellationTokenSource cancellation = new();
        Task<IReadOnlyList<AgentPublicSearchResult>> search = provider.SearchAsync("query", 5, cancellation.Token);

        await Task.WhenAll(duck.WaitForStartAsync(), bing.WaitForStartAsync());
        cancellation.Cancel();

        Assert.That(async () => await search, Throws.InstanceOf<OperationCanceledException>());
        await Task.WhenAll(duck.WaitForCancellationAsync(), bing.WaitForCancellationAsync());
    }

    [Test]
    public async Task SearchAsync_ProviderTimeoutCancelsTimedOutProviderWhenPeerHasNoEvidence()
    {
        GateProvider slow = new(waitForRelease: true);
        GateProvider empty = new();
        ParallelPublicSearchProvider provider = new(
            slow,
            empty,
            new AgentMultiSourceSearchConfig { Enabled = true, PerProviderTimeoutMilliseconds = 20 });

        IReadOnlyList<AgentPublicSearchResult> results = await provider.SearchAsync("query", 5);

        Assert.Multiple(() =>
        {
            Assert.That(slow.Calls, Is.EqualTo(1));
            Assert.That(empty.Calls, Is.EqualTo(1));
            Assert.That(results, Is.Empty);
        });
        await slow.WaitForCancellationAsync();
    }

    [Test]
    public async Task SearchAsync_TimeoutReturnsWhenProviderIgnoresCancellation()
    {
        IgnoringCancellationProvider slow = new();
        ParallelPublicSearchProvider provider = new(
            slow,
            new GateProvider(),
            new AgentMultiSourceSearchConfig { Enabled = true, PerProviderTimeoutMilliseconds = 20 });
        Task<IReadOnlyList<AgentPublicSearchResult>> search = provider.SearchAsync("query", 5);

        await slow.WaitForStartAsync();
        try
        {
            IReadOnlyList<AgentPublicSearchResult> results = await search.WaitAsync(TimeSpan.FromMilliseconds(250));
            Assert.That(results, Is.Empty);
        }
        finally
        {
            slow.Release();
        }
    }

    [Test]
    public async Task SearchAsync_TimeoutKeepsProviderTokenUsableUntilLateTaskCompletes()
    {
        LateTokenUseProvider slow = new();
        ParallelPublicSearchProvider provider = new(
            slow,
            new GateProvider(),
            new AgentMultiSourceSearchConfig { Enabled = true, PerProviderTimeoutMilliseconds = 20 });

        IReadOnlyList<AgentPublicSearchResult> results = await provider.SearchAsync("query", 5)
            .WaitAsync(TimeSpan.FromMilliseconds(250));
        Assert.That(results, Is.Empty);

        slow.AccessTokenAndComplete();
        await slow.WaitForCompletionAsync();
        Assert.That(slow.TokenAccessException, Is.Null);
    }

    [Test]
    public async Task SearchAsync_OpensOnlyFailingProviderCircuitThenRetriesAfterConfiguredWindow()
    {
        MutableTimeProvider clock = new(DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        GateProvider failingDuck = new(exception: new InvalidOperationException("duck failed"));
        GateProvider healthyBing = new([
            new AgentPublicSearchResult("Bing", "https://example.test/bing", "ok")
        ]);
        ParallelPublicSearchProvider provider = new(
            failingDuck,
            healthyBing,
            new AgentMultiSourceSearchConfig
            {
                Enabled = true,
                FailureThreshold = 3,
                CircuitBreakSeconds = 60
            },
            clock);

        await provider.SearchAsync("one", 5);
        await provider.SearchAsync("two", 5);
        await provider.SearchAsync("three", 5);
        await provider.SearchAsync("four", 5);
        clock.Advance(TimeSpan.FromSeconds(61));
        await provider.SearchAsync("five", 5);

        Assert.Multiple(() =>
        {
            Assert.That(failingDuck.Calls, Is.EqualTo(4));
            Assert.That(healthyBing.Calls, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task SearchAsync_AllowsOneHalfOpenProbeAfterCircuitWindow()
    {
        MutableTimeProvider clock = new(DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        GateProvider duck = new(exception: new InvalidOperationException("duck failed"));
        GateProvider bing = new([
            new AgentPublicSearchResult("Bing", "https://example.test/bing", "ok")
        ]);
        ParallelPublicSearchProvider provider = new(
            duck,
            bing,
            new AgentMultiSourceSearchConfig
            {
                Enabled = true,
                FailureThreshold = 1,
                CircuitBreakSeconds = 60
            },
            clock);

        await provider.SearchAsync("first", 5);
        duck.Exception = null;
        duck.WaitForRelease = true;
        bing.WaitForRelease = true;
        clock.Advance(TimeSpan.FromSeconds(61));

        Task<IReadOnlyList<AgentPublicSearchResult>> firstProbe = provider.SearchAsync("second", 5);
        Task<IReadOnlyList<AgentPublicSearchResult>> concurrentRequest = provider.SearchAsync("third", 5);
        try
        {
            Assert.That(duck.Calls, Is.EqualTo(2));
        }
        finally
        {
            duck.Release();
            bing.Release();
        }

        await Task.WhenAll(firstProbe, concurrentRequest);
    }

    [Test]
    public async Task SearchAsync_PeerCancelledHalfOpenProbeIsReleasedForNextRequest()
    {
        MutableTimeProvider clock = new(DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
        GateProvider duck = new(exception: new InvalidOperationException("duck failed"));
        GateProvider bing = new([
            new AgentPublicSearchResult("Bing", "https://example.test/bing", "ok")
        ]);
        ParallelPublicSearchProvider provider = new(
            duck,
            bing,
            new AgentMultiSourceSearchConfig
            {
                Enabled = true,
                FailureThreshold = 1,
                CircuitBreakSeconds = 60
            },
            clock);

        await provider.SearchAsync("first", 5);
        duck.Exception = null;
        duck.WaitForRelease = true;
        clock.Advance(TimeSpan.FromSeconds(61));

        await provider.SearchAsync("second", 5);
        await duck.WaitForCancellationAsync();
        await provider.SearchAsync("third", 5);

        Assert.That(duck.Calls, Is.EqualTo(3));
    }

    sealed class GateProvider : IAgentPublicSearchProvider
    {
        readonly IReadOnlyList<AgentPublicSearchResult> results;
        bool waitForRelease;
        Exception? exception;
        readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GateProvider(
            IReadOnlyList<AgentPublicSearchResult>? results = null,
            bool waitForRelease = false,
            Exception? exception = null)
        {
            this.results = results ?? [];
            this.waitForRelease = waitForRelease;
            this.exception = exception;
        }

        public Func<Task>? BeforeReturn { get; set; }
        public int Calls { get; private set; }
        public bool WaitForRelease
        {
            get => waitForRelease;
            set => waitForRelease = value;
        }
        public Exception? Exception
        {
            get => exception;
            set => exception = value;
        }

        public async Task<IReadOnlyList<AgentPublicSearchResult>> SearchAsync(
            string query,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            started.TrySetResult();
            if (BeforeReturn != null)
                await BeforeReturn();
            if (exception != null)
                throw exception;
            if (waitForRelease)
            {
                try
                {
                    await release.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.TrySetResult();
                    throw;
                }
            }

            return results;
        }

        public Task WaitForStartAsync() => started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        public Task WaitForCancellationAsync() => cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        public void Release() => release.TrySetResult();
    }

    sealed class IgnoringCancellationProvider : IAgentPublicSearchProvider
    {
        readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<AgentPublicSearchResult>> SearchAsync(
            string query,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await release.Task;
            return [];
        }

        public Task WaitForStartAsync() => started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        public void Release() => release.TrySetResult();
    }

    sealed class LateTokenUseProvider : IAgentPublicSearchProvider
    {
        readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? TokenAccessException { get; private set; }

        public async Task<IReadOnlyList<AgentPublicSearchResult>> SearchAsync(
            string query,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            await release.Task;
            try
            {
                using CancellationTokenRegistration registration = cancellationToken.Register(static () => { });
                _ = cancellationToken.WaitHandle.WaitOne(0);
            }
            catch (Exception exception)
            {
                TokenAccessException = exception;
            }
            finally
            {
                completed.TrySetResult();
            }

            return [];
        }

        public void AccessTokenAndComplete() => release.TrySetResult();
        public Task WaitForCompletionAsync() => completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    sealed class MutableTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        DateTimeOffset current = initial;

        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan elapsed) => current = current.Add(elapsed);
    }
}
