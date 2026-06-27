using System.Net;
using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentPublicSearchServiceTests
{
    [Test]
    public async Task SearchAsync_WhenDisabled_DoesNotCallProvider()
    {
        FakePublicSearchProvider provider = new();
        AgentPublicSearchService service = new(
            new AgentPublicSearchConfig { EnablePublicSearch = false },
            provider);

        AgentPublicSearchResponse response = await service.SearchAsync("test");

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.Reason, Is.EqualTo("public_search_disabled"));
            Assert.That(provider.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task SearchAsync_LimitsResultsAndWrapsAsUntrusted()
    {
        FakePublicSearchProvider provider = new(
            new AgentPublicSearchResult("One", "https://example.com/1", "first"),
            new AgentPublicSearchResult("Two", "https://example.com/2", "second"),
            new AgentPublicSearchResult("Three", "https://example.com/3", "third"));
        AgentPublicSearchService service = new(
            new AgentPublicSearchConfig
            {
                EnablePublicSearch = true,
                MaxResults = 2
            },
            provider);

        AgentPublicSearchResponse response = await service.SearchAsync("example");

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(response.Results, Has.Count.EqualTo(2));
            Assert.That(response.FormattedContent, Does.Contain("[UNTRUSTED EXTERNAL CONTEXT: public-search]"));
            Assert.That(response.FormattedContent, Does.Contain("https://example.com/1"));
            Assert.That(response.FormattedContent, Does.Not.Contain("https://example.com/3"));
        });
    }

    [Test]
    public async Task SearchAsync_PassesNormalizedQueryAndClampedMaxResultsToProvider()
    {
        FakePublicSearchProvider provider = new(
            new AgentPublicSearchResult("One", "https://example.com/1", "first"));
        AgentPublicSearchService service = new(
            new AgentPublicSearchConfig
            {
                EnablePublicSearch = true,
                MaxQueryChars = 7,
                MaxResults = 0
            },
            provider);

        AgentPublicSearchResponse response = await service.SearchAsync("   abcdefghij   ");

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(provider.LastQuery, Is.EqualTo("abcdefg"));
            Assert.That(provider.LastMaxResults, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SearchAsync_WhenSuccessful_RecordsAgentAuditSuccess()
    {
        FakePublicSearchProvider provider = new(
            new AgentPublicSearchResult("One", "https://example.com/1", "first"));
        AgentAuditLogService audit = new(CreateAuditPath());
        AgentPublicSearchService service = new(
            new AgentPublicSearchConfig { EnablePublicSearch = true },
            provider,
            audit);

        AgentPublicSearchResponse response = await service.SearchAsync("example");

        AgentAuditLogEntry entry = audit.GetRecentEntries(10).Single();
        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(entry.Action, Is.EqualTo("agent.public_search"));
            Assert.That(entry.Actor, Is.EqualTo("agent"));
            Assert.That(entry.Succeeded, Is.True);
            Assert.That(entry.Error, Is.Null);
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task SearchAsync_WhenQueryEmpty_DoesNotCallProvider(string query)
    {
        FakePublicSearchProvider provider = new();
        AgentPublicSearchService service = new(
            new AgentPublicSearchConfig { EnablePublicSearch = true },
            provider);

        AgentPublicSearchResponse response = await service.SearchAsync(query);

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.Reason, Is.EqualTo("empty_query"));
            Assert.That(provider.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task SearchAsync_WhenQueryEmpty_RecordsAgentAuditFailure()
    {
        FakePublicSearchProvider provider = new();
        AgentAuditLogService audit = new(CreateAuditPath());
        AgentPublicSearchService service = new(
            new AgentPublicSearchConfig { EnablePublicSearch = true },
            provider,
            audit);

        AgentPublicSearchResponse response = await service.SearchAsync("   ");

        AgentAuditLogEntry entry = audit.GetRecentEntries(10).Single();
        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.Reason, Is.EqualTo("empty_query"));
            Assert.That(entry.Action, Is.EqualTo("agent.public_search"));
            Assert.That(entry.Actor, Is.EqualTo("agent"));
            Assert.That(entry.Succeeded, Is.False);
            Assert.That(entry.Error, Is.EqualTo("empty_query"));
        });
    }

    [Test]
    public async Task SearchAsync_WhenProviderMissing_ReturnsNotConfigured()
    {
        AgentPublicSearchService service = new(
            new AgentPublicSearchConfig { EnablePublicSearch = true },
            provider: null);

        AgentPublicSearchResponse response = await service.SearchAsync("example");

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.Reason, Is.EqualTo("search_provider_not_configured"));
        });
    }

    [Test]
    public async Task SearchAsync_WhenProviderThrows_ReturnsFailureAndRecordsAuditFailure()
    {
        FakePublicSearchProvider provider = new(exception: new InvalidOperationException("provider offline"));
        AgentAuditLogService audit = new(CreateAuditPath());
        AgentPublicSearchService service = new(
            new AgentPublicSearchConfig { EnablePublicSearch = true },
            provider,
            audit);

        AgentPublicSearchResponse response = await service.SearchAsync("example");

        AgentAuditLogEntry entry = audit.GetRecentEntries(10).Single();
        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.Reason, Is.EqualTo("search_failed"));
            Assert.That(entry.Action, Is.EqualTo("agent.public_search"));
            Assert.That(entry.Actor, Is.EqualTo("agent"));
            Assert.That(entry.Succeeded, Is.False);
            Assert.That(entry.Error, Is.EqualTo("search_failed"));
        });
    }

    [Test]
    public async Task DuckDuckGoHtmlSearchProvider_ParsesResultLinksSnippetsAndRedirectUrls()
    {
        const string html = """
                            <html>
                              <body>
                                <div class="result">
                                  <a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Falpha%3Fx%3D1&amp;rut=ignored">Alpha &amp; One</a>
                                  <a class="result__snippet">First <b>snippet</b> &amp; detail.</a>
                                </div>
                                <div class="result">
                                  <a class="result__a" href="https://example.org/beta">Beta</a>
                                  <div class="result__snippet">Second snippet.</div>
                                </div>
                              </body>
                            </html>
                            """;
        FakeHttpMessageHandler handler = new(html);
        DuckDuckGoHtmlSearchProvider provider = new(new HttpClient(handler));

        IReadOnlyList<AgentPublicSearchResult> results = await provider.SearchAsync("xiayu bot", 1);

        Assert.Multiple(() =>
        {
            Assert.That(handler.LastRequestUri?.Host, Is.EqualTo("duckduckgo.com"));
            Assert.That(handler.LastRequestUri?.Query, Does.Contain("q=xiayu%20bot"));
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title, Is.EqualTo("Alpha & One"));
            Assert.That(results[0].Url, Is.EqualTo("https://example.com/alpha?x=1"));
            Assert.That(results[0].Snippet, Is.EqualTo("First snippet & detail."));
        });
    }

    [Test]
    public async Task DuckDuckGoHtmlSearchProvider_ParsesRelativeRedirectUrls()
    {
        const string html = """
                            <html>
                              <body>
                                <a class="result__a" href="/l/?uddg=https%3A%2F%2Fexample.net%2Frelative">Relative result</a>
                              </body>
                            </html>
                            """;
        DuckDuckGoHtmlSearchProvider provider = new(new HttpClient(new FakeHttpMessageHandler(html)));

        IReadOnlyList<AgentPublicSearchResult> results = await provider.SearchAsync("relative", 3);

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Url, Is.EqualTo("https://example.net/relative"));
        });
    }

    [Test]
    public async Task BingHtmlSearchProvider_ParsesBingResultBlocks()
    {
        const string html = """
                            <html>
                              <body>
                                <li class="b_algo">
                                  <h2><a href="https://example.com/bing">Bing &amp; One</a></h2>
                                  <div class="b_caption"><p class="b_lineclamp2">Bing <strong>snippet</strong> &amp; detail.</p></div>
                                </li>
                                <li class="b_algo">
                                  <h2><a href="https://example.org/second">Second</a></h2>
                                  <p>Second snippet.</p>
                                </li>
                              </body>
                            </html>
                            """;
        FakeHttpMessageHandler handler = new(html);
        BingHtmlSearchProvider provider = new(new HttpClient(handler));

        IReadOnlyList<AgentPublicSearchResult> results = await provider.SearchAsync("xiayu bot", 1);

        Assert.Multiple(() =>
        {
            Assert.That(handler.LastRequestUri?.Host, Is.EqualTo("www.bing.com"));
            Assert.That(handler.LastRequestUri?.Query, Does.Contain("q=xiayu%20bot"));
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title, Is.EqualTo("Bing & One"));
            Assert.That(results[0].Url, Is.EqualTo("https://example.com/bing"));
            Assert.That(results[0].Snippet, Is.EqualTo("Bing snippet & detail."));
        });
    }

    [Test]
    public async Task FallbackPublicSearchProvider_UsesNextProviderWhenPrimaryFails()
    {
        ThrowingPublicSearchProvider primary = new();
        FakePublicSearchProvider secondary = new(
            new AgentPublicSearchResult("Fallback", "https://example.com/fallback", "fallback snippet"));
        FallbackPublicSearchProvider provider = new(primary, secondary);

        IReadOnlyList<AgentPublicSearchResult> results = await provider.SearchAsync("fallback query", 3);

        Assert.Multiple(() =>
        {
            Assert.That(primary.Calls, Is.EqualTo(1));
            Assert.That(secondary.Calls, Is.EqualTo(1));
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title, Is.EqualTo("Fallback"));
        });
    }

    [Test]
    public void FallbackPublicSearchProvider_WhenAllProvidersFail_RethrowsLastFailure()
    {
        FallbackPublicSearchProvider provider = new(
            new ThrowingPublicSearchProvider(),
            new ThrowingPublicSearchProvider());

        InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SearchAsync("fallback query", 3))!;

        Assert.That(ex.Message, Is.EqualTo("primary failed"));
    }

    static string CreateAuditPath() => Path.Combine(
        TestContext.CurrentContext.WorkDirectory,
        "agent-public-search-audit",
        $"{Guid.NewGuid():N}.jsonl");

    sealed class FakePublicSearchProvider : IAgentPublicSearchProvider
    {
        readonly AgentPublicSearchResult[] results;
        readonly Exception? exception;

        public FakePublicSearchProvider(params AgentPublicSearchResult[] results)
        {
            this.results = results;
        }

        public FakePublicSearchProvider(Exception exception)
        {
            results = [];
            this.exception = exception;
        }

        public int Calls { get; private set; }
        public string? LastQuery { get; private set; }
        public int? LastMaxResults { get; private set; }

        public Task<IReadOnlyList<AgentPublicSearchResult>> SearchAsync(
            string query,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastQuery = query;
            LastMaxResults = maxResults;
            if (exception != null)
                throw exception;

            return Task.FromResult<IReadOnlyList<AgentPublicSearchResult>>(results.ToArray());
        }
    }

    sealed class ThrowingPublicSearchProvider : IAgentPublicSearchProvider
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<AgentPublicSearchResult>> SearchAsync(
            string query,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("primary failed");
        }
    }

    sealed class FakeHttpMessageHandler(string body, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            });
        }
    }
}
