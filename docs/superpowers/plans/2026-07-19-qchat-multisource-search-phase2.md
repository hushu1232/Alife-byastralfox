# QChat Multi-Source Web Research Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add opt-in, low-latency parallel DuckDuckGo/Bing research for QChat semantic research and explicit `/search`, while preserving the existing authorization, cache, evidence-isolation, and natural-persona behavior.

**Architecture:** Keep the existing `IAgentPublicSearchProvider` contract and add a `ParallelPublicSearchProvider` below `AgentPublicSearchService`. It starts the two existing HTML providers concurrently, applies independent cancellation-aware time budgets and per-provider circuit breakers, and returns a deterministic, URL/title-deduplicated list before the existing `AgentWebResearchService` applies its source-safety and evidence wrapping rules. QChat selects this provider only through a disabled-by-default nested configuration; its browser-agent path stays on the existing serial fallback. SmartWebSearch remains an independently loaded native Alife plugin, with optional non-user-visible loaded/not-loaded diagnostics only.

**Tech Stack:** .NET 9, C#, NUnit, existing `IAgentPublicSearchProvider`, `AgentWebResearchService`, QChat semantic-research pipeline, `TimeProvider`, CodeGraph-guided source navigation.

---

## File structure and responsibility map

| File | Change | Responsibility |
| --- | --- | --- |
| `sources/Alife.Function/Alife.Function.MessageFilter/AgentPublicSearchModels.cs` | Modify | Hold the reusable opt-in multi-source configuration next to the public-search contract. |
| `sources/Alife.Function/Alife.Function.MessageFilter/ParallelPublicSearchProvider.cs` | Create | Parallel provider coordination, cancellation/timeout handling, per-engine circuit breakers, normalization, deduplication, and deterministic result ranking. |
| `sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchModels.cs` | Modify | Expose the multi-source configuration under QChat semantic research with safe defaults. |
| `sources/Alife.Function/Alife.Function.QChat/QChatSmartWebSearchPluginDetector.cs` | Create | Detect only whether a manually loaded native SmartWebSearch assembly is present; it must never call or parse the plugin. |
| `sources/Alife.Function/Alife.Function.QChat/QChatService.cs` | Modify | Construct a persistent parallel research provider only when configured; retain the existing serial provider for browser automation and report optional plugin status through module health. |
| `Tests/Alife.Test.Framework/ParallelPublicSearchProviderTests.cs` | Create | Deterministic unit tests for concurrency, timeout/cancellation, merge/dedup, failure behavior, and circuits. |
| `Tests/Alife.Test.QChat/QChatSemanticWebResearchServiceTests.cs` | Modify | Verify configuration defaults and that research eligibility/evidence isolation continue unchanged with multi-source configuration present. |
| `Tests/Alife.Test.QChat/QChatSmartWebSearchPluginDetectorTests.cs` | Create | Verify a missing or disabled manual plugin detector has no QChat search dependency. |
| `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs` | Modify | Verify explicit `/search` chooses the opt-in coordinator while the default and injected-provider paths remain compatible. |
| `docs/semantic-web-research.md` | Modify | Document enabled configuration, runtime behavior, diagnostic-only plugin status, and no DataAgent dependency. |
| `docs/smart-web-search-plugin.md` | Create | Operator instructions for manually installing/configuring the external plugin without auto-download, textual parsing, or QChat coupling. |

`FallbackPublicSearchProvider.cs`, `DuckDuckGoHtmlSearchProvider.cs`, `BingHtmlSearchProvider.cs`, `AgentWebResearchService.cs`, and `QChatSemanticWebResearchService.cs` remain behaviorally intact except for receiving the coordinator output through their existing interface. In particular, do not change `QChatSemanticWebResearchService.FormatModelPrompt`: it is the existing source-constrained `ExternalContextFormatter.WrapUntrusted` boundary.

### Task 1: Define the opt-in configuration and preserve the existing default search path

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentPublicSearchModels.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchModels.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs:365-375,5675-5719,5897-5902`
- Modify: `Tests/Alife.Test.QChat/QChatSemanticWebResearchServiceTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs:16523-16598`

- [ ] **Step 1: Write the failing configuration/default-path tests.**

  Add these tests before production changes. The first pins all Phase 2 defaults; the second exercises the future QChat injection seam and proves that a caller-provided `IAgentPublicSearchProvider` remains authoritative even when multi-source is enabled.

  ```csharp
  [Test]
  public void MultiSourceSearch_DefaultsToDisabledSafeBuiltInsAndPluginDetection()
  {
      AgentMultiSourceSearchConfig config = new();

      Assert.Multiple(() =>
      {
          Assert.That(config.Enabled, Is.False);
          Assert.That(config.ParallelBuiltInProviders, Is.True);
          Assert.That(config.PerProviderTimeoutMilliseconds, Is.EqualTo(1500));
          Assert.That(config.MaxMergedResults, Is.EqualTo(5));
          Assert.That(config.FailureThreshold, Is.EqualTo(3));
          Assert.That(config.CircuitBreakSeconds, Is.EqualTo(60));
          Assert.That(config.DetectSmartWebSearchPlugin, Is.True);
      });
  }

  [Test]
  public async Task ExplicitSearch_UsesInjectedProviderWhenMultiSourceIsEnabled()
  {
      FakeOneBotRuntime runtime = new();
      FakePublicSearchProvider provider = new(
          new AgentPublicSearchResult("Injected", "https://example.test/injected", "snippet"));
      QChatService service = CreateStartedService(runtime, new QChatConfig
      {
          BotId = 999,
          OwnerId = 1001,
          EnablePublicInternetSearch = true,
          EnableBalancedTextStreaming = false,
          SemanticWebResearch = new QChatSemanticWebResearchConfig
          {
              MultiSourceSearch = new AgentMultiSourceSearchConfig { Enabled = true }
          }
      }, publicSearchProvider: provider);

      runtime.Raise(new OneBotMessageEvent { SelfId = 999, UserId = 1001, RawMessage = "搜一下 injected" });
      await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);

      Assert.Multiple(() =>
      {
          Assert.That(provider.Calls, Is.EqualTo(1));
          Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("https://example.test/injected"));
      });
  }
  ```

- [ ] **Step 2: Run the two new tests and confirm they fail for missing configuration/type support.**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal --filter "FullyQualifiedName~MultiSourceSearch_DefaultsToDisabledSafeBuiltInsAndPluginDetection|FullyQualifiedName~ExplicitSearch_UsesInjectedProviderWhenMultiSourceIsEnabled"
  ```

  Expected: compilation failure because `AgentMultiSourceSearchConfig` and `QChatSemanticWebResearchConfig.MultiSourceSearch` do not yet exist.

- [ ] **Step 3: Add the reusable configuration and QChat nesting.**

  In `AgentPublicSearchModels.cs`, add this class after `AgentPublicSearchConfig`; do not change the `IAgentPublicSearchProvider` method signature or add plugin dependencies.

  ```csharp
  public sealed class AgentMultiSourceSearchConfig
  {
      public bool Enabled { get; set; } = false;
      public bool ParallelBuiltInProviders { get; set; } = true;
      public int PerProviderTimeoutMilliseconds { get; set; } = 1500;
      public int MaxMergedResults { get; set; } = 5;
      public int FailureThreshold { get; set; } = 3;
      public int CircuitBreakSeconds { get; set; } = 60;
      public bool DetectSmartWebSearchPlugin { get; set; } = true;
  }
  ```

  Add `using Alife.Function.Agent;` to `QChatSemanticWebResearchModels.cs`, then add the non-null nested configuration to `QChatSemanticWebResearchConfig`:

  ```csharp
  public AgentMultiSourceSearchConfig MultiSourceSearch { get; set; } = new();
  ```

  In `QChatService`, preserve `resolvedPublicSearchProvider` for browser automation and add a separate field for research:

  ```csharp
  IAgentPublicSearchProvider? resolvedResearchPublicSearchProvider;
  ```

  Change only `ResolvePublicSearchService` to select the injected provider first and otherwise cache a provider constructed for QChat research:

  ```csharp
  IAgentPublicSearchProvider provider = injectedPublicSearchProvider
      ?? (resolvedResearchPublicSearchProvider ??= CreateResearchPublicSearchProvider(config));
  ```

  Add a temporary implementation that delegates to the existing serial construction until Task 3 supplies the parallel class:

  ```csharp
  static IAgentPublicSearchProvider CreateResearchPublicSearchProvider(QChatConfig config) =>
      CreateDefaultPublicSearchProvider();
  ```

  Keep the call at browser-agent line 5770 and `CreateDefaultPublicSearchProvider()` unchanged. This separation prevents semantic research configuration from altering browser-agent navigation behavior.

- [ ] **Step 4: Run the Task 1 tests and the existing semantic service tests.**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal --filter "FullyQualifiedName~MultiSourceSearch_DefaultsToDisabledSafeBuiltInsAndPluginDetection|FullyQualifiedName~ExplicitSearch_UsesInjectedProviderWhenMultiSourceIsEnabled|FullyQualifiedName~QChatSemanticWebResearchServiceTests"
  ```

  Expected: PASS. The test uses an injected provider, so it verifies the configuration is additive and does not require SmartWebSearch, API keys, DataAgent, or a real network request.

- [ ] **Step 5: Commit the configuration seam.**

  ```powershell
  git add sources/Alife.Function/Alife.Function.MessageFilter/AgentPublicSearchModels.cs sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchModels.cs sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatSemanticWebResearchServiceTests.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
  git commit -m "feat(qchat): add opt-in multi-source search configuration"
  ```

### Task 2: Build the deterministic result merger with URL and near-title deduplication

**Files:**

- Create: `sources/Alife.Function/Alife.Function.MessageFilter/ParallelPublicSearchProvider.cs`
- Create: `Tests/Alife.Test.Framework/ParallelPublicSearchProviderTests.cs`

- [ ] **Step 1: Write the failing merge and ranking tests.**

  Make the merger public and pure so it can be tested without HTTP, tasks, or time. Use a source order field solely inside the coordinator; do not alter the public `AgentPublicSearchResult` record, which is already used across Browser and Agent paths.

  ```csharp
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

      Assert.That(merged.Select(item => item.Url), Is.EqualTo(new[]
      {
          "https://example.test/a", "https://example.test/c"
      }));
  }
  ```

- [ ] **Step 2: Run the merge tests and verify they fail because the merger does not exist.**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore -v:minimal --filter "FullyQualifiedName~ParallelPublicSearchProviderTests"
  ```

  Expected: compilation failure for `AgentPublicSearchCandidate` and `AgentPublicSearchResultMerger`.

- [ ] **Step 3: Implement the immutable candidate and pure merger.**

  Create `ParallelPublicSearchProvider.cs` with these public value types before the provider class. Normalize only HTTP(S) URLs; discard invalid/non-HTTP inputs before they can reach QChat evidence. The scoring order is provider order, original result order, normalized title, normalized URL, so it is deterministic without introducing keyword rules.

  ```csharp
  public sealed record AgentPublicSearchCandidate(
      string ProviderId,
      int ProviderOrder,
      int ResultOrder,
      AgentPublicSearchResult Result);

  public static class AgentPublicSearchResultMerger
  {
      public static IReadOnlyList<AgentPublicSearchResult> Merge(
          IEnumerable<AgentPublicSearchCandidate> candidates,
          int maxResults)
      {
          int limit = Math.Clamp(maxResults, 1, 5);
          List<(AgentPublicSearchCandidate Candidate, AgentPublicSearchResult Result, string Url, string Title)> accepted = [];
          foreach (AgentPublicSearchCandidate candidate in candidates
                       .Where(candidate => candidate.Result != null)
                       .OrderBy(candidate => candidate.ProviderOrder)
                       .ThenBy(candidate => candidate.ResultOrder)
                       .ThenBy(candidate => NormalizeTitle(candidate.Result.Title), StringComparer.Ordinal)
                       .ThenBy(candidate => candidate.Result.Url, StringComparer.Ordinal))
          {
              if (TryNormalizeUrl(candidate.Result.Url, out string normalizedUrl) == false)
                  continue;
              string title = NormalizeTitle(candidate.Result.Title);
              if (accepted.Any(item => item.Url == normalizedUrl || IsNearDuplicateTitle(item.Title, title)))
                  continue;

              accepted.Add((candidate, candidate.Result with { Url = normalizedUrl }, normalizedUrl, title));
              if (accepted.Count == limit)
                  break;
          }

          return accepted.Select(item => item.Result).ToArray();
      }

      internal static bool TryNormalizeUrl(string? value, out string normalized)
      {
          normalized = string.Empty;
          if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) == false ||
              (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
              return false;

          UriBuilder builder = new(uri)
          {
              Scheme = uri.Scheme.ToLowerInvariant(),
              Host = uri.Host.ToLowerInvariant(),
              Fragment = string.Empty,
              Port = uri.IsDefaultPort ? -1 : uri.Port
          };
          if (builder.Path.Length > 1)
              builder.Path = builder.Path.TrimEnd('/');
          normalized = builder.Uri.AbsoluteUri.TrimEnd('/');
          return normalized.Length > 0;
      }

      internal static string NormalizeTitle(string? value) => string.Concat((value ?? "")
          .Trim()
          .ToLowerInvariant()
          .Where(character => char.IsLetterOrDigit(character)));

      internal static bool IsNearDuplicateTitle(string left, string right)
      {
          if (left.Length == 0 || right.Length == 0)
              return false;
          if (left == right)
              return true;
          int length = Math.Max(left.Length, right.Length);
          return length >= 5 && LevenshteinDistance(left, right) <= Math.Max(1, length / 7);
      }

      static int LevenshteinDistance(string left, string right)
      {
          int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
          for (int row = 1; row <= left.Length; row++)
          {
              int[] current = new int[right.Length + 1];
              current[0] = row;
              for (int column = 1; column <= right.Length; column++)
                  current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1));
              previous = current;
          }
          return previous[right.Length];
      }
  }
  ```

  Add the needed `System`, `System.Collections.Generic`, and `System.Linq` usings. Keep this merger free of `AgentWebResearchService`, `QChatService`, `DataAgent`, plugin types, HTTP, and user-facing strings.

- [ ] **Step 4: Run the merger tests and the current public-search parser/fallback tests.**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore -v:minimal --filter "FullyQualifiedName~ParallelPublicSearchProviderTests|FullyQualifiedName~AgentPublicSearchServiceTests"
  ```

  Expected: PASS. Existing DuckDuckGo/Bing parser tests and `FallbackPublicSearchProvider` tests must remain green.

- [ ] **Step 5: Commit deterministic merge behavior.**

  ```powershell
  git add sources/Alife.Function/Alife.Function.MessageFilter/ParallelPublicSearchProvider.cs Tests/Alife.Test.Framework/ParallelPublicSearchProviderTests.cs
  git commit -m "feat(search): add deterministic multi-source result merge"
  ```

### Task 3: Add actual parallel execution, per-provider time budgets, cancellation, and circuits

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/ParallelPublicSearchProvider.cs`
- Modify: `Tests/Alife.Test.Framework/ParallelPublicSearchProviderTests.cs`

- [ ] **Step 1: Write failing tests for concurrent start, fast completion, one/both failure, cancellation, circuit opening, and circuit recovery.**

  Add a test-only `GateProvider` that exposes `Started`, `CancellationObserved`, `Release`, and `Calls`; it must wait with the received `CancellationToken`. Add a `MutableTimeProvider` whose `GetUtcNow()` returns an assignable `DateTimeOffset`.

  ```csharp
  [Test]
  public async Task SearchAsync_StartsBothProvidersAndReturnsFastEvidenceWithoutWaitingForSlowPeer()
  {
      GateProvider slow = new(waitForRelease: true);
      GateProvider fast = new([new AgentPublicSearchResult("Fast", "https://example.test/fast", "fast")]);
      fast.BeforeReturn = slow.WaitForStartAsync;
      ParallelPublicSearchProvider provider = CreateProvider(slow, fast);

      IReadOnlyList<AgentPublicSearchResult> results = await provider.SearchAsync("latest test", 5)
          .WaitAsync(TimeSpan.FromMilliseconds(250));

      await slow.WaitForCancellationAsync();
      Assert.Multiple(() =>
      {
          Assert.That(slow.Calls, Is.EqualTo(1));
          Assert.That(fast.Calls, Is.EqualTo(1));
          Assert.That(results.Single().Url, Is.EqualTo("https://example.test/fast"));
      });
  }

  [Test]
  public async Task SearchAsync_OneFailureReturnsOtherProviderEvidence()
  {
      ParallelPublicSearchProvider provider = CreateProvider(
          new GateProvider(new InvalidOperationException("duck failed")),
          new GateProvider([new AgentPublicSearchResult("Bing", "https://example.test/bing", "ok")]));

      IReadOnlyList<AgentPublicSearchResult> results = await provider.SearchAsync("query", 5);

      Assert.That(results.Single().Title, Is.EqualTo("Bing"));
  }

  [Test]
  public void SearchAsync_BothProvidersFail_ThrowsSearchFailureWithoutEvidence()
  {
      ParallelPublicSearchProvider provider = CreateProvider(
          new GateProvider(new InvalidOperationException("duck failed")),
          new GateProvider(new InvalidOperationException("bing failed")));

      Assert.That(async () => await provider.SearchAsync("query", 5),
          Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("public_search_all_providers_failed"));
  }

  [Test]
  public async Task SearchAsync_CallerCancellationCancelsBothProviders()
  {
      GateProvider duck = new(waitForRelease: true);
      GateProvider bing = new(waitForRelease: true);
      ParallelPublicSearchProvider provider = CreateProvider(duck, bing);
      using CancellationTokenSource cancellation = new();
      Task<IReadOnlyList<AgentPublicSearchResult>> task = provider.SearchAsync("query", 5, cancellation.Token);

      await Task.WhenAll(duck.WaitForStartAsync(), bing.WaitForStartAsync());
      cancellation.Cancel();

      Assert.That(async () => await task, Throws.InstanceOf<OperationCanceledException>());
      await Task.WhenAll(duck.WaitForCancellationAsync(), bing.WaitForCancellationAsync());
  }

  [Test]
  public async Task SearchAsync_OpensOnlyFailingProviderCircuitThenRetriesAfterConfiguredWindow()
  {
      MutableTimeProvider clock = new(DateTimeOffset.Parse("2026-07-19T00:00:00Z"));
      GateProvider failingDuck = new(new InvalidOperationException("duck failed"));
      GateProvider healthyBing = new([new AgentPublicSearchResult("Bing", "https://example.test/bing", "ok")]);
      ParallelPublicSearchProvider provider = CreateProvider(failingDuck, healthyBing, clock, failureThreshold: 3, circuitBreakSeconds: 60);

      await provider.SearchAsync("one", 5);
      await provider.SearchAsync("two", 5);
      await provider.SearchAsync("three", 5);
      await provider.SearchAsync("four", 5);
      clock.Advance(TimeSpan.FromSeconds(61));
      await provider.SearchAsync("five", 5);

      Assert.That(failingDuck.Calls, Is.EqualTo(4));
      Assert.That(healthyBing.Calls, Is.EqualTo(5));
  }
  ```

  Also add a timeout test with a permanently gated provider and `PerProviderTimeoutMilliseconds = 20`, asserting that the quick peer still supplies evidence and that the timed-out provider increments its circuit only once. Keep the existing cache test at `AgentWebResearchServiceTests.ResearchAsync_ReusesCachedResultBeforeSearchingAgain`; add an assertion there that the underlying provider call count stays one after the second request.

- [ ] **Step 2: Run the new provider tests and confirm they fail because only the merger exists.**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore -v:minimal --filter "FullyQualifiedName~ParallelPublicSearchProviderTests"
  ```

  Expected: compilation failure because `ParallelPublicSearchProvider` and the test helpers have not yet been implemented.

- [ ] **Step 3: Implement the provider using two named slots and an injected clock.**

  Add the following shape below the Task 2 merger. Do not change the `IAgentPublicSearchProvider` interface and do not use `Task.Run`; calling `SearchAsync` once per provider before the first `await` is what makes the two actual HTTP providers concurrent.

  ```csharp
  public sealed class ParallelPublicSearchProvider : IAgentPublicSearchProvider
  {
      readonly ProviderSlot[] slots;
      readonly AgentMultiSourceSearchConfig config;
      readonly TimeProvider timeProvider;
      readonly ConcurrentDictionary<string, ProviderCircuit> circuits = new(StringComparer.Ordinal);

      public ParallelPublicSearchProvider(
          IAgentPublicSearchProvider duckDuckGo,
          IAgentPublicSearchProvider bing,
          AgentMultiSourceSearchConfig config,
          TimeProvider? timeProvider = null)
      {
          slots = [new("duckduckgo", 0, duckDuckGo), new("bing", 1, bing)];
          this.config = config ?? throw new ArgumentNullException(nameof(config));
          this.timeProvider = timeProvider ?? TimeProvider.System;
      }

      public async Task<IReadOnlyList<AgentPublicSearchResult>> SearchAsync(
          string query,
          int maxResults,
          CancellationToken cancellationToken = default)
      {
          cancellationToken.ThrowIfCancellationRequested();
          using CancellationTokenSource stopPeers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
          ProviderSlot[] available = slots.Where(slot => IsCircuitClosed(slot.Id)).ToArray();
          if (available.Length == 0)
              throw new InvalidOperationException("public_search_all_providers_failed");

          List<Task<ProviderAttempt>> pending = available
              .Select(slot => ExecuteSlotAsync(slot, query, maxResults, cancellationToken, stopPeers.Token))
              .ToList();
          List<ProviderAttempt> completed = [];
          try
          {
              while (pending.Count > 0)
              {
                  Task<ProviderAttempt> next = await Task.WhenAny(pending);
                  pending.Remove(next);
                  ProviderAttempt attempt = await next;
                  completed.Add(attempt);
                  if (attempt.Results.Count == 0)
                      continue;

                  completed.AddRange(pending.Where(task => task.IsCompletedSuccessfully).Select(task => task.Result));
                  stopPeers.Cancel();
                  return AgentPublicSearchResultMerger.Merge(
                      completed.Where(item => item.Results.Count > 0)
                          .SelectMany(item => item.Results.Select((result, index) => new AgentPublicSearchCandidate(item.ProviderId, item.ProviderOrder, index, result))),
                      Math.Min(Math.Max(1, maxResults), Math.Clamp(config.MaxMergedResults, 1, 5)));
              }
          }
          finally
          {
              stopPeers.Cancel();
          }

          if (completed.Any(item => item.CompletedNormally))
              return [];
          throw new InvalidOperationException("public_search_all_providers_failed");
      }
  }
  ```

  Define the internal support types in the same class so no caller observes engine-specific state or exception text:

  ```csharp
  sealed record ProviderSlot(string Id, int Order, IAgentPublicSearchProvider Provider);

  sealed class ProviderCircuit
  {
      public object Gate { get; } = new();
      public int ConsecutiveFailures { get; set; }
      public DateTimeOffset? OpenUntil { get; set; }
  }

  sealed record ProviderAttempt(
      string ProviderId,
      int ProviderOrder,
      IReadOnlyList<AgentPublicSearchResult> Results,
      bool CompletedNormally)
  {
      public static ProviderAttempt Failed(string id, int order) => new(id, order, [], false);
      public static ProviderAttempt Cancelled(string id, int order) => new(id, order, [], false);
  }
  ```

  Implement `ExecuteSlotAsync` with a linked token per slot, `CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, config.PerProviderTimeoutMilliseconds)))`, and these exact branches:

  ```csharp
  catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
  {
      throw;
  }
  catch (OperationCanceledException) when (peerStopToken.IsCancellationRequested && deadlineCancellation.IsCancellationRequested == false)
  {
      return ProviderAttempt.Cancelled(slot.Id, slot.Order);
  }
  catch (Exception)
  {
      RecordFailure(slot.Id);
      return ProviderAttempt.Failed(slot.Id, slot.Order);
  }
  ```

  The complete helper methods are:

  ```csharp
  async Task<ProviderAttempt> ExecuteSlotAsync(
      ProviderSlot slot,
      string query,
      int maxResults,
      CancellationToken callerCancellationToken,
      CancellationToken peerStopToken)
  {
      using CancellationTokenSource deadlineCancellation = new();
      deadlineCancellation.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, config.PerProviderTimeoutMilliseconds)));
      using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
          callerCancellationToken,
          peerStopToken,
          deadlineCancellation.Token);
      try
      {
          IReadOnlyList<AgentPublicSearchResult> results = await slot.Provider.SearchAsync(
              query,
              Math.Min(Math.Max(1, maxResults), Math.Clamp(config.MaxMergedResults, 1, 5)),
              timeout.Token);
          RecordSuccess(slot.Id);
          return new ProviderAttempt(slot.Id, slot.Order, results ?? [], true);
      }
      catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
      {
          throw;
      }
      catch (OperationCanceledException) when (peerStopToken.IsCancellationRequested && deadlineCancellation.IsCancellationRequested == false)
      {
          return ProviderAttempt.Cancelled(slot.Id, slot.Order);
      }
      catch (Exception)
      {
          RecordFailure(slot.Id);
          return ProviderAttempt.Failed(slot.Id, slot.Order);
      }
  }

  bool IsCircuitClosed(string providerId)
  {
      ProviderCircuit circuit = circuits.GetOrAdd(providerId, _ => new ProviderCircuit());
      lock (circuit.Gate)
      {
          if (circuit.OpenUntil is not { } openUntil)
              return true;
          if (openUntil > timeProvider.GetUtcNow())
              return false;
          circuit.OpenUntil = null;
          circuit.ConsecutiveFailures = 0;
          return true;
      }
  }

  void RecordSuccess(string providerId)
  {
      ProviderCircuit circuit = circuits.GetOrAdd(providerId, _ => new ProviderCircuit());
      lock (circuit.Gate)
      {
          circuit.ConsecutiveFailures = 0;
          circuit.OpenUntil = null;
      }
  }

  void RecordFailure(string providerId)
  {
      ProviderCircuit circuit = circuits.GetOrAdd(providerId, _ => new ProviderCircuit());
      lock (circuit.Gate)
      {
          circuit.ConsecutiveFailures++;
          if (circuit.ConsecutiveFailures < Math.Max(1, config.FailureThreshold))
              return;
          circuit.OpenUntil = timeProvider.GetUtcNow().AddSeconds(Math.Max(1, config.CircuitBreakSeconds));
      }
  }
  ```

  A successful non-empty response calls `RecordSuccess(slot.Id)`; a successful empty response is `CompletedNormally` but does not end the other engine early. `IsCircuitClosed` uses `timeProvider.GetUtcNow()`, skips a slot until `OpenUntil`, and clears its failure count when that window expires. `RecordFailure` opens that one slot only when its consecutive failure count reaches `Math.Max(1, config.FailureThreshold)`, for `Math.Max(1, config.CircuitBreakSeconds)` seconds. Peer cancellation after a winner is neither a success nor a failure. Add `using System.Collections.Concurrent;` and never share or dispose the injected provider instances.

- [ ] **Step 4: Run the complete parallel-provider and research cache test set.**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore -v:minimal --filter "FullyQualifiedName~ParallelPublicSearchProviderTests|FullyQualifiedName~ResearchAsync_ReusesCachedResultBeforeSearchingAgain"
  ```

  Expected: PASS. The test suite proves parallel start, no wait for a slow peer, one/both failure behavior, caller cancellation, timeout fallback, deterministic merge, cache reuse, and open/recovering per-engine circuits without external network access.

- [ ] **Step 5: Commit the coordinator.**

  ```powershell
  git add sources/Alife.Function/Alife.Function.MessageFilter/ParallelPublicSearchProvider.cs Tests/Alife.Test.Framework/ParallelPublicSearchProviderTests.cs Tests/Alife.Test.Framework/AgentWebResearchServiceTests.cs
  git commit -m "feat(search): run built-in providers in parallel with circuits"
  ```

### Task 4: Wire the configured coordinator into QChat research and explicit `/search`

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs:5675-5719,5897-5902`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatSemanticWebResearchServiceTests.cs`

- [ ] **Step 1: Write the failing QChat integration tests.**

  Add an explicit-search test that uses `QChatService` with no injected provider and an injected **multi-source provider factory**. The factory constructs two deterministic fake providers, not HTTP clients. Add the factory parameter to the existing optional-injection tail of `QChatService`’s primary constructor and to the `CreateStartedService` test helper; this keeps the normal runtime construction path unchanged. Also add a semantic-research test that asserts the `AgentWebResearchRequest` still has the existing owner/group access settings and its model prompt only contains evidence URLs.

  ```csharp
  [Test]
  public async Task ExplicitSearch_WhenMultiSourceEnabled_UsesCoordinatorAndCapsEvidence()
  {
      FakeOneBotRuntime runtime = new();
      CountingSearchProvider duck = new("duckduckgo", Enumerable.Range(1, 4)
          .Select(i => new AgentPublicSearchResult($"Duck {i}", $"https://duck.example/{i}", "snippet")));
      CountingSearchProvider bing = new("bing", [
          new AgentPublicSearchResult("Duck 1 mirror", "https://duck.example/1#mirror", "duplicate"),
          new AgentPublicSearchResult("Bing unique", "https://bing.example/unique", "unique")
      ]);
      QChatService service = CreateStartedService(
          runtime,
          CreateResearchConfig(multiSourceEnabled: true),
          multiSourcePublicSearchProviderFactory: _ =>
          new ParallelPublicSearchProvider(duck, bing, new AgentMultiSourceSearchConfig { Enabled = true, MaxMergedResults = 2 });

      runtime.Raise(new OneBotMessageEvent { SelfId = 999, UserId = 1001, RawMessage = "搜一下 current topic" });
      await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);

      Assert.Multiple(() =>
      {
          Assert.That(duck.Calls, Is.EqualTo(1));
          Assert.That(bing.Calls, Is.EqualTo(1));
          Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("https://duck.example/1"));
          Assert.That(runtime.PrivateMessages.Single().Message, Does.Not.Contain("https://duck.example/3"));
      });
  }
  ```

  Add this self-contained test helper in `QChatServiceAdapterTests` beside the existing `FakePublicSearchProvider`:

  ```csharp
  sealed class CountingSearchProvider(string id, IEnumerable<AgentPublicSearchResult> results) : IAgentPublicSearchProvider
  {
      readonly AgentPublicSearchResult[] results = results.ToArray();
      public string Id { get; } = id;
      public int Calls { get; private set; }

      public Task<IReadOnlyList<AgentPublicSearchResult>> SearchAsync(
          string query,
          int maxResults,
          CancellationToken cancellationToken = default)
      {
          Calls++;
          return Task.FromResult<IReadOnlyList<AgentPublicSearchResult>>(results);
      }
  }

  static QChatConfig CreateResearchConfig(bool multiSourceEnabled) => new()
  {
      BotId = 999,
      OwnerId = 1001,
      EnablePublicInternetSearch = true,
      EnableBalancedTextStreaming = false,
      SemanticWebResearch = new QChatSemanticWebResearchConfig
      {
          MultiSourceSearch = new AgentMultiSourceSearchConfig { Enabled = multiSourceEnabled }
      }
  };
  ```

- [ ] **Step 2: Run the new integration tests and confirm the coordinator is not selected yet.**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal --filter "FullyQualifiedName~ExplicitSearch_WhenMultiSourceEnabled_UsesCoordinatorAndCapsEvidence|FullyQualifiedName~QChatSemanticWebResearchServiceTests"
  ```

  Expected: FAIL because the Task 1 temporary implementation always returns the serial fallback, or compile failure until the selected test seam exists.

- [ ] **Step 3: Replace the temporary research factory with the configured built-in provider construction.**

  Extend the existing primary `QChatService` constructor with this optional tail parameter and field, then forward the matching optional parameter through `CreateStartedService`:

  ```csharp
  Func<AgentMultiSourceSearchConfig, IAgentPublicSearchProvider>? multiSourcePublicSearchProviderFactory = null)

  readonly Func<AgentMultiSourceSearchConfig, IAgentPublicSearchProvider>? injectedMultiSourcePublicSearchProviderFactory =
      multiSourcePublicSearchProviderFactory;
  ```

  Leave the existing default factory exactly for browser automation:

  ```csharp
  static IAgentPublicSearchProvider CreateDefaultPublicSearchProvider() =>
      new FallbackPublicSearchProvider(
          new DuckDuckGoHtmlSearchProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(8) }),
          new BingHtmlSearchProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(8) }));
  ```

  Implement the new instance research-only factory as follows:

  ```csharp
  IAgentPublicSearchProvider CreateResearchPublicSearchProvider(QChatConfig config)
  {
      AgentMultiSourceSearchConfig multiSource = config.SemanticWebResearch.MultiSourceSearch ?? new AgentMultiSourceSearchConfig();
      if (multiSource.Enabled == false || multiSource.ParallelBuiltInProviders == false)
          return CreateDefaultPublicSearchProvider();

      if (injectedMultiSourcePublicSearchProviderFactory != null)
          return injectedMultiSourcePublicSearchProviderFactory(multiSource);

      return new ParallelPublicSearchProvider(
          new DuckDuckGoHtmlSearchProvider(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }),
          new BingHtmlSearchProvider(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }),
          multiSource);
  }
  ```

  `ResolvePublicSearchService` remains the only caller of `CreateResearchPublicSearchProvider`; it serves both semantic research and the existing explicit `/search` command. It already feeds `AgentPublicSearchService`, `AgentWebResearchService`, and `QChatSemanticWebResearchService`, so the existing untrusted-context wrapper, group/owner gating, cache, source filtering, delayed natural feedback, and model-prompt source restrictions remain intact. Do not add DataAgent, engine labels, plugin text, or hard-coded QQ progress/failure messages anywhere in this path.

- [ ] **Step 4: Run QChat semantic and explicit-search regression tests.**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal --filter "FullyQualifiedName~SemanticWebResearch|FullyQualifiedName~WebResearch|FullyQualifiedName~ExplicitSearch_WhenMultiSourceEnabled_UsesCoordinatorAndCapsEvidence"
  ```

  Expected: PASS. This includes owner private, mentioned-group, unmentioned-group, fast/slow feedback, cancellation, and evidence-only input regressions already present in `QChatServiceAdapterTests`.

- [ ] **Step 5: Commit QChat wiring.**

  ```powershell
  git add sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs Tests/Alife.Test.QChat/QChatSemanticWebResearchServiceTests.cs
  git commit -m "feat(qchat): use parallel providers for configured research"
  ```

### Task 5: Add manual SmartWebSearch presence diagnostics without coupling it to QChat evidence

**Files:**

- Create: `sources/Alife.Function/Alife.Function.QChat/QChatSmartWebSearchPluginDetector.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs:2516-2525`
- Create: `Tests/Alife.Test.QChat/QChatSmartWebSearchPluginDetectorTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Write the failing detector and health-status tests.**

  The detector accepts assembly simple names for unit tests; its runtime overload reads `AppDomain.CurrentDomain.GetAssemblies()`. It must not load an assembly, read plugin configuration, reference a plugin type, invoke `[XmlFunction]`, or alter the search provider choice.

  ```csharp
  [Test]
  public void Detect_WhenDisabled_DoesNotClaimPluginIsLoaded()
  {
      QChatSmartWebSearchPluginStatus status = QChatSmartWebSearchPluginDetector.Detect(
          enabled: false,
          assemblyNames: ["Alife.Plugin.SmartWebSearch"]);

      Assert.Multiple(() =>
      {
          Assert.That(status.DetectionEnabled, Is.False);
          Assert.That(status.IsLoaded, Is.False);
          Assert.That(status.Code, Is.EqualTo("disabled"));
      });
  }

  [Test]
  public void Detect_WhenPluginIsAbsent_RemainsInformationalAndSearchIndependent()
  {
      QChatSmartWebSearchPluginStatus status = QChatSmartWebSearchPluginDetector.Detect(
          enabled: true,
          assemblyNames: []);

      Assert.Multiple(() =>
      {
          Assert.That(status.DetectionEnabled, Is.True);
          Assert.That(status.IsLoaded, Is.False);
          Assert.That(status.Code, Is.EqualTo("not_loaded"));
          Assert.That(status.Description, Does.Contain("QChat"));
      });
  }
  ```

  Add a QChat health test that supplies `SemanticWebResearch.MultiSourceSearch.DetectSmartWebSearchPlugin = true`, asserts that `GetHealth()` remains based on OneBot connection state, and only checks an informational `smart-web-search=not_loaded` suffix. Do not make `ModuleHealthStatus` degrade merely because the plugin is absent or unconfigured.

- [ ] **Step 2: Run the tests and confirm they fail because the detector/status model is absent.**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal --filter "FullyQualifiedName~QChatSmartWebSearchPluginDetectorTests|FullyQualifiedName~SmartWebSearch"
  ```

  Expected: compilation failure for `QChatSmartWebSearchPluginDetector` and `QChatSmartWebSearchPluginStatus`.

- [ ] **Step 3: Implement pure presence detection and append only its diagnostic status to module health.**

  Create the detector with no project reference to the remote repository:

  ```csharp
  public sealed record QChatSmartWebSearchPluginStatus(
      bool DetectionEnabled,
      bool IsLoaded,
      string Code,
      string Description);

  public static class QChatSmartWebSearchPluginDetector
  {
      const string PluginAssemblyName = "Alife.Plugin.SmartWebSearch";

      public static QChatSmartWebSearchPluginStatus Detect(bool enabled, IEnumerable<string> assemblyNames)
      {
          if (enabled == false)
              return new(false, false, "disabled", "SmartWebSearch detection is disabled; QChat structured research is independent.");

          bool loaded = assemblyNames.Any(name => string.Equals(name, PluginAssemblyName, StringComparison.OrdinalIgnoreCase));
          return loaded
              ? new(true, true, "loaded", "SmartWebSearch native plugin is loaded; its credentials and XML tools remain plugin-managed.")
              : new(true, false, "not_loaded", "SmartWebSearch is not loaded; QChat continues with built-in structured providers.");
      }

      public static QChatSmartWebSearchPluginStatus Detect(bool enabled) => Detect(
          enabled,
          AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name ?? ""));
  }
  ```

  In `GetHealth()`, compute this detector only when `Configuration.SemanticWebResearch.MultiSourceSearch.DetectSmartWebSearchPlugin` is true and append `smart-web-search={status.Code}` to the message. Preserve the existing `Healthy`, `Degraded`, and `Unavailable` status selection exactly. This is a diagnostics/health result, never a QQ response string.

- [ ] **Step 4: Run detector, QChat health, and research regression tests.**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal --filter "FullyQualifiedName~QChatSmartWebSearchPluginDetectorTests|FullyQualifiedName~SmartWebSearch|FullyQualifiedName~SemanticWebResearch"
  ```

  Expected: PASS. A missing plugin/API key cannot block, slow, or change the QChat structured research path.

- [ ] **Step 5: Commit the optional detector.**

  ```powershell
  git add sources/Alife.Function/Alife.Function.QChat/QChatSmartWebSearchPluginDetector.cs sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatSmartWebSearchPluginDetectorTests.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
  git commit -m "feat(qchat): report optional SmartWebSearch plugin presence"
  ```

### Task 6: Document operational behavior and perform full regression verification

**Files:**

- Modify: `docs/semantic-web-research.md`
- Create: `docs/smart-web-search-plugin.md`
- Verify: `Tests/Alife.Test.Framework/Alife.Test.Framework.csproj`
- Verify: `Tests/Alife.Test.QChat/Alife.Test.QChat.csproj`

- [ ] **Step 1: Write/update the operator documentation.**

  Add this configuration example to `docs/semantic-web-research.md`; the defaults must be shown as disabled and no secret must appear in a repository file.

  ```json
  {
    "semanticWebResearch": {
      "enabled": true,
      "multiSourceSearch": {
        "enabled": true,
        "parallelBuiltInProviders": true,
        "perProviderTimeoutMilliseconds": 1500,
        "maxMergedResults": 5,
        "failureThreshold": 3,
        "circuitBreakSeconds": 60,
        "detectSmartWebSearchPlugin": true
      }
    }
  }
  ```

  Explain that semantic routing remains semantic rather than keyword-triggered; group research requires an explicit bot mention; only structured URL evidence is wrapped as untrusted context; `AgentWebResearchControlState` cache is consulted before providers; and DataAgent is not on this path.

  Create `docs/smart-web-search-plugin.md` with these mandatory sections: remote plugin capabilities (`Search`, `SmartSummary`, `SmartChatSearch`, `HotSearch`); manual native-package installation and normal Alife module reload; Tavily/Baidu credentials are configured in the plugin itself and never committed; QChat does not parse `Poke(...)`/Markdown or call its XML functions; absence/unconfigured credentials leave QChat on built-in providers; and no auto-download/update or proactive/scheduled search exists.

- [ ] **Step 2: Run whitespace and scope checks before final tests.**

  Run:

  ```powershell
  git diff --check
  rg -n --ignore-case "[T]ODO|[T]BD|implement later|fill in details" docs/semantic-web-research.md docs/smart-web-search-plugin.md sources/Alife.Function/Alife.Function.MessageFilter/ParallelPublicSearchProvider.cs
  rg -n "DataAgent" sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchModels.cs sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchService.cs sources/Alife.Function/Alife.Function.QChat/QChatSmartWebSearchPluginDetector.cs
  ```

  Expected: `git diff --check` succeeds; placeholder scan has no matches; the focused semantic-research and plugin detector files have no `DataAgent` match. A nonzero exit from either `rg` command is expected when there are no matches.

- [ ] **Step 3: Run framework and full QChat regressions.**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore -v:minimal
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal
  ```

  Expected: both projects pass with no failures. If the full host solution fails due to already-running `Alife.Client` processes locking `Outputs\\Alife.Client`, report that environmental lock separately; do not terminate user processes and do not call it a Phase 2 failure.

- [ ] **Step 4: Review the completed diff against the approved design.**

  Verify every approved rule against the changed paths: disabled default; parallel DDG/Bing only in configured QChat research/`/search`; individual timeout/failure/circuit behavior; deterministic cap/dedup; cache reuse; untrusted evidence wrapper retained; natural delayed feedback retained; manual plugin only; no keys/auto-download/text parsing; no DataAgent; no proactive push. Use `git diff --check`, `git status --short`, and CodeGraph’s affected-test view if its worktree index has been initialized/synced.

- [ ] **Step 5: Commit documentation and verification-ready change set.**

  ```powershell
  git add docs/semantic-web-research.md docs/smart-web-search-plugin.md
  git commit -m "docs: describe QChat multi-source web research"
  git status --short
  ```

  Expected: clean feature worktree after the documentation commit. Do not merge into `master`, alter the user’s dirty main worktree, download the third-party plugin, or stop running Alife processes during this plan.
