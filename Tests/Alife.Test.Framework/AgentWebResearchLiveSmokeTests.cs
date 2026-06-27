using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentWebResearchLiveSmokeTests
{
    [Test]
    public async Task LiveSmoke_PublicSearchReadAndResearchPipeline()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("ALIFE_WEB_LIVE_SMOKE"),
                "1",
                StringComparison.Ordinal) == false)
        {
            Assert.Ignore("Set ALIFE_WEB_LIVE_SMOKE=1 to run real public web smoke validation.");
        }

        using HttpClient searchClient = new() { Timeout = TimeSpan.FromSeconds(20) };
        using HttpClient readClient = new() { Timeout = TimeSpan.FromSeconds(20) };
        AgentPublicSearchService search = new(
            new AgentPublicSearchConfig
            {
                EnablePublicSearch = true,
                MaxResults = 5,
                MaxQueryChars = 160
            },
            new FallbackPublicSearchProvider(
                new DuckDuckGoHtmlSearchProvider(searchClient),
                new BingHtmlSearchProvider(searchClient)));
        AgentInternetService internet = new(
            config: new AgentInternetConfig
            {
                EnableInternetAccess = true,
                TimeoutMilliseconds = 20000,
                MaxResponseBytes = 1024 * 1024,
                MaxExtractedChars = 6000
            },
            httpClient: readClient);
        AgentWebResearchService service = new(
            search,
            new AgentWebAccessService(internetService: internet));

        AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
            "dotnet 9 release notes",
            AgentWebAccessActorRole.Owner,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                EnableAutoRead = true,
                EnablePublicFetch = true,
                EnableBrowserSnapshot = false
            },
            MaxSources: 1));

        TestContext.Out.WriteLine($"reason={result.Reason}");
        TestContext.Out.WriteLine($"query={result.Query}");
        TestContext.Out.WriteLine($"answer={result.Answer}");
        foreach (AgentWebResearchEvidence evidence in result.Evidence)
            TestContext.Out.WriteLine($"source={evidence.Title} {evidence.Url} type={evidence.SourceType}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Answer);
            Assert.That(result.Evidence, Is.Not.Empty);
            Assert.That(result.Evidence[0].Url, Does.StartWith("http"));
            Assert.That(result.Answer, Does.Contain("来源"));
        });
    }
}
