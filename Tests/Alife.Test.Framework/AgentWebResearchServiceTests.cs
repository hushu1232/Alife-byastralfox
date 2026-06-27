using Alife.Function.Agent;
using NUnit.Framework;
using System.IO;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentWebResearchServiceTests
{
    [Test]
    public async Task ResearchAsync_WhenQueryIsEmpty_ReturnsReadableFallbackWithoutMojibake()
    {
        AgentWebResearchService service = new();
        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "   ",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("empty_query"));
            Assert.That(result.Answer, Is.EqualTo("\u6ca1\u67e5\u5230\u53ef\u9760\u6765\u6e90\u3002"));
            Assert.That(result.Answer, Does.Not.Contain("\u6fde"));
            Assert.That(result.Answer, Does.Not.Contain("\u951f"));
        });
    }

    [Test]
    public async Task ResearchAsync_OwnerSearchesAndReadsTopResult()
    {
        FakePublicSearchService search = new([
            new AgentPublicSearchResult("Agent Browser", "https://example.com/agent-browser", "browser snippet")
        ]);
        FakeInternetService internet = new(new AgentInternetFetchResult(
            true,
            "ok",
            "[UNTRUSTED EXTERNAL CONTEXT: internet-page]\nAgent browser read content from the page."));
        AgentWebAccessService webAccess = new(internetService: internet);
        AgentWebResearchService service = new(search, webAccess);

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "  agent browser web access  ",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                AllowGroupMemberPublicSearch = true,
                EnableAutoRead = true,
                EnablePublicFetch = true,
                EnableBrowserSnapshot = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(search.Calls, Is.EqualTo(1));
            Assert.That(search.LastQuery, Is.EqualTo("agent browser web access"));
            Assert.That(internet.Calls, Is.EqualTo(1));
            Assert.That(internet.LastUrl, Is.EqualTo("https://example.com/agent-browser"));
            Assert.That(result.Evidence, Has.Count.EqualTo(1));
            Assert.That(result.Evidence[0].Summary, Does.Contain("Agent browser read content"));
            Assert.That(result.Answer, Does.Contain("Agent Browser"));
            Assert.That(result.Answer, Does.Contain("\u6765\u6e90"));
        });
    }

    [Test]
    public async Task ResearchAsync_GroupMemberUsesSearchEvidenceWithoutReadingPages()
    {
        FakePublicSearchService search = new([
            new AgentPublicSearchResult("Public Result", "https://example.com/public", "public search snippet")
        ]);
        FakeInternetService internet = new(new AgentInternetFetchResult(true, "ok", "should not be read"));
        AgentWebResearchService service = new(search, new AgentWebAccessService(internetService: internet));

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "public topic",
            AgentWebAccessActorRole.GroupMember,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                AllowGroupMemberPublicSearch = true,
                EnablePublicFetch = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(search.Calls, Is.EqualTo(1));
            Assert.That(internet.Calls, Is.Zero);
            Assert.That(result.Evidence[0].Summary, Is.EqualTo("public search snippet"));
            Assert.That(result.Answer, Does.Contain("Public Result"));
        });
    }

    [Test]
    public async Task ResearchAsync_NoSearchResultsDoesNotFabricate()
    {
        AgentWebResearchService service = new(new FakePublicSearchService([]), new AgentWebAccessService());

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "missing topic",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                EnableAutoRead = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("no_results"));
            Assert.That(result.Evidence, Is.Empty);
            Assert.That(result.Answer, Does.Contain("\u6ca1\u67e5\u5230"));
        });
    }

    [Test]
    public async Task ResearchAsync_PrivateOrUnsafeSearchResultIsSkipped()
    {
        FakePublicSearchService search = new([
            new AgentPublicSearchResult("Local", "http://127.0.0.1:3000", "private snippet"),
            new AgentPublicSearchResult("Public", "https://example.com/public", "public snippet")
        ]);
        FakeInternetService internet = new(new AgentInternetFetchResult(true, "ok", "public page content"));
        AgentWebResearchService service = new(search, new AgentWebAccessService(internetService: internet));

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "mixed topic",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                EnableAutoRead = true,
                EnablePublicFetch = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(internet.Calls, Is.EqualTo(1));
            Assert.That(internet.LastUrl, Is.EqualTo("https://example.com/public"));
            Assert.That(result.Evidence.Single().Url, Is.EqualTo("https://example.com/public"));
        });
    }

    [Test]
    public async Task ResearchAsync_OwnerExpandsQueryWhenOriginalHasNoUsablePublicResults()
    {
        FakePublicSearchService search = new(new Dictionary<string, IReadOnlyList<AgentPublicSearchResult>>
        {
            ["dotnet 9"] =
            [
                new AgentPublicSearchResult("Local", "http://127.0.0.1:8080/private", "unsafe local result")
            ],
            ["official docs dotnet 9"] =
            [
                new AgentPublicSearchResult("Microsoft Docs", "https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9", "official docs result")
            ]
        });
        FakeInternetService internet = new(new AgentInternetFetchResult(true, "ok", "official page content"));
        AgentWebResearchService service = new(search, new AgentWebAccessService(internetService: internet));

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "dotnet 9",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                EnableAutoRead = true,
                EnablePublicFetch = true
            },
            MaxSources: 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(search.Queries, Does.Contain("dotnet 9"));
            Assert.That(search.Queries, Does.Contain("official docs dotnet 9"));
            Assert.That(result.Evidence.Single().Title, Is.EqualTo("Microsoft Docs"));
            Assert.That(internet.LastUrl, Is.EqualTo("https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9"));
        });
    }

    [Test]
    public async Task ResearchAsync_OwnerUsesFreshnessAwareExpansionForLatestRequests()
    {
        FakePublicSearchService search = new(new Dictionary<string, IReadOnlyList<AgentPublicSearchResult>>
        {
            ["dotnet 10 \u6700\u65b0\u53d1\u5e03\u65e5\u671f"] =
            [
                new AgentPublicSearchResult("Local", "http://127.0.0.1:8080/private", "unsafe local result")
            ],
            ["dotnet 10 \u6700\u65b0\u53d1\u5e03\u65e5\u671f latest release notes"] =
            [
                new AgentPublicSearchResult("Microsoft Release Notes", "https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10", "fresh official release notes")
            ],
            ["official docs dotnet 10 \u6700\u65b0\u53d1\u5e03\u65e5\u671f"] =
            [
                new AgentPublicSearchResult("Should Not Need Generic Docs", "https://docs.example.com/extra", "extra search would waste tokens")
            ]
        });
        FakeInternetService internet = new(new AgentInternetFetchResult(true, "ok", "fresh release note content"));
        AgentWebResearchService service = new(search, new AgentWebAccessService(internetService: internet));

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "dotnet 10 \u6700\u65b0\u53d1\u5e03\u65e5\u671f",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                EnableAutoRead = true,
                EnablePublicFetch = true
            },
            MaxSources: 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(search.Queries, Is.EqualTo(new[]
            {
                "dotnet 10 \u6700\u65b0\u53d1\u5e03\u65e5\u671f",
                "dotnet 10 \u6700\u65b0\u53d1\u5e03\u65e5\u671f latest release notes"
            }));
            Assert.That(result.Evidence.Single().Title, Is.EqualTo("Microsoft Release Notes"));
        });
    }

    [Test]
    public async Task ResearchAsync_OwnerUsesExactErrorExpansionBeforeGenericFallback()
    {
        FakePublicSearchService search = new(new Dictionary<string, IReadOnlyList<AgentPublicSearchResult>>
        {
            ["鎶ラ敊 HTTP 429 Too Many Requests retry-after"] =
            [
                new AgentPublicSearchResult("Local", "http://127.0.0.1:8080/error", "unsafe local result")
            ],
            ["\"HTTP 429 Too Many Requests\" retry-after"] =
            [
                new AgentPublicSearchResult("MDN 429", "https://developer.mozilla.org/docs/Web/HTTP/Status/429", "exact error result")
            ],
            ["official docs 鎶ラ敊 HTTP 429 Too Many Requests retry-after"] =
            [
                new AgentPublicSearchResult("Should Not Need Generic Docs", "https://docs.example.com/extra", "extra search would waste tokens")
            ]
        });
        FakeInternetService internet = new(new AgentInternetFetchResult(true, "ok", "exact error page content"));
        AgentWebResearchService service = new(search, new AgentWebAccessService(internetService: internet));

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "鎶ラ敊 HTTP 429 Too Many Requests retry-after",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                EnableAutoRead = true,
                EnablePublicFetch = true
            },
            MaxSources: 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(search.Queries, Is.EqualTo(new[]
            {
                "鎶ラ敊 HTTP 429 Too Many Requests retry-after",
                "\"HTTP 429 Too Many Requests\" retry-after"
            }));
            Assert.That(result.Evidence.Single().Title, Is.EqualTo("MDN 429"));
        });
    }

    [Test]
    public async Task ResearchAsync_OwnerUsesEnglishTechnicalExpansionForChineseBrowserTerms()
    {
        FakePublicSearchService search = new(new Dictionary<string, IReadOnlyList<AgentPublicSearchResult>>
        {
            ["\u6d4f\u89c8\u5668 \u81ea\u52a8\u8bfb\u53d6 \u53cd\u722c"] =
            [
                new AgentPublicSearchResult("Local", "http://127.0.0.1:8080/browser", "unsafe local result")
            ],
            ["browser auto read anti bot"] =
            [
                new AgentPublicSearchResult("Browser Strategy", "https://docs.example.com/browser-strategy", "english technical result")
            ],
            ["official docs \u6d4f\u89c8\u5668 \u81ea\u52a8\u8bfb\u53d6 \u53cd\u722c"] =
            [
                new AgentPublicSearchResult("Should Not Need Generic Docs", "https://docs.example.com/extra", "extra search would waste tokens")
            ]
        });
        FakeInternetService internet = new(new AgentInternetFetchResult(true, "ok", "english technical page content"));
        AgentWebResearchService service = new(search, new AgentWebAccessService(internetService: internet));

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "\u6d4f\u89c8\u5668 \u81ea\u52a8\u8bfb\u53d6 \u53cd\u722c",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                EnableAutoRead = true,
                EnablePublicFetch = true
            },
            MaxSources: 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(search.Queries, Is.EqualTo(new[]
            {
                "\u6d4f\u89c8\u5668 \u81ea\u52a8\u8bfb\u53d6 \u53cd\u722c",
                "browser auto read anti bot"
            }));
            Assert.That(result.Evidence.Single().Title, Is.EqualTo("Browser Strategy"));
        });
    }

    [Test]
    public async Task ResearchAsync_GroupMemberDoesNotExpandQueryWhenOriginalHasNoUsablePublicResults()
    {
        FakePublicSearchService search = new(new Dictionary<string, IReadOnlyList<AgentPublicSearchResult>>
        {
            ["dotnet 9"] =
            [
                new AgentPublicSearchResult("Local", "http://127.0.0.1:8080/private", "unsafe local result")
            ],
            ["official docs dotnet 9"] =
            [
                new AgentPublicSearchResult("Microsoft Docs", "https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9", "official docs result")
            ]
        });
        AgentWebResearchService service = new(search, new AgentWebAccessService());

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "dotnet 9",
            AgentWebAccessActorRole.GroupMember,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                AllowGroupMemberPublicSearch = true
            },
            MaxSources: 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("no_results"));
            Assert.That(search.Queries, Is.EqualTo(new[] { "dotnet 9" }));
        });
    }

    [Test]
    public async Task ResearchAsync_GroupMemberDoesNotUseIntentExpansionForLatestRequests()
    {
        FakePublicSearchService search = new(new Dictionary<string, IReadOnlyList<AgentPublicSearchResult>>
        {
            ["dotnet 10 \u6700\u65b0\u53d1\u5e03\u65e5\u671f"] =
            [
                new AgentPublicSearchResult("Local", "http://127.0.0.1:8080/private", "unsafe local result")
            ],
            ["dotnet 10 \u6700\u65b0\u53d1\u5e03\u65e5\u671f latest release notes"] =
            [
                new AgentPublicSearchResult("Microsoft Release Notes", "https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10", "fresh official release notes")
            ]
        });
        AgentWebResearchService service = new(search, new AgentWebAccessService());

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "dotnet 10 \u6700\u65b0\u53d1\u5e03\u65e5\u671f",
            AgentWebAccessActorRole.GroupMember,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                AllowGroupMemberPublicSearch = true
            },
            MaxSources: 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("no_results"));
            Assert.That(search.Queries, Is.EqualTo(new[] { "dotnet 10 \u6700\u65b0\u53d1\u5e03\u65e5\u671f" }));
        });
    }

    [Test]
    public async Task ResearchAsync_PrefersTrustedSourcesBeforeGenericResults()
    {
        FakePublicSearchService search = new([
            new AgentPublicSearchResult("Generic Blog", "https://random.example.com/post", "blog snippet"),
            new AgentPublicSearchResult("GitHub Repo", "https://github.com/example/agent-browser", "github snippet"),
            new AgentPublicSearchResult("Official Docs", "https://learn.microsoft.com/example/docs", "docs snippet")
        ]);
        FakeInternetService internet = new(new AgentInternetFetchResult(true, "ok", "trusted page content"));
        AgentWebResearchService service = new(search, new AgentWebAccessService(internetService: internet));

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "agent browser",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                EnableAutoRead = true,
                EnablePublicFetch = true
            },
            MaxSources: 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(internet.Calls, Is.EqualTo(1));
            Assert.That(internet.LastUrl, Is.EqualTo("https://learn.microsoft.com/example/docs"));
            Assert.That(result.Evidence.Single().Title, Is.EqualTo("Official Docs"));
        });
    }

    [Test]
    public async Task ResearchAsync_SkipsKnownLoginWallHostFromSiteExperience()
    {
        AgentBrowserSiteExperienceStore store = new(CreateTempRoot());
        store.RecordSnapshotResult(
            "https://login.example.com/private",
            success: false,
            reason: "login_required");
        FakePublicSearchService search = new([
            new AgentPublicSearchResult("Login Wall", "https://login.example.com/private", "login snippet"),
            new AgentPublicSearchResult("Public Docs", "https://docs.example.com/public", "docs snippet")
        ]);
        FakeInternetService internet = new(new AgentInternetFetchResult(true, "ok", "public docs content"));
        AgentWebResearchService service = new(
            search,
            new AgentWebAccessService(internetService: internet),
            store);

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "site experience",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                EnableAutoRead = true,
                EnablePublicFetch = true
            },
            MaxSources: 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(internet.Calls, Is.EqualTo(1));
            Assert.That(internet.LastUrl, Is.EqualTo("https://docs.example.com/public"));
            Assert.That(result.Evidence.Single().Title, Is.EqualTo("Public Docs"));
        });
    }

    [Test]
    public async Task ResearchAsync_UsesSearchSnippetForKnownAntiBotHostWithoutReadingPage()
    {
        AgentBrowserSiteExperienceStore store = new(CreateTempRoot());
        store.RecordSnapshotResult(
            "https://captcha.example.com/page",
            success: false,
            reason: "cloudflare captcha");
        FakePublicSearchService search = new([
            new AgentPublicSearchResult("Anti Bot", "https://captcha.example.com/page", "search snippet saves tokens")
        ]);
        FakeInternetService internet = new(new AgentInternetFetchResult(true, "ok", "should not be read"));
        AgentWebResearchService service = new(
            search,
            new AgentWebAccessService(internetService: internet),
            store);

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "anti bot topic",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                EnableAutoRead = true,
                EnablePublicFetch = true
            },
            MaxSources: 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(internet.Calls, Is.Zero);
            Assert.That(result.Evidence.Single().Summary, Is.EqualTo("search snippet saves tokens"));
            Assert.That(result.Answer, Does.Contain("search snippet saves tokens"));
        });
    }

    [Test]
    public async Task ResearchAsync_OwnerFallsBackToSearchSnippetWhenPageReadFails()
    {
        FakePublicSearchService search = new([
            new AgentPublicSearchResult("Fallback Source", "https://example.com/fallback", "search snippet survives")
        ]);
        FakeInternetService internet = new(new AgentInternetFetchResult(
            false,
            "http_status_403",
            "internet_fetch_denied: http_status_403"));
        AgentWebResearchService service = new(search, new AgentWebAccessService(internetService: internet));

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "fallback topic",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                EnableAutoRead = true,
                EnablePublicFetch = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(internet.Calls, Is.EqualTo(1));
            Assert.That(result.Evidence.Single().Title, Is.EqualTo("Fallback Source"));
            Assert.That(result.Evidence.Single().Summary, Is.EqualTo("search snippet survives"));
            Assert.That(result.Answer, Does.Contain("search snippet survives"));
            Assert.That(result.Answer, Does.Contain("https://example.com/fallback"));
        });
    }

    [Test]
    public async Task ResearchAsync_OwnerRecordsReadFailureInSiteExperienceStore()
    {
        string root = CreateTempRoot();
        AgentBrowserSiteExperienceStore store = new(root);
        FakePublicSearchService search = new([
            new AgentPublicSearchResult("Blocked Source", "https://blocked.example.com/page", "blocked page snippet")
        ]);
        FakeInternetService internet = new(new AgentInternetFetchResult(
            false,
            "cloudflare captcha",
            "internet_fetch_denied: cloudflare captcha"));
        AgentWebAccessService webAccess = new(
            internetService: internet,
            browserSiteExperienceStore: store);
        AgentWebResearchService service = new(search, webAccess);

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "blocked topic",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                EnableAutoRead = true,
                EnablePublicFetch = true
            }));

        AgentBrowserSiteExperience? experience = store.Get("blocked.example.com");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(experience, Is.Not.Null);
            Assert.That(experience!.LastSuccess, Is.False);
            Assert.That(experience.LastReason, Is.EqualTo("cloudflare captcha"));
            Assert.That(experience.HasAntiBotSignals, Is.True);
            Assert.That(experience.PreferredStrategy, Is.EqualTo(AgentBrowserSiteStrategy.DynamicBrowser));
        });
    }

    [Test]
    public async Task ResearchAsync_ReusesCachedResultBeforeSearchingAgain()
    {
        AgentWebResearchControlState control = new();
        FakePublicSearchService search = new([
            new AgentPublicSearchResult("Cached Source", "https://example.com/cache", "cached search snippet")
        ]);
        AgentWebResearchService service = new(search, new AgentWebAccessService(), controlState: control);
        AgentWebResearchRequest request = new(
            "cache this topic",
            AgentWebAccessActorRole.GroupMember,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                AllowGroupMemberPublicSearch = true,
                WebResearchCacheSeconds = 120
            },
            MaxSources: 1,
            ActorUserId: 2002,
            GroupId: 3003);

        AgentWebResearchResult first = await service.ResearchAsync(request);
        AgentWebResearchResult second = await service.ResearchAsync(request);
        AgentWebResearchMetricsSnapshot metrics = control.GetMetricsSnapshot();

        Assert.Multiple(() =>
        {
            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.True);
            Assert.That(search.Calls, Is.EqualTo(1));
            Assert.That(second.Answer, Is.EqualTo(first.Answer));
            Assert.That(metrics.CacheHits, Is.EqualTo(1));
            Assert.That(metrics.SearchCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ResearchAsync_GroupMemberCooldownRejectsDifferentQueryBeforeSearch()
    {
        AgentWebResearchControlState control = new();
        FakePublicSearchService search = new([
            new AgentPublicSearchResult("First Source", "https://example.com/first", "first snippet")
        ]);
        AgentWebResearchService service = new(search, new AgentWebAccessService(), controlState: control);
        AgentWebAccessConfig config = new()
        {
            EnablePublicSearch = true,
            AllowGroupMemberPublicSearch = true,
            WebResearchUserCooldownSeconds = 60,
            WebResearchGroupCooldownSeconds = 60
        };

        AgentWebResearchResult first = await service.ResearchAsync(new AgentWebResearchRequest(
            "first topic",
            AgentWebAccessActorRole.GroupMember,
            config,
            MaxSources: 1,
            ActorUserId: 2002,
            GroupId: 3003));
        AgentWebResearchResult second = await service.ResearchAsync(new AgentWebResearchRequest(
            "second topic",
            AgentWebAccessActorRole.GroupMember,
            config,
            MaxSources: 1,
            ActorUserId: 2002,
            GroupId: 3003));
        AgentWebResearchMetricsSnapshot metrics = control.GetMetricsSnapshot();

        Assert.Multiple(() =>
        {
            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.False);
            Assert.That(second.Reason, Is.EqualTo("web_research_cooldown"));
            Assert.That(search.Calls, Is.EqualTo(1));
            Assert.That(metrics.RateLimitedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ResearchAsync_ConcurrentCapRejectsExtraRequestWithoutSearch()
    {
        AgentWebResearchControlState control = new();
        BlockingPublicSearchService search = new([
            new AgentPublicSearchResult("Slow Source", "https://example.com/slow", "slow snippet")
        ]);
        AgentWebResearchService service = new(search, new AgentWebAccessService(), controlState: control);
        AgentWebAccessConfig config = new()
        {
            EnablePublicSearch = true,
            AllowGroupMemberPublicSearch = true,
            WebResearchMaxConcurrent = 1
        };

        Task<AgentWebResearchResult> firstTask = service.ResearchAsync(new AgentWebResearchRequest(
            "slow topic",
            AgentWebAccessActorRole.GroupMember,
            config,
            MaxSources: 1,
            ActorUserId: 2002,
            GroupId: 3003));
        await search.WaitForCallAsync();
        AgentWebResearchResult rejected = await service.ResearchAsync(new AgentWebResearchRequest(
            "other topic",
            AgentWebAccessActorRole.GroupMember,
            config,
            MaxSources: 1,
            ActorUserId: 2003,
            GroupId: 3004));
        search.Release();
        AgentWebResearchResult first = await firstTask;
        AgentWebResearchMetricsSnapshot metrics = control.GetMetricsSnapshot();

        Assert.Multiple(() =>
        {
            Assert.That(first.Success, Is.True);
            Assert.That(rejected.Success, Is.False);
            Assert.That(rejected.Reason, Is.EqualTo("web_research_busy"));
            Assert.That(search.Calls, Is.EqualTo(1));
            Assert.That(metrics.ConcurrentRejectedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ResearchAsync_TracksSearchReadBytesLatencyAndApproximateSummaryCost()
    {
        AgentWebResearchControlState control = new();
        FakePublicSearchService search = new([
            new AgentPublicSearchResult("Metric Source", "https://example.com/metrics", "metric snippet")
        ]);
        FakeInternetService internet = new(new AgentInternetFetchResult(
            true,
            "ok",
            "metric readable page content with enough detail"));
        AgentWebResearchService service = new(
            search,
            new AgentWebAccessService(internetService: internet),
            controlState: control);

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "metric topic",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                EnableAutoRead = true,
                EnablePublicFetch = true
            },
            MaxSources: 1,
            ActorUserId: 1001));
        AgentWebResearchMetricsSnapshot metrics = control.GetMetricsSnapshot();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(metrics.SearchCount, Is.EqualTo(1));
            Assert.That(metrics.ReadCount, Is.EqualTo(1));
            Assert.That(metrics.PageBytes, Is.GreaterThan(0));
            Assert.That(metrics.TotalLatencyMilliseconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(metrics.ApproximateSummaryTokens, Is.GreaterThan(0));
        });
    }

    static string CreateTempRoot()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "agent-web-research", Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }

    sealed class FakePublicSearchService : AgentPublicSearchService
    {
        readonly IReadOnlyList<AgentPublicSearchResult>? results;
        readonly IReadOnlyDictionary<string, IReadOnlyList<AgentPublicSearchResult>>? resultsByQuery;
        readonly List<string> queries = [];

        public FakePublicSearchService(IReadOnlyList<AgentPublicSearchResult> results)
        {
            this.results = results;
        }

        public FakePublicSearchService(IReadOnlyDictionary<string, IReadOnlyList<AgentPublicSearchResult>> resultsByQuery)
        {
            this.resultsByQuery = resultsByQuery;
        }

        public int Calls { get; private set; }
        public string? LastQuery { get; private set; }
        public IReadOnlyList<string> Queries => queries;

        public override Task<AgentPublicSearchResponse> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastQuery = query;
            queries.Add(query);

            IReadOnlyList<AgentPublicSearchResult> responseResults = resultsByQuery != null
                ? resultsByQuery.GetValueOrDefault(query, [])
                : results ?? [];
            return Task.FromResult(new AgentPublicSearchResponse(true, "ok", responseResults, "formatted search"));
        }
    }

    sealed class FakeInternetService(AgentInternetFetchResult result) : AgentInternetService
    {
        public int Calls { get; private set; }
        public string? LastUrl { get; private set; }

        public override Task<AgentInternetFetchResult> FetchPublicPageAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastUrl = url;
            return Task.FromResult(result);
        }
    }

    sealed class BlockingPublicSearchService(IReadOnlyList<AgentPublicSearchResult> results) : AgentPublicSearchService
    {
        readonly TaskCompletionSource called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public Task WaitForCallAsync() => called.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public void Release() => release.TrySetResult();

        public override async Task<AgentPublicSearchResponse> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            called.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new AgentPublicSearchResponse(true, "ok", results, "formatted search");
        }
    }
}
