using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentBrowserAutomationServiceTests
{
    [Test]
    public async Task ExecuteAsync_PublicUrlCapturesSnapshotAndReturnsEvidence()
    {
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(
            true,
            "ok",
            "https://example.com/docs",
            "Docs",
            "Install with dotnet tool install. Configure the API key after install.",
            [new AgentBrowserElement("link-1", "link", "Getting Started", "https://example.com/docs/getting-started")]));
        AgentBrowserAutomationService service = new(browserProvider: browser);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse https://example.com/docs install steps",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2, MaxTextCharsPerPage = 32, MaxEvidenceItems = 1 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(browser.Calls, Is.EqualTo(1));
            Assert.That(browser.LastRequest?.Url, Is.EqualTo("https://example.com/docs"));
            Assert.That(browser.LastRequest?.MaxTextChars, Is.EqualTo(32));
            Assert.That(result.Evidence.Single().Title, Is.EqualTo("Docs"));
            Assert.That(result.Evidence.Single().Summary, Does.Contain("Install"));
            Assert.That(result.Answer, Does.Contain("Docs"));
            Assert.That(result.Answer, Does.Contain("https://example.com/docs"));
        });
    }

    [Test]
    public async Task ExecuteAsync_SearchesPublicWebThenCapturesSearchResultSnapshot()
    {
        FakePublicSearchProvider search = new(new AgentPublicSearchResult(
            "Project Docs",
            "https://example.com/project",
            "project docs snippet"));
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(
            true,
            "ok",
            "https://example.com/project",
            "Project Docs",
            "Project documentation content.",
            []));
        AgentBrowserAutomationService service = new(browserProvider: browser, searchProvider: search);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse project docs",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(search.Calls, Is.EqualTo(1));
            Assert.That(search.LastQuery, Is.EqualTo("browse project docs"));
            Assert.That(browser.Calls, Is.EqualTo(1));
            Assert.That(browser.LastRequest?.Url, Is.EqualTo("https://example.com/project"));
        });
    }

    [Test]
    public async Task ExecuteAsync_LoginWallStopsWithSafeReasonWithoutLeakingSnapshotBody()
    {
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(
            false,
            "login_required",
            "https://example.com/private",
            "Login",
            "Sign in required. Secret account prompt text.",
            [],
            new AgentBrowserSnapshotDiagnostics(
                LoginWallDetected: true,
                AntiBotDetected: false,
                TextTruncated: false,
                OriginalTextChars: 45,
                LinkCount: 0)));
        AgentBrowserAutomationService service = new(browserProvider: browser);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse https://example.com/private",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_login_required"));
            Assert.That(result.Answer, Does.Not.Contain("Sign in required"));
            Assert.That(result.Answer, Does.Not.Contain("Secret account prompt text"));
        });
    }

    [Test]
    public async Task ExecuteAsync_CollectsEvidenceUntilConfiguredEvidenceLimit()
    {
        FakeBrowserProvider browser = new(
            new AgentBrowserSnapshot(
                true,
                "ok",
                "https://example.com/docs",
                "Docs",
                "Docs overview content.",
                [new AgentBrowserElement("link-1", "link", "Install", "https://example.com/install")]),
            new AgentBrowserSnapshot(
                true,
                "ok",
                "https://example.com/install",
                "Install",
                "Install content.",
                [new AgentBrowserElement("link-2", "link", "API", "https://example.com/api")]));
        AgentBrowserAutomationService service = new(browserProvider: browser);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse https://example.com/docs install api",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 5, MaxPages = 3, MaxEvidenceItems = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(browser.Calls, Is.EqualTo(2));
            Assert.That(result.Evidence, Has.Count.EqualTo(2));
            Assert.That(result.Evidence.Select(item => item.Title), Is.EqualTo(new[] { "Docs", "Install" }));
        });
    }

    [Test]
    public async Task ExecuteAsync_AntiBotChallengeStopsWithSafeReasonWithoutLeakingSnapshotBody()
    {
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(
            false,
            "cloudflare challenge body hidden",
            "https://example.com/protected",
            "Verification",
            "Solve this challenge token ABC123.",
            [],
            new AgentBrowserSnapshotDiagnostics(
                LoginWallDetected: false,
                AntiBotDetected: true,
                TextTruncated: false,
                OriginalTextChars: 34,
                LinkCount: 0)));
        AgentBrowserAutomationService service = new(browserProvider: browser);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse https://example.com/protected",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_anti_bot_challenge"));
            Assert.That(result.Answer, Does.Not.Contain("ABC123"));
        });
    }

    [Test]
    public async Task ExecuteAsync_UnsafeUrlDeniesBeforeBrowserProvider()
    {
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(true, "ok", "https://example.com", "x", "x", []));
        AgentBrowserAutomationService service = new(browserProvider: browser);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse http://127.0.0.1:3000",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_unsafe_url"));
            Assert.That(browser.Calls, Is.Zero);
        });
    }

    [TestCase("browse file:///C:/secret.txt")]
    [TestCase("browse javascript:alert(1)")]
    [TestCase("browse data:text/html,hello")]
    public async Task ExecuteAsync_UnsafeSchemeDeniesBeforeSearchOrBrowserProvider(string task)
    {
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(true, "ok", "https://example.com", "x", "x", []));
        FakePublicSearchProvider search = new(new AgentPublicSearchResult("x", "https://example.com", "x"));
        AgentBrowserAutomationService service = new(browserProvider: browser, searchProvider: search);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            task,
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_unsafe_url"));
            Assert.That(browser.Calls, Is.Zero);
            Assert.That(search.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_MixedUnsafeAndSafeUrlDeniesBeforeBrowserProvider()
    {
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(true, "ok", "https://example.com", "x", "x", []));
        AgentBrowserAutomationService service = new(browserProvider: browser);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse file:///C:/secret.txt and https://example.com/docs",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_unsafe_url"));
            Assert.That(browser.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_PublicIpv6UrlIsNotRejectedByUrlPrecheck()
    {
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(
            true,
            "ok",
            "https://[2606:4700:4700::1111]",
            "IPv6",
            "Public IPv6 content.",
            []));
        AgentBrowserAutomationService service = new(browserProvider: browser);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse https://[2606:4700:4700::1111]",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2, MaxEvidenceItems = 1 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(browser.Calls, Is.EqualTo(1));
            Assert.That(browser.LastRequest?.Url, Is.EqualTo("https://[2606:4700:4700::1111]"));
        });
    }

    [Test]
    public async Task ExecuteAsync_SearchSyntaxWithColonDoesNotTriggerUnsafeUrlPrecheck()
    {
        FakePublicSearchProvider search = new(new AgentPublicSearchResult(
            "Alife Docs",
            "https://example.com/alife",
            "docs"));
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(
            true,
            "ok",
            "https://example.com/alife",
            "Alife Docs",
            "Alife docs content.",
            []));
        AgentBrowserAutomationService service = new(browserProvider: browser, searchProvider: search);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse site:github.com Alife docs",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2, MaxEvidenceItems = 1 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(search.Calls, Is.EqualTo(1));
            Assert.That(search.LastQuery, Is.EqualTo("browse site:github.com Alife docs"));
            Assert.That(browser.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_SearchWithoutProviderReturnsSpecificFailureReason()
    {
        AgentBrowserAutomationService service = new();

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse project docs",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("search_provider_not_configured"));
            Assert.That(result.Answer, Does.Contain("Public search is not configured"));
        });
    }

    [Test]
    public async Task ExecuteAsync_SearchNoPublicResultReturnsSpecificFailureReason()
    {
        FakePublicSearchProvider search = new(new AgentPublicSearchResult("private", "http://127.0.0.1:3000", "private"));
        AgentBrowserAutomationService service = new(searchProvider: search);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse project docs",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("no_public_search_result"));
            Assert.That(search.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_DeniesNonOwnerBeforeProviders()
    {
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(true, "ok", "https://example.com", "x", "x", []));
        FakePublicSearchProvider search = new(new AgentPublicSearchResult("x", "https://example.com", "x"));
        AgentBrowserAutomationService service = new(browserProvider: browser, searchProvider: search);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse https://example.com",
            AgentWebAccessActorRole.GroupMember,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_owner_required"));
            Assert.That(browser.Calls, Is.Zero);
            Assert.That(search.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_EmptyTaskReturnsEmptyTaskWithoutProviders()
    {
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(true, "ok", "https://example.com", "x", "x", []));
        FakePublicSearchProvider search = new(new AgentPublicSearchResult("x", "https://example.com", "x"));
        AgentBrowserAutomationService service = new(browserProvider: browser, searchProvider: search);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "   ",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_empty_task"));
            Assert.That(browser.Calls, Is.Zero);
            Assert.That(search.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_PageLimitStopsBeforeOpeningNextUsefulLink()
    {
        FakeBrowserProvider browser = new(
            new AgentBrowserSnapshot(
                true,
                "ok",
                "https://example.com/docs",
                "Docs",
                "",
                [new AgentBrowserElement("link-1", "link", "Install", "https://example.com/install")]),
            new AgentBrowserSnapshot(
                true,
                "ok",
                "https://example.com/install",
                "Install",
                "",
                [new AgentBrowserElement("link-2", "link", "API Guide", "https://example.com/api")]));
        AgentBrowserAutomationService service = new(browserProvider: browser);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse https://example.com/docs install api",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 5, MaxPages = 2, MaxEvidenceItems = 3 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_page_limit"));
            Assert.That(browser.Calls, Is.EqualTo(2));
            Assert.That(result.OpenedPageCount, Is.EqualTo(2));
            Assert.That(result.Evidence, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_RuntimeUnavailableAfterFirstPagePreservesOpenedPageCount()
    {
        FakeBrowserProvider browser = new(
            new AgentBrowserSnapshot(
                true,
                "ok",
                "https://example.com/docs",
                "Docs",
                "",
                [new AgentBrowserElement("link-1", "link", "Install", "https://example.com/install")]));
        browser.ThrowOnCall = 2;
        AgentBrowserAutomationService service = new(browserProvider: browser);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse https://example.com/docs install",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 5, MaxPages = 3 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_runtime_unavailable"));
            Assert.That(result.OpenedPageCount, Is.EqualTo(1));
            Assert.That(result.Steps, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task ExecuteAsync_RuntimeUnavailableWhenNavigationNeedsMissingBrowserProvider()
    {
        AgentBrowserAutomationService service = new();

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse https://example.com/docs",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_runtime_unavailable"));
            Assert.That(result.OpenedPageCount, Is.Zero);
        });
    }

    sealed class FakeBrowserProvider(params AgentBrowserSnapshot[] snapshots) : IAgentBrowserProvider
    {
        int index;

        public int Calls { get; private set; }
        public AgentBrowserSnapshotRequest? LastRequest { get; private set; }
        public int ThrowOnCall { get; set; }

        public Task<AgentBrowserSnapshot> CaptureSnapshotAsync(
            AgentBrowserSnapshotRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (ThrowOnCall == Calls)
                throw new InvalidOperationException("browser failed");

            LastRequest = request;
            AgentBrowserSnapshot snapshot = snapshots[Math.Min(index, snapshots.Length - 1)];
            index++;
            return Task.FromResult(snapshot);
        }
    }

    sealed class FakePublicSearchProvider(params AgentPublicSearchResult[] results) : IAgentPublicSearchProvider
    {
        public int Calls { get; private set; }
        public string LastQuery { get; private set; } = "";
        public int LastMaxResults { get; private set; }

        public Task<IReadOnlyList<AgentPublicSearchResult>> SearchAsync(
            string query,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastQuery = query;
            LastMaxResults = maxResults;
            return Task.FromResult<IReadOnlyList<AgentPublicSearchResult>>(results);
        }
    }
}
