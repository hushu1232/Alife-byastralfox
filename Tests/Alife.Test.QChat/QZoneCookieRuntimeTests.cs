using System.Net;
using System.Net.Http;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QZoneCookieRuntimeTests
{
    [Test]
    public async Task LatestPostMapsFixedQZonePayloadWithoutPersistingCookie()
    {
        RecordingHandler handler = new("""{"code":0,"data":{"tid":"p1","uin":1001,"content":"hello"}}""");
        QZoneCookieRuntime runtime = new(new StaticCookieProvider("skey=x"), handler);

        QZonePostSnapshot? post = await runtime.GetLatestPost(1001);

        Assert.Multiple(() =>
        {
            Assert.That(post, Is.EqualTo(new QZonePostSnapshot("p1", 1001, "hello")));
            Assert.That(handler.Requests.Single().Headers.Contains("Cookie"), Is.True);
            Assert.That(runtime.GetAuditSafeState().Contains("skey"), Is.False);
        });
    }

    [Test]
    public async Task LatestCommentsMapFixedQZonePayloadAndHonorRequestedCount()
    {
        RecordingHandler handler = new("""{"code":0,"data":{"comments":[{"id":"c1","uin":1002,"content":"nice"}]}}""");
        QZoneCookieRuntime runtime = new(new StaticCookieProvider("skey=x"), handler);

        IReadOnlyList<QZoneCommentSnapshot> comments = await runtime.GetLatestComments(1001, "p1", 1);

        Assert.That(comments, Is.EqualTo(new[] { new QZoneCommentSnapshot("c1", 1002, "nice") }));
    }

    [Test]
    public void WriteOperationsAreUnavailableInFirstPhase()
    {
        QZoneCookieRuntime runtime = new(new StaticCookieProvider("skey=x"), new RecordingHandler("{}"));

        Assert.ThrowsAsync<InvalidOperationException>(() => runtime.PublishPost("must not send"));
    }

    private sealed class StaticCookieProvider(string cookie) : IQZoneEphemeralCookieProvider
    {
        public Task<string> GetCookieAsync(CancellationToken cancellationToken = default) => Task.FromResult(cookie);
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            });
        }
    }
}
