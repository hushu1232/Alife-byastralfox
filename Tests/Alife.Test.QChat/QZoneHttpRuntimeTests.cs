using System.Net;
using System.Net.Http;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QZoneHttpRuntimeTests
{
    [Test]
    public async Task GetLatestPost_UsesEphemeralSessionAndParsesJsonpFeed()
    {
        CountingSessionProvider provider = new();
        RecordingHandler handler = new(CreateResponse(
            "_Callback({\"code\":0,\"data\":{\"msglist\":[{\"tid\":\"t1\",\"uin\":10001,\"content\":\"hello\",\"created_time\":42}]}});"));
        QZoneHttpRuntime runtime = new(provider, new HttpClient(handler, disposeHandler: false));

        QZonePostSnapshot? post = await runtime.GetLatestPost(10001);

        Assert.Multiple(() =>
        {
            Assert.That(post, Is.EqualTo(new QZonePostSnapshot("t1", 10001, "hello", null, null, 42)));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Get.Method));
            Assert.That(handler.Requests[0].Url, Does.StartWith(QZoneHttpRuntime.FeedListUrl));
            Assert.That(handler.Requests[0].Url, Does.Contain("uin=10001"));
            Assert.That(handler.Requests[0].Url, Does.Contain("ftype=0"));
            Assert.That(handler.Requests[0].Url, Does.Contain("sort=0"));
            Assert.That(handler.Requests[0].Url, Does.Contain("pos=0"));
            Assert.That(handler.Requests[0].Url, Does.Contain("num=1"));
            Assert.That(handler.Requests[0].Url, Does.Contain("replynum=100"));
            Assert.That(handler.Requests[0].Url, Does.Contain("callback=_preloadCallback"));
            Assert.That(handler.Requests[0].Url, Does.Contain("code_version=1"));
            Assert.That(handler.Requests[0].Url, Does.Contain("format=json"));
            Assert.That(handler.Requests[0].Url, Does.Contain("need_comment=1"));
            Assert.That(handler.Requests[0].Url, Does.Contain("need_private_comment=1"));
            Assert.That(handler.Requests[0].Url, Does.Contain("g_tk=701234"));
            Assert.That(handler.Requests[0].Cookie, Is.EqualTo("uin=o10001; p_skey=session-value;"));
            Assert.That(provider.CallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetLatestPost_ParsesNumericPostIdAndStringOwnerId()
    {
        CountingSessionProvider provider = new();
        RecordingHandler handler = new(CreateResponse(
            "{\"code\":0,\"data\":{\"msglist\":[{\"tid\":12345,\"uin\":\"10001\",\"content\":\"hello\"}]}}"));
        QZoneHttpRuntime runtime = new(provider, new HttpClient(handler, disposeHandler: false));

        QZonePostSnapshot? post = await runtime.GetLatestPost(10001);

        Assert.That(post, Is.EqualTo(new QZonePostSnapshot("12345", 10001, "hello")));
    }

    [Test]
    public async Task TextAndDeleteOperations_SendRequiredFormsInOrder()
    {
        CountingSessionProvider provider = new();
        RecordingHandler handler = new(
            CreateResponse("{\"code\":0,\"data\":{}}"),
            CreateResponse("{\"code\":0,\"data\":{}}"),
            CreateResponse("{\"code\":0,\"data\":{}}"),
            CreateResponse("{\"code\":0,\"data\":{}}"),
            CreateResponse("{\"code\":0,\"data\":{}}"));
        QZoneHttpRuntime runtime = new(provider, new HttpClient(handler, disposeHandler: false));

        await runtime.PublishPost("text");
        await runtime.Comment(20002, "tid", "comment");
        await runtime.ReplyComment(20002, "tid", "cid", "reply");
        await runtime.LikePost(20002, "tid");
        await runtime.DeletePost(new QZonePostSnapshot("tid", 10001, "text", "10001_tid", "tid", 42));

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests.Select(request => request.Url), Is.EqualTo(new[]
            {
                QZoneHttpRuntime.PublishUrl + "?g_tk=701234&uin=10001",
                QZoneHttpRuntime.CommentUrl,
                QZoneHttpRuntime.ReplyUrl,
                QZoneHttpRuntime.LikeUrl,
                QZoneHttpRuntime.DeleteUrl,
            }));
            Assert.That(handler.Requests.Select(request => request.Method), Is.All.EqualTo(HttpMethod.Post.Method));
            Assert.That(handler.Requests[0].Body, Does.Contain("con=text"));
            Assert.That(handler.Requests[0].Body, Does.Contain("syn_tweet_verson=1"));
            Assert.That(handler.Requests[0].Body, Does.Contain("paramstr=1"));
            Assert.That(handler.Requests[0].Body, Does.Contain("who=1"));
            Assert.That(handler.Requests[0].Body, Does.Contain("to_sign=0"));
            Assert.That(handler.Requests[0].Body, Does.Contain("code_version=1"));
            Assert.That(handler.Requests[1].Body, Does.Contain("content=comment"));
            Assert.That(handler.Requests[2].Body, Does.Contain("commentId=cid"));
            Assert.That(handler.Requests[3].Body, Does.Contain("unikey=20002_tid"));
            Assert.That(handler.Requests[4].Body, Does.Contain("feedsKey=tid"));
            Assert.That(handler.Requests[0].Body, Does.Not.Contain("g_tk="));
            Assert.That(handler.Requests.Skip(1).Select(request => request.Body), Is.All.Contains("g_tk=701234"));
            Assert.That(handler.Requests.Select(request => request.Cookie), Is.All.EqualTo("uin=o10001; p_skey=session-value;"));
            Assert.That(provider.CallCount, Is.EqualTo(5));
        });
    }

    [Test]
    public void DeletePost_RejectsUnavailableMetadataOrNonOwnedPost()
    {
        CountingSessionProvider provider = new();
        RecordingHandler handler = new();
        QZoneHttpRuntime runtime = new(provider, new HttpClient(handler, disposeHandler: false));

        InvalidOperationException metadataException = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.DeletePost(new QZonePostSnapshot("tid", 10001, "text")))!;
        InvalidOperationException ownerException = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.DeletePost(new QZonePostSnapshot("tid", 20002, "text", "20002_tid", "tid", 42)))!;

        Assert.Multiple(() =>
        {
            Assert.That(metadataException.Message, Is.EqualTo("qzone_delete_metadata_unavailable"));
            Assert.That(ownerException.Message, Is.EqualTo("qzone_delete_metadata_unavailable"));
            Assert.That(handler.Requests, Is.Empty);
            Assert.That(provider.CallCount, Is.EqualTo(2));
        });
    }

    [TestCase(HttpStatusCode.InternalServerError, "{\"cookies\":\"session-value\"}", "qzone_http_500")]
    [TestCase(HttpStatusCode.OK, "_Callback({\"code\":123,\"message\":\"uin=o10001; p_skey=session-value;\"});", "qzone_api_123")]
    public void FailedHttpOrApiResponse_ExposesOnlySafeQZoneHttpExceptionCode(
        HttpStatusCode statusCode,
        string responseBody,
        string expectedCode)
    {
        QZoneHttpRuntime runtime = new(
            new CountingSessionProvider(),
            new HttpClient(new RecordingHandler(CreateResponse(responseBody, statusCode)), disposeHandler: false));

        QZoneHttpException exception = Assert.ThrowsAsync<QZoneHttpException>(async () =>
            await runtime.GetLatestPost(10001))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Code, Is.EqualTo(expectedCode));
            Assert.That(exception.Message, Is.EqualTo(expectedCode));
            Assert.That(exception.Message, Does.Not.Contain("session-value"));
            Assert.That(exception.Message, Does.Not.Contain("p_skey"));
        });
    }

    [TestCase("{\"code\":\"0\"}")]
    [TestCase("{\"code\":null}")]
    public void NonNumericApiCode_ThrowsSafeInvalidResponseCode(string responseBody)
    {
        QZoneHttpRuntime runtime = new(
            new CountingSessionProvider(),
            new HttpClient(new RecordingHandler(CreateResponse(responseBody)), disposeHandler: false));

        QZoneHttpException exception = Assert.ThrowsAsync<QZoneHttpException>(async () =>
            await runtime.GetLatestPost(10001))!;

        Assert.That(exception.Message, Is.EqualTo("qzone_api_invalid_response"));
    }

    [Test]
    public void ArbitraryParenthesizedResponse_ThrowsSafeInvalidResponseCode()
    {
        QZoneHttpRuntime runtime = new(
            new CountingSessionProvider(),
            new HttpClient(new RecordingHandler(CreateResponse("unexpected text ({\"code\":0})")), disposeHandler: false));

        QZoneHttpException exception = Assert.ThrowsAsync<QZoneHttpException>(async () =>
            await runtime.GetLatestPost(10001))!;

        Assert.That(exception.Message, Is.EqualTo("qzone_api_invalid_response"));
    }

    [Test]
    public async Task GetLatestComments_MapsFeedCommentList()
    {
        RecordingHandler handler = new(CreateResponse("""
            {"code":0,"data":{"msglist":[{"tid":"t1","commentlist":[{"id":"c1","uin":20002,"content":"nice","topicId":"10001_t1"}]}]}}
            """));
        QZoneHttpRuntime runtime = new(new CountingSessionProvider(), new HttpClient(handler, disposeHandler: false));

        IReadOnlyList<QZoneCommentSnapshot> comments = await runtime.GetLatestComments(10001, "t1", 1);

        Assert.That(comments, Is.EqualTo(new[]
        {
            new QZoneCommentSnapshot("c1", 20002, "nice", "10001_t1"),
        }));
    }

    [Test]
    public async Task UploadImage_SubmitsBase64QZoneFormAndMapsUploadedImage()
    {
        CountingSessionProvider provider = new("uin=o10001; skey=session-skey; p_skey=session-value;");
        RecordingHandler handler = new(CreateResponse("""
            {"code":0,"data":{"albumid":"album-a","lloc":"lloc-a","sloc":"sloc-a","width":800,"height":600,"type":1,"url":"https://photo.example.invalid/image.jpg?bo=bo-a"}}
            """));
        QZoneHttpRuntime runtime = new(provider, new HttpClient(handler, disposeHandler: false));

        QZoneUploadedImage image = await runtime.UploadImage(new QZoneImageUpload(
            "generated.png",
            "image/png",
            [1, 2, 3],
            QZoneImageOrigin.Generated));

        Assert.Multiple(() =>
        {
            Assert.That(image, Is.EqualTo(new QZoneUploadedImage("album-a", "lloc-a", "sloc-a", 800, 600, 1, "https://photo.example.invalid/image.jpg?bo=bo-a")));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Post.Method));
            Assert.That(handler.Requests[0].Url, Is.EqualTo($"{QZoneHttpRuntime.UploadUrl}?g_tk=701234"));
            Assert.That(handler.Requests[0].Body, Does.Contain("uploadtype=1"));
            Assert.That(handler.Requests[0].Body, Does.Contain("albumtype=7"));
            Assert.That(handler.Requests[0].Body, Does.Contain("skey=session-skey"));
            Assert.That(handler.Requests[0].Body, Does.Contain("p_skey=session-value"));
            Assert.That(handler.Requests[0].Body, Does.Contain("uin=10001"));
            Assert.That(handler.Requests[0].Body, Does.Contain("p_uin=10001"));
            Assert.That(handler.Requests[0].Body, Does.Contain("zzpaneluin=10001"));
            Assert.That(handler.Requests[0].Body, Does.Contain("refer=shuoshuo"));
            Assert.That(handler.Requests[0].Body, Does.Contain("output_type=json"));
            Assert.That(handler.Requests[0].Body, Does.Contain("base64=1"));
            Assert.That(handler.Requests[0].Body, Does.Contain("picfile=AQID"));
            Assert.That(handler.Requests[0].Cookie, Is.EqualTo("uin=o10001; skey=session-skey; p_skey=session-value;"));
            Assert.That(provider.CallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void UploadImage_RejectsSessionCookiesWithoutBothQZoneUploadKeys()
    {
        CountingSessionProvider provider = new();
        RecordingHandler handler = new();
        QZoneHttpRuntime runtime = new(provider, new HttpClient(handler, disposeHandler: false));

        QZoneHttpException exception = Assert.ThrowsAsync<QZoneHttpException>(async () =>
            await runtime.UploadImage(new QZoneImageUpload("image.png", "image/png", [1], QZoneImageOrigin.Generated)))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Code, Is.EqualTo("qzone_image_upload_session_unavailable"));
            Assert.That(exception.Message, Does.Not.Contain("session-value"));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public void UploadImage_RejectsIncompleteUploadDataWithSafeInvalidResponseCode()
    {
        CountingSessionProvider provider = new("uin=o10001; skey=session-skey; p_skey=session-value;");
        RecordingHandler handler = new(CreateResponse("""
            {"code":0,"data":{"albumid":"","lloc":"lloc-a","sloc":"sloc-a","width":800,"height":600,"type":1,"url":"https://photo.example.invalid/private.jpg?bo=bo-a"}}
            """));
        QZoneHttpRuntime runtime = new(provider, new HttpClient(handler, disposeHandler: false));

        QZoneHttpException exception = Assert.ThrowsAsync<QZoneHttpException>(async () =>
            await runtime.UploadImage(new QZoneImageUpload("image.png", "image/png", [1], QZoneImageOrigin.Generated)))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Code, Is.EqualTo("qzone_api_invalid_response"));
            Assert.That(exception.Message, Does.Not.Contain("photo.example.invalid"));
        });
    }

    [Test]
    public async Task PublishImagePost_UsesRichFieldsForEachUploadedImage()
    {
        CountingSessionProvider provider = new();
        RecordingHandler handler = new(CreateResponse("{\"code\":0,\"data\":{}}"));
        QZoneHttpRuntime runtime = new(provider, new HttpClient(handler, disposeHandler: false));
        QZoneUploadedImage[] images =
        [
            new("album-a", "lloc-a", "sloc-a", 800, 600, 1, "https://photo.example.invalid/a.jpg?bo=bo-a"),
            new("album-b", "lloc-b", "sloc-b", 320, 240, 2, "https://photo.example.invalid/b.jpg?bo=bo-b"),
        ];

        await runtime.PublishImagePost("hello", images);

        Dictionary<string, string> form = ParseForm(handler.Requests.Single().Body);
        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests.Single().Url, Is.EqualTo(QZoneHttpRuntime.PublishUrl));
            Assert.That(form["con"], Is.EqualTo("hello"));
            Assert.That(form["richtype"], Is.EqualTo("1"));
            Assert.That(form["richflag"], Is.EqualTo("1"));
            Assert.That(form["richval"], Is.EqualTo(",album-a,lloc-a,sloc-a,1,600,800,,600,800\t,album-b,lloc-b,sloc-b,2,240,320,,240,320"));
            Assert.That(form["pic_bo"], Is.EqualTo("bo-a\tbo-b"));
            Assert.That(form["g_tk"], Is.EqualTo("701234"));
            Assert.That(provider.CallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void PublishImagePost_RejectsEmptyImagesWithSafeCode()
    {
        RecordingHandler handler = new();
        QZoneHttpRuntime runtime = new(new CountingSessionProvider(), new HttpClient(handler, disposeHandler: false));

        QZoneHttpException exception = Assert.ThrowsAsync<QZoneHttpException>(async () =>
            await runtime.PublishImagePost("hello", []))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Code, Is.EqualTo("qzone_image_upload_unavailable"));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    static HttpResponseMessage CreateResponse(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body),
        };
    }

    static Dictionary<string, string> ParseForm(string body)
    {
        return body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')),
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty);
    }

    private sealed class CountingSessionProvider(string cookies = "uin=o10001; p_skey=session-value;") : IQZoneSessionProvider
    {
        public int CallCount { get; private set; }

        public Task<QZoneSession> GetSessionAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new QZoneSession(10001, cookies, "701234"));
        }
    }

    private sealed record RecordedRequest(string Method, string Url, string? Cookie, string Body);

    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        readonly Queue<HttpResponseMessage> responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            string? cookie = request.Headers.TryGetValues("Cookie", out IEnumerable<string>? values)
                ? values.Single()
                : null;

            Requests.Add(new RecordedRequest(
                request.Method.Method,
                request.RequestUri!.AbsoluteUri,
                cookie,
                body));

            if (responses.Count == 0)
                throw new AssertionException("Unexpected HTTP request.");

            return responses.Dequeue();
        }
    }
}
