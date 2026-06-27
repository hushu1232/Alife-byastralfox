using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentBrowserSiteExperienceStoreTests
{
    [Test]
    public void RecordSnapshotResult_PersistsLatestHostExperience()
    {
        string root = CreateTempRoot();
        AgentBrowserSiteExperienceStore store = new(root);

        bool recorded = store.RecordSnapshotResult(
            "https://Example.com/docs",
            success: true,
            reason: "ok",
            now: new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero));

        AgentBrowserSiteExperienceStore reloaded = new(root);
        AgentBrowserSiteExperience? experience = reloaded.Get("example.com");

        Assert.Multiple(() =>
        {
            Assert.That(recorded, Is.True);
            Assert.That(experience, Is.Not.Null);
            Assert.That(experience!.Host, Is.EqualTo("example.com"));
            Assert.That(experience.PreferredStrategy, Is.EqualTo(AgentBrowserSiteStrategy.BrowserSnapshot));
            Assert.That(experience.NeedsBrowser, Is.True);
            Assert.That(experience.NeedsLogin, Is.False);
            Assert.That(experience.HasAntiBotSignals, Is.False);
            Assert.That(experience.LastSuccess, Is.True);
            Assert.That(experience.LastReason, Is.EqualTo("ok"));
            Assert.That(experience.RiskLevel, Is.EqualTo(AgentBrowserSiteRiskLevel.Low));
        });
    }

    [Test]
    public void RecordSnapshotResult_RejectsUnsafeUrls()
    {
        AgentBrowserSiteExperienceStore store = new(CreateTempRoot());

        Assert.Multiple(() =>
        {
            Assert.That(store.RecordSnapshotResult("file:///c:/secret.txt", true, "ok"), Is.False);
            Assert.That(store.RecordSnapshotResult("http://localhost:8080", true, "ok"), Is.False);
            Assert.That(store.RecordSnapshotResult("http://127.0.0.1/admin", true, "ok"), Is.False);
            Assert.That(store.RecordSnapshotResult("http://192.168.1.10/admin", true, "ok"), Is.False);
            Assert.That(store.ListRecent(10), Is.Empty);
        });
    }

    [Test]
    public void RecordSnapshotResult_ClassifiesLoginAndAntiBotSignals()
    {
        AgentBrowserSiteExperienceStore store = new(CreateTempRoot());

        store.RecordSnapshotResult(
            "https://login.example.com",
            success: false,
            reason: "login_required",
            now: new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero));
        store.RecordSnapshotResult(
            "https://captcha.example.com",
            success: false,
            reason: "cloudflare captcha",
            now: new DateTimeOffset(2026, 6, 23, 12, 1, 0, TimeSpan.Zero));

        AgentBrowserSiteExperience? login = store.Get("login.example.com");
        AgentBrowserSiteExperience? captcha = store.Get("captcha.example.com");

        Assert.Multiple(() =>
        {
            Assert.That(login, Is.Not.Null);
            Assert.That(login!.PreferredStrategy, Is.EqualTo(AgentBrowserSiteStrategy.Blocked));
            Assert.That(login.NeedsLogin, Is.True);
            Assert.That(login.RiskLevel, Is.EqualTo(AgentBrowserSiteRiskLevel.High));

            Assert.That(captcha, Is.Not.Null);
            Assert.That(captcha!.PreferredStrategy, Is.EqualTo(AgentBrowserSiteStrategy.DynamicBrowser));
            Assert.That(captcha.HasAntiBotSignals, Is.True);
            Assert.That(captcha.RiskLevel, Is.EqualTo(AgentBrowserSiteRiskLevel.Medium));
        });
    }

    [Test]
    public async Task WebAccessBrowserSnapshot_RecordsSiteExperience()
    {
        AgentBrowserSiteExperienceStore store = new(CreateTempRoot());
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(
            true,
            "ok",
            "https://example.com/docs",
            "Docs",
            "text",
            []));
        AgentWebAccessService service = new(browserProvider: browser, browserSiteExperienceStore: store);

        await service.ExecuteAsync(new AgentWebAccessRequest(
            AgentWebAccessActorRole.Owner,
            AgentWebAccessCapability.BrowserSnapshot,
            "https://example.com/docs",
            new AgentWebAccessConfig
            {
                EnableBrowserSnapshot = true
            }));

        AgentBrowserSiteExperience? experience = store.Get("example.com");

        Assert.Multiple(() =>
        {
            Assert.That(browser.Calls, Is.EqualTo(1));
            Assert.That(experience, Is.Not.Null);
            Assert.That(experience!.LastSuccess, Is.True);
            Assert.That(experience.LastReason, Is.EqualTo("ok"));
        });
    }

    [Test]
    public void FormatStatus_ReturnsRecentReadableSummary()
    {
        AgentBrowserSiteExperienceStore store = new(CreateTempRoot());
        store.RecordSnapshotResult(
            "https://example.com",
            success: true,
            reason: "ok",
            now: new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero));

        string status = store.FormatStatus();

        Assert.Multiple(() =>
        {
            Assert.That(status, Does.Contain("browser_site_experience recent=1"));
            Assert.That(status, Does.Contain("host=example.com"));
            Assert.That(status, Does.Contain("strategy=BrowserSnapshot"));
            Assert.That(status, Does.Contain("success=true"));
        });
    }

    [Test]
    public void FormatDoctor_ReturnsProviderStateAndStrategySummary()
    {
        AgentBrowserSiteExperienceStore store = new(CreateTempRoot());
        store.RecordSnapshotResult(
            "https://example.com",
            success: true,
            reason: "ok",
            now: new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero));

        string doctor = store.FormatDoctor(
            internetAccessEnabled: true,
            browserProviderConfigured: true);

        Assert.Multiple(() =>
        {
            Assert.That(doctor, Does.Contain("web_doctor browser_provider=configured internet=enabled"));
            Assert.That(doctor, Does.Contain("recent_sites=1"));
            Assert.That(doctor, Does.Contain("host=example.com"));
            Assert.That(doctor, Does.Contain("strategy=BrowserSnapshot"));
        });
    }

    [Test]
    public void StrategyRouter_DefaultsUnknownPublicSiteToPublicFetch()
    {
        AgentBrowserSiteExperienceStore store = new(CreateTempRoot());

        AgentWebStrategyDecision decision = AgentWebStrategyRouter.Evaluate(
            "https://example.com/docs",
            store);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.True);
            Assert.That(decision.Host, Is.EqualTo("example.com"));
            Assert.That(decision.Strategy, Is.EqualTo(AgentBrowserSiteStrategy.PublicFetch));
            Assert.That(decision.Capability, Is.EqualTo(AgentWebAccessCapability.PublicFetch));
            Assert.That(decision.Reason, Is.EqualTo("default_public_fetch"));
        });
    }

    [Test]
    public void StrategyRouter_UsesRecordedBrowserSnapshotPreference()
    {
        AgentBrowserSiteExperienceStore store = new(CreateTempRoot());
        store.RecordSnapshotResult(
            "https://example.com/dashboard",
            success: true,
            reason: "ok",
            now: new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero));

        AgentWebStrategyDecision decision = AgentWebStrategyRouter.Evaluate(
            "https://www.example.com/dashboard",
            store);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.True);
            Assert.That(decision.Strategy, Is.EqualTo(AgentBrowserSiteStrategy.BrowserSnapshot));
            Assert.That(decision.Capability, Is.EqualTo(AgentWebAccessCapability.BrowserSnapshot));
            Assert.That(decision.Reason, Is.EqualTo("site_prefers_browser_snapshot"));
        });
    }

    [Test]
    public void StrategyRouter_BlocksRecordedLoginSite()
    {
        AgentBrowserSiteExperienceStore store = new(CreateTempRoot());
        store.RecordSnapshotResult(
            "https://secure.example.com",
            success: false,
            reason: "login_required",
            now: new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero));

        AgentWebStrategyDecision decision = AgentWebStrategyRouter.Evaluate(
            "https://secure.example.com/account",
            store);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Strategy, Is.EqualTo(AgentBrowserSiteStrategy.Blocked));
            Assert.That(decision.Capability, Is.Null);
            Assert.That(decision.Reason, Is.EqualTo("site_requires_login_or_owner_assistance"));
        });
    }

    [Test]
    public void StrategyRouter_RejectsUnsafeUrl()
    {
        AgentWebStrategyDecision decision = AgentWebStrategyRouter.Evaluate(
            "http://127.0.0.1/admin",
            new AgentBrowserSiteExperienceStore(CreateTempRoot()));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Strategy, Is.EqualTo(AgentBrowserSiteStrategy.Blocked));
            Assert.That(decision.Reason, Is.EqualTo("unsafe_url"));
        });
    }

    static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "alife-browser-site-experience-tests", Guid.NewGuid().ToString("N"));

    sealed class FakeBrowserProvider(AgentBrowserSnapshot snapshot) : IAgentBrowserProvider
    {
        public int Calls { get; private set; }

        public Task<AgentBrowserSnapshot> CaptureSnapshotAsync(
            AgentBrowserSnapshotRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(snapshot);
        }
    }
}
