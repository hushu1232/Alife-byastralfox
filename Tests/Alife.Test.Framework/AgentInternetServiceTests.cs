using Alife.Function.Agent;
using NUnit.Framework;
using System.Net;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentInternetServiceTests
{
    [TestCase("https://example.com/page", true)]
    [TestCase("http://example.com/page", true)]
    [TestCase("ftp://example.com/file", false)]
    [TestCase("file:///C:/Windows/win.ini", false)]
    [TestCase("javascript:alert(1)", false)]
    [TestCase("http://localhost:3000", false)]
    [TestCase("http://127.0.0.1:3000", false)]
    [TestCase("http://10.0.0.1", false)]
    [TestCase("http://192.168.1.1", false)]
    public void UrlPolicy_AllowsOnlyPublicHttpAndHttps(string url, bool expected)
    {
        AgentInternetConfig config = AgentInternetConfig.CreateDefault();

        AgentInternetUrlPolicyDecision decision = AgentInternetUrlPolicy.Evaluate(url, config);

        Assert.That(decision.Allowed, Is.EqualTo(expected));
    }

    [Test]
    public void UrlPolicy_DeniesBlockedHost()
    {
        AgentInternetConfig config = AgentInternetConfig.CreateDefault();
        config.BlockedHosts = "example.com, ads.example";

        AgentInternetUrlPolicyDecision decision = AgentInternetUrlPolicy.Evaluate("https://example.com/a", config);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("blocked_host"));
        });
    }

    [Test]
    public async Task FetchPublicPageAsync_WhenDisabled_DoesNotCallHttp()
    {
        RecordingHandler handler = new("<html>hello</html>");
        AgentInternetService service = new(
            config: new AgentInternetConfig { EnableInternetAccess = false },
            httpClient: new HttpClient(handler));

        AgentInternetFetchResult result = await service.FetchPublicPageAsync("https://example.com");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("internet_access_disabled"));
            Assert.That(handler.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task FetchPublicPageAsync_WrapsExternalContentAsUntrusted()
    {
        RecordingHandler handler = new("<html><head><title>Example</title></head><body>Hello <b>world</b></body></html>");
        AgentInternetService service = new(
            config: new AgentInternetConfig { EnableInternetAccess = true },
            httpClient: new HttpClient(handler));

        AgentInternetFetchResult result = await service.FetchPublicPageAsync("https://example.com");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Content, Does.Contain("[UNTRUSTED EXTERNAL CONTEXT: internet-page]"));
            Assert.That(result.Content, Does.Contain("Hello world"));
            Assert.That(handler.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task FetchPublicPageAsync_WhenUrlDenied_RecordsAuditFailureWithoutHttp()
    {
        RecordingHandler handler = new("<html>hello</html>");
        string auditPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "agent-internet-audit",
            $"{Guid.NewGuid():N}.jsonl");
        AgentAuditLogService audit = new(auditPath);
        AgentInternetService service = new(
            config: new AgentInternetConfig { EnableInternetAccess = true },
            auditLog: audit,
            httpClient: new HttpClient(handler));

        AgentInternetFetchResult result = await service.FetchPublicPageAsync("http://10.0.0.1:3000");

        IReadOnlyList<AgentAuditLogEntry> entries = audit.GetRecentEntries(10);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("private_or_loopback_address"));
            Assert.That(handler.Calls, Is.Zero);
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Action, Is.EqualTo("agent.internet.fetch"));
            Assert.That(entries[0].Succeeded, Is.False);
            Assert.That(entries[0].Error, Is.EqualTo("private_or_loopback_address"));
        });
    }

    sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }
}
