using System.Text.Json;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class NapCatQZoneSessionProviderTests
{
    [Test]
    public async Task GetSessionAsync_UsesNapCatGetCookiesAndParsesCurrentAccount()
    {
        FakeActionInvoker invoker = new();
        invoker.Enqueue(new NapCatQZoneCookieResponse(
            "uin=o10001; p_uin=o10001; p_skey=session-value;", "701234"));
        NapCatQZoneSessionProvider provider = new(invoker);

        QZoneSession session = await provider.GetSessionAsync();

        Assert.That(invoker.Calls.Single().Action, Is.EqualTo("get_cookies"));
        Assert.That(invoker.Calls.Single().Json, Is.EqualTo("{\"domain\":\"qzone.qq.com\"}"));
        Assert.That(session.AccountId, Is.EqualTo(10001));
        Assert.That(session.Bkn, Is.EqualTo("701234"));
    }

    [TestCase("", "701234", "qzone_cookie_unavailable")]
    [TestCase("uin=o10001", "", "qzone_bkn_unavailable")]
    public void GetSessionAsync_RejectsIncompleteNapCatResponse(string cookies, string bkn, string reason)
    {
        FakeActionInvoker invoker = new();
        invoker.Enqueue(new NapCatQZoneCookieResponse(cookies, bkn));

        QZoneSessionUnavailableException exception = Assert.ThrowsAsync<QZoneSessionUnavailableException>(
            async () => await new NapCatQZoneSessionProvider(invoker).GetSessionAsync())!;

        Assert.That(exception.Message, Is.EqualTo(reason));
        if (cookies.Length > 0)
            Assert.That(exception.Message, Does.Not.Contain(cookies));
    }

    private sealed class FakeActionInvoker : IOneBotActionInvoker
    {
        public List<(string Action, string Json)> Calls { get; } = [];
        readonly Queue<object?> responses = new();

        public void Enqueue(object? response)
        {
            responses.Enqueue(response);
        }

        public Task<T?> CallActionAsync<T>(string action, object? parameters = null)
        {
            Calls.Add((action, JsonSerializer.Serialize(parameters)));
            return Task.FromResult(responses.Count == 0 ? default : (T?)responses.Dequeue());
        }
    }
}
