using Alife.Function.LocalRuntime;

namespace Alife.Test.LocalRuntime;

public sealed class LocalProductionConfigurationTests
{
    [Test]
    public void Parse_accepts_exactly_two_unique_loopback_slots()
    {
        LocalProductionPlan plan = LocalProductionConfiguration.Parse("""
            {"accounts":[
              {"id":"account-a","oneBotUrl":"ws://127.0.0.1:3001","runtimeRoot":"D:\\Alife\\runtime\\account-a","storageRoot":"D:\\Alife\\storage\\account-a","tempRoot":"D:\\Alife\\.tmp\\account-a"},
              {"id":"account-b","oneBotUrl":"ws://127.0.0.1:3002","runtimeRoot":"D:\\Alife\\runtime\\account-b","storageRoot":"D:\\Alife\\storage\\account-b","tempRoot":"D:\\Alife\\.tmp\\account-b"}]}
            """);

        Assert.That(plan.Accounts.Select(x => x.Id), Is.EqualTo(["account-a", "account-b"]));
    }

    [TestCase("ws://0.0.0.0:3001")]
    [TestCase("ws://example.invalid:3001")]
    public void Parse_rejects_non_loopback_uri(string url)
    {
        string configuration = """
            {"accounts":[
              {"id":"account-a","oneBotUrl":"URL","runtimeRoot":"D:\\Alife\\runtime\\account-a","storageRoot":"D:\\Alife\\storage\\account-a","tempRoot":"D:\\Alife\\.tmp\\account-a"},
              {"id":"account-b","oneBotUrl":"ws://127.0.0.1:3002","runtimeRoot":"D:\\Alife\\runtime\\account-b","storageRoot":"D:\\Alife\\storage\\account-b","tempRoot":"D:\\Alife\\.tmp\\account-b"}]}
            """.Replace("URL", url, StringComparison.Ordinal);

        Assert.That(() => LocalProductionConfiguration.Parse(configuration),
            Throws.TypeOf<LocalProductionConfigurationException>());
    }
}
