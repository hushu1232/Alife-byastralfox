using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentWebAccessServiceTests
{
    [Test]
    public async Task ExecuteAsync_PublicSearchAllowed_CallsSearchService()
    {
        FakePublicSearchService search = new("search context");
        AgentWebAccessService service = new(searchService: search);

        AgentWebAccessResponse response = await service.ExecuteAsync(new AgentWebAccessRequest(
            AgentWebAccessActorRole.GroupMember,
            AgentWebAccessCapability.PublicSearch,
            "dotnet release",
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                AllowGroupMemberPublicSearch = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(response.Reason, Is.EqualTo("ok"));
            Assert.That(response.FormattedContent, Is.EqualTo("search context"));
            Assert.That(search.Calls, Is.EqualTo(1));
            Assert.That(search.LastQuery, Is.EqualTo("dotnet release"));
        });
    }

    [Test]
    public async Task ExecuteAsync_BrowserSnapshotDenied_DoesNotCallBrowserProvider()
    {
        FakeBrowserProvider browser = new();
        AgentWebAccessService service = new(browserProvider: browser);

        AgentWebAccessResponse response = await service.ExecuteAsync(new AgentWebAccessRequest(
            AgentWebAccessActorRole.GroupMember,
            AgentWebAccessCapability.BrowserSnapshot,
            "https://example.com",
            new AgentWebAccessConfig
            {
                EnableBrowserSnapshot = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.Reason, Is.EqualTo("owner_required"));
            Assert.That(browser.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_OwnerBrowserSnapshotAllowed_FormatsUntrustedSnapshot()
    {
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(
            true,
            "ok",
            "https://example.com",
            "Example",
            "browser text",
            []));
        AgentWebAccessService service = new(browserProvider: browser);

        AgentWebAccessResponse response = await service.ExecuteAsync(new AgentWebAccessRequest(
            AgentWebAccessActorRole.Owner,
            AgentWebAccessCapability.BrowserSnapshot,
            "https://example.com",
            new AgentWebAccessConfig
            {
                EnableBrowserSnapshot = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(browser.Calls, Is.EqualTo(1));
            Assert.That(browser.LastRequest?.Url, Is.EqualTo("https://example.com"));
            Assert.That(response.FormattedContent, Does.Contain("[UNTRUSTED EXTERNAL CONTEXT: browser-snapshot]"));
            Assert.That(response.FormattedContent, Does.Contain("browser text"));
        });
    }

    [Test]
    public async Task ExecuteAsync_PublicFetchAllowed_CallsInternetService()
    {
        FakeInternetService internet = new(new AgentInternetFetchResult(
            true,
            "ok",
            "internet content"));
        AgentWebAccessService service = new(internetService: internet);

        AgentWebAccessResponse response = await service.ExecuteAsync(new AgentWebAccessRequest(
            AgentWebAccessActorRole.Owner,
            AgentWebAccessCapability.PublicFetch,
            "https://example.com",
            new AgentWebAccessConfig
            {
                EnablePublicFetch = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(response.FormattedContent, Is.EqualTo("internet content"));
            Assert.That(internet.Calls, Is.EqualTo(1));
            Assert.That(internet.LastUrl, Is.EqualTo("https://example.com"));
        });
    }

    [Test]
    public async Task ExecuteAsync_AutoReadUnknownPublicSiteUsesPublicFetch()
    {
        FakeInternetService internet = new(new AgentInternetFetchResult(
            true,
            "ok",
            "public fetch content"));
        FakeBrowserProvider browser = new();
        AgentBrowserSiteExperienceStore store = new(CreateTempStoreRoot());
        AgentWebAccessService service = new(
            internetService: internet,
            browserProvider: browser,
            browserSiteExperienceStore: store);

        AgentWebAccessResponse response = await service.ExecuteAsync(new AgentWebAccessRequest(
            AgentWebAccessActorRole.Owner,
            AgentWebAccessCapability.AutoRead,
            "https://example.com/docs",
            new AgentWebAccessConfig
            {
                EnableAutoRead = true,
                EnablePublicFetch = true,
                EnableBrowserSnapshot = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(response.Capability, Is.EqualTo(AgentWebAccessCapability.PublicFetch));
            Assert.That(response.Reason, Is.EqualTo("ok"));
            Assert.That(response.FormattedContent, Is.EqualTo("public fetch content"));
            Assert.That(internet.Calls, Is.EqualTo(1));
            Assert.That(internet.LastUrl, Is.EqualTo("https://example.com/docs"));
            Assert.That(browser.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_AutoReadRecordedBrowserSiteUsesSnapshot()
    {
        FakeInternetService internet = new(new AgentInternetFetchResult(
            true,
            "ok",
            "public fetch content"));
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(
            true,
            "ok",
            "https://example.com/dashboard",
            "Dashboard",
            "browser content",
            []));
        AgentBrowserSiteExperienceStore store = new(CreateTempStoreRoot());
        store.RecordSnapshotResult("https://example.com/dashboard", true, "ok");
        AgentWebAccessService service = new(
            internetService: internet,
            browserProvider: browser,
            browserSiteExperienceStore: store);

        AgentWebAccessResponse response = await service.ExecuteAsync(new AgentWebAccessRequest(
            AgentWebAccessActorRole.Owner,
            AgentWebAccessCapability.AutoRead,
            "https://www.example.com/dashboard",
            new AgentWebAccessConfig
            {
                EnableAutoRead = true,
                EnablePublicFetch = true,
                EnableBrowserSnapshot = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(response.Capability, Is.EqualTo(AgentWebAccessCapability.BrowserSnapshot));
            Assert.That(response.FormattedContent, Does.Contain("browser content"));
            Assert.That(browser.Calls, Is.EqualTo(1));
            Assert.That(browser.LastRequest?.Url, Is.EqualTo("https://www.example.com/dashboard"));
            Assert.That(internet.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_AutoReadLoginSiteIsDeniedBeforeProviders()
    {
        FakeInternetService internet = new(new AgentInternetFetchResult(
            true,
            "ok",
            "public fetch content"));
        FakeBrowserProvider browser = new();
        AgentBrowserSiteExperienceStore store = new(CreateTempStoreRoot());
        store.RecordSnapshotResult("https://secure.example.com", false, "login_required");
        AgentWebAccessService service = new(
            internetService: internet,
            browserProvider: browser,
            browserSiteExperienceStore: store);

        AgentWebAccessResponse response = await service.ExecuteAsync(new AgentWebAccessRequest(
            AgentWebAccessActorRole.Owner,
            AgentWebAccessCapability.AutoRead,
            "https://secure.example.com/account",
            new AgentWebAccessConfig
            {
                EnableAutoRead = true,
                EnablePublicFetch = true,
                EnableBrowserSnapshot = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.Capability, Is.EqualTo(AgentWebAccessCapability.AutoRead));
            Assert.That(response.Reason, Is.EqualTo("site_requires_login_or_owner_assistance"));
            Assert.That(response.FormattedContent, Does.Contain("web_access_denied"));
            Assert.That(internet.Calls, Is.Zero);
            Assert.That(browser.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_AutoReadGroupMemberDeniedBeforeProviders()
    {
        FakeInternetService internet = new(new AgentInternetFetchResult(
            true,
            "ok",
            "public fetch content"));
        FakeBrowserProvider browser = new();
        AgentWebAccessService service = new(
            internetService: internet,
            browserProvider: browser,
            browserSiteExperienceStore: new AgentBrowserSiteExperienceStore(CreateTempStoreRoot()));

        AgentWebAccessResponse response = await service.ExecuteAsync(new AgentWebAccessRequest(
            AgentWebAccessActorRole.GroupMember,
            AgentWebAccessCapability.AutoRead,
            "https://example.com/docs",
            new AgentWebAccessConfig
            {
                EnableAutoRead = true,
                EnablePublicFetch = true,
                EnableBrowserSnapshot = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.Reason, Is.EqualTo("owner_required"));
            Assert.That(internet.Calls, Is.Zero);
            Assert.That(browser.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_ExternalRagQueryAllowed_UsesConfiguredChunkLimit()
    {
        FakeExternalRagService rag = new("rag context");
        AgentWebAccessService service = new(externalRagService: rag);

        AgentWebAccessResponse response = await service.ExecuteAsync(new AgentWebAccessRequest(
            AgentWebAccessActorRole.GroupMember,
            AgentWebAccessCapability.ExternalRagQuery,
            "project boundary",
            new AgentWebAccessConfig
            {
                EnableExternalRagQuery = true,
                AllowGroupMemberExternalRagQuery = true,
                MaxExternalRagChunks = 3
            }));

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(response.FormattedContent, Is.EqualTo("rag context"));
            Assert.That(rag.LastQuery, Is.EqualTo("project boundary"));
            Assert.That(rag.LastMaxChunks, Is.EqualTo(3));
        });
    }


    [Test]
    public async Task ExecuteAsync_MissingProvider_ReturnsNotConfigured()
    {
        AgentWebAccessService service = new();

        AgentWebAccessResponse response = await service.ExecuteAsync(new AgentWebAccessRequest(
            AgentWebAccessActorRole.Owner,
            AgentWebAccessCapability.BrowserSnapshot,
            "https://example.com",
            new AgentWebAccessConfig
            {
                EnableBrowserSnapshot = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.Reason, Is.EqualTo("browser_provider_not_configured"));
        });
    }

    sealed class FakePublicSearchService(string formattedContent) : AgentPublicSearchService
    {
        public int Calls { get; private set; }
        public string? LastQuery { get; private set; }

        public override Task<AgentPublicSearchResponse> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastQuery = query;
            return Task.FromResult(new AgentPublicSearchResponse(true, "ok", [], formattedContent));
        }
    }

    sealed class FakeBrowserProvider(AgentBrowserSnapshot? snapshot = null) : IAgentBrowserProvider
    {
        readonly AgentBrowserSnapshot snapshot = snapshot ?? new AgentBrowserSnapshot(
            true,
            "ok",
            "https://example.com",
            "Example",
            "browser text",
            []);

        public int Calls { get; private set; }
        public AgentBrowserSnapshotRequest? LastRequest { get; private set; }

        public Task<AgentBrowserSnapshot> CaptureSnapshotAsync(
            AgentBrowserSnapshotRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(snapshot);
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

    sealed class FakeExternalRagService(string formattedContext) : AgentExternalRagService(
        new AgentExternalRagStore(Path.Combine(Path.GetTempPath(), "alife-web-access-rag-tests-" + Guid.NewGuid().ToString("N"))),
        new FakeInternetService(new AgentInternetFetchResult(true, "ok", "unused")))
    {
        public string? LastQuery { get; private set; }
        public int LastMaxChunks { get; private set; }

        public override AgentExternalRagQueryResponse Query(string query, int maxChunks)
        {
            LastQuery = query;
            LastMaxChunks = maxChunks;
            return new AgentExternalRagQueryResponse(true, "ok", [], formattedContext);
        }
    }

    static string CreateTempStoreRoot() =>
        Path.Combine(Path.GetTempPath(), "alife-web-access-auto-read-tests-" + Guid.NewGuid().ToString("N"));
}
