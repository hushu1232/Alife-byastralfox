using System.Net;
using System.Net.Http;
using System.IO;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QZoneImageSourceResolverTests
{
    [Test]
    public async Task ResolveAsync_GeneratedFileReturnsOriginalBytesWithoutCreatingAnotherFile()
    {
        string root = CreateTempRoot();
        string generatedPath = Path.Combine(root, "generated.png");
        await File.WriteAllBytesAsync(generatedPath, [1, 2, 3]);
        QZoneImageSourceResolver resolver = new(new HttpClient(new RejectingHandler(), disposeHandler: false));

        try
        {
            QZoneImageUpload upload = await resolver.ResolveAsync(
                QZoneImageSource.GeneratedFile(generatedPath, "generated.png"),
                maximumBytes: 8,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(upload.FileName, Is.EqualTo("generated.png"));
                Assert.That(upload.Bytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(upload.Origin, Is.EqualTo(QZoneImageOrigin.Generated));
                Assert.That(Directory.GetFiles(root), Is.EqualTo(new[] { generatedPath }));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ResolveAsync_OwnerUrlUsesInjectedHttpClientAndPreservesContentType()
    {
        Uri url = new("https://example.invalid/image.jpg");
        RecordingBytesHandler handler = new(CreateResponse([9, 8, 7], "image/jpeg"));
        QZoneImageSourceResolver resolver = new(new HttpClient(handler, disposeHandler: false));

        QZoneImageUpload upload = await resolver.ResolveAsync(
            QZoneImageSource.OwnerUrl(url),
            maximumBytes: 8,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(upload.Bytes, Is.EqualTo(new byte[] { 9, 8, 7 }));
            Assert.That(upload.ContentType, Is.EqualTo("image/jpeg"));
            Assert.That(upload.Origin, Is.EqualTo(QZoneImageOrigin.OwnerProvided));
            Assert.That(handler.RequestUris, Is.EqualTo(new[] { url }));
        });
    }

    [Test]
    public async Task ResolveAsync_RejectsUnavailableInvalidAndOversizedSourcesWithSafeCodes()
    {
        string root = CreateTempRoot();
        string missingPath = Path.Combine(root, "private-missing-image.png");
        string oversizedPath = Path.Combine(root, "oversized.png");
        await File.WriteAllBytesAsync(oversizedPath, [1, 2, 3]);
        QZoneImageSourceResolver resolver = new(new HttpClient(new RejectingHandler(), disposeHandler: false));

        try
        {
            await AssertSafeCodeAsync(
                () => resolver.ResolveAsync(QZoneImageSource.OwnerFile(missingPath), 8, CancellationToken.None),
                "qzone_image_source_unavailable",
                missingPath);
            await AssertSafeCodeAsync(
                () => resolver.ResolveAsync(QZoneImageSource.OwnerFile(""), 8, CancellationToken.None),
                "qzone_image_source_invalid");
            await AssertSafeCodeAsync(
                () => resolver.ResolveAsync(QZoneImageSource.OwnerUrl(new Uri("ftp://example.invalid/private-image.jpg")), 8, CancellationToken.None),
                "qzone_image_source_invalid",
                "example.invalid");
            await AssertSafeCodeAsync(
                () => resolver.ResolveAsync(QZoneImageSource.GeneratedFile(oversizedPath), 2, CancellationToken.None),
                "qzone_image_too_large",
                oversizedPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static Task AssertSafeCodeAsync(Func<Task> action, string expectedCode, string? secret = null)
    {
        QZoneImageSourceException exception = Assert.ThrowsAsync<QZoneImageSourceException>(async () => await action())!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo(expectedCode));
            if (secret is not null)
                Assert.That(exception.Message, Does.Not.Contain(secret));
        });

        return Task.CompletedTask;
    }

    static HttpResponseMessage CreateResponse(byte[] bytes, string contentType)
    {
        ByteArrayContent content = new(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-qzone-image-source-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new AssertionException("Unexpected HTTP request.");
        }
    }

    private sealed class RecordingBytesHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        readonly HttpResponseMessage response = response;

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(response);
        }
    }
}
