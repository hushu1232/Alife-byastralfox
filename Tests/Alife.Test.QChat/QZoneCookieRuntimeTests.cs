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
        const string cookie = "skey=x";
        RecordingHandler handler = new("""{"code":0,"data":{"tid":"p1","uin":1001,"content":"hello"}}""");
        CountingCookieProvider provider = new(cookie);
        QZoneCookieRuntime runtime = new(provider, handler);

        QZonePostSnapshot? post = await runtime.GetLatestPost(1001);
        string auditState = runtime.GetAuditSafeState();

        Assert.Multiple(() =>
        {
            Assert.That(post, Is.EqualTo(new QZonePostSnapshot("p1", 1001, "hello")));
            Assert.That(handler.Requests.Single().Headers.Contains("Cookie"), Is.True);
            Assert.That(provider.CallCount, Is.EqualTo(1));
            Assert.That(auditState, Is.Not.EqualTo(cookie));
            Assert.That(auditState, Does.Not.Contain(cookie));
        });
    }

    [Test]
    public async Task LatestCommentsMapFixedQZonePayloadAndHonorRequestedCount()
    {
        RecordingHandler handler = new("""{"code":0,"data":{"comments":[{"id":"c1","uin":1002,"content":"nice"}]}}""");
        CountingCookieProvider provider = new("skey=x");
        QZoneCookieRuntime runtime = new(provider, handler);

        IReadOnlyList<QZoneCommentSnapshot> comments = await runtime.GetLatestComments(1001, "p1", 1);

        Assert.Multiple(() =>
        {
            Assert.That(comments, Is.EqualTo(new[] { new QZoneCommentSnapshot("c1", 1002, "nice") }));
            Assert.That(provider.CallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task LatestPostReturnsNullForNonObjectData()
    {
        QZoneCookieRuntime runtime = new(
            new CountingCookieProvider("skey=x"),
            new RecordingHandler("""{"code":0,"data":[]}"""));

        QZonePostSnapshot? post = await runtime.GetLatestPost(1001);

        Assert.That(post, Is.Null);
    }

    [Test]
    public async Task LatestPostReturnsNullWhenCodeIsNotANumber()
    {
        QZoneCookieRuntime runtime = new(
            new CountingCookieProvider("skey=x"),
            new RecordingHandler("""{"code":"0","data":{"tid":"p1","uin":1001,"content":"hello"}}"""));

        QZonePostSnapshot? post = await runtime.GetLatestPost(1001);

        Assert.That(post, Is.Null);
    }

    [Test]
    public async Task LatestCommentsReturnEmptyForNonObjectCommentEntries()
    {
        QZoneCookieRuntime runtime = new(
            new CountingCookieProvider("skey=x"),
            new RecordingHandler("""{"code":0,"data":{"comments":[[]]}}"""));

        IReadOnlyList<QZoneCommentSnapshot> comments = await runtime.GetLatestComments(1001, "p1", 1);

        Assert.That(comments, Is.Empty);
    }

    [Test]
    public void WriteOperationsAreUnavailableInFirstPhase()
    {
        CountingCookieProvider provider = new("skey=x");
        RecordingHandler handler = new("{}");
        QZoneCookieRuntime runtime = new(provider, handler);

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => runtime.PublishPost("must not send"));
            Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Comment(1001, "p1", "must not send"));
            Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ReplyComment(1001, "p1", "c1", "must not send"));
            Assert.ThrowsAsync<InvalidOperationException>(() => runtime.LikePost(1001, "p1"));
            Assert.That(handler.Requests, Is.Empty);
            Assert.That(provider.CallCount, Is.Zero);
        });
    }

    [Test]
    public void DisposedRuntimeRejectsReadsBeforeUsingProviderOrHandler()
    {
        CountingCookieProvider provider = new("skey=x");
        RecordingHandler handler = new("{}");
        QZoneCookieRuntime runtime = new(provider, handler);

        Assert.That(runtime, Is.AssignableTo<IDisposable>());
        ((IDisposable)(object)runtime).Dispose();

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ObjectDisposedException>(() => runtime.GetLatestPost(1001));
            Assert.That(provider.CallCount, Is.Zero);
            Assert.That(handler.Requests, Is.Empty);
            Assert.That(handler.IsDisposed, Is.False);
        });
    }

    private sealed class CountingCookieProvider(string cookie) : IQZoneEphemeralCookieProvider
    {
        public int CallCount { get; private set; }

        public Task<string> GetCookieAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(cookie);
        }
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public bool IsDisposed { get; private set; }

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

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
