using Alife.Function.Browser;
using Xunit;

namespace Alife.Test.Browser;

public class BrowserIntegrationTest
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task TestLocalBrowserWorkflow()
    {
        string userDataFolder = Path.Combine(Path.GetTempPath(), "Alife.BrowserTest", Guid.NewGuid().ToString("N"));
        using var engine = new BrowserEngine(userDataFolder);
        await engine.WaitToLoadedAsync(TimeSpan.FromSeconds(30));
        await engine.NavigateAsync("about:blank", TimeSpan.FromSeconds(10));

        string bodyHtml = """
                          <input id="query" placeholder="Search box" />
                          <button id="search" onclick="document.getElementById('result').textContent='Local result: ' + document.getElementById('query').value">Search</button>
                          <div id="result">Waiting</div>
                          """;
        string writeScript = "(() => { document.title = 'Alife Browser Test'; document.body.innerHTML = " +
                             System.Text.Json.JsonSerializer.Serialize(bodyHtml) +
                             "; return document.title; })()";
        string writeResult = await engine.ExecuteScriptAsync(writeScript);
        Assert.Contains("Alife Browser Test", writeResult);

        string observe = await engine.ObserveAsync(1);
        Assert.Contains("Alife Browser Test", observe);
        Assert.Contains("Search box", observe);
        Assert.Contains("Search", observe);

        const string searchScript = """
                                    (function() {
                                        const input = document.getElementById('query');
                                        input.focus();
                                        input.value = 'alife';
                                        input.dispatchEvent(new Event('input', { bubbles: true }));

                                        document.getElementById('search').click();
                                        return document.getElementById('result').textContent;
                                    })()
                                    """;

        string jsResult = await engine.ExecuteScriptAsync(searchScript);
        Assert.Contains("Local result: alife", jsResult);

        string searchResult = await engine.ObserveAsync(1);
        Assert.Contains("Local result: alife", searchResult);
    }
}
