using Alife.Function.FunctionCaller;
using Alife.Function.QChat;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatImageSegmentParserTests
{
    [Test]
    public void ExtractsImageUrlAndFile()
    {
        IReadOnlyList<QChatImageCandidate> images = QChatImageSegmentParser.Extract(
            "[CQ:image,file=abc.jpg,url=https://multimedia.nt.qq.com.cn/image.jpg]");

        Assert.Multiple(() =>
        {
            Assert.That(images, Has.Count.EqualTo(1));
            Assert.That(images[0].File, Is.EqualTo("abc.jpg"));
            Assert.That(images[0].Url, Is.EqualTo("https://multimedia.nt.qq.com.cn/image.jpg"));
            Assert.That(images[0].SourceKind, Is.EqualTo(QChatImageSourceKind.PublicUrl));
        });
    }

    [Test]
    public void DecodesHtmlEscapedAmpersandsInImageUrl()
    {
        IReadOnlyList<QChatImageCandidate> images = QChatImageSegmentParser.Extract(
            "[CQ:image,file=abc.jpg,url=https://multimedia.nt.qq.com.cn/download?appid=1406&amp;fileid=abc&amp;rkey=def]");

        Assert.Multiple(() =>
        {
            Assert.That(images, Has.Count.EqualTo(1));
            Assert.That(images[0].Url, Is.EqualTo("https://multimedia.nt.qq.com.cn/download?appid=1406&fileid=abc&rkey=def"));
            Assert.That(images[0].SourceKind, Is.EqualTo(QChatImageSourceKind.PublicUrl));
        });
    }

    [Test]
    public void BuildsLocalToolRouteInputFromCurrentImageWithoutChangingModelInput()
    {
        const string modelInput = "把这张图片保存为 happy_cat.jpg";
        IReadOnlyList<string> trustedUrls = QChatImageSegmentParser.ExtractTrustedHttpsUrls(
            "[CQ:image,file=abc.jpg,url=https://multimedia.nt.qq.com.cn/download?appid=1406&amp;fileid=abc]");
        string routeInput = QChatImageSegmentParser.AppendFirstImageUrlForLocalToolRouting(modelInput, trustedUrls);
        XmlFunctionCaller caller = new(NullLogger<XmlFunctionCaller>.Instance);
        caller.EnableQqEmojiSaveImageCapability();
        ToolRouteDecision route = caller.RouteCurrentTurn(
            routeInput,
            new ToolRouteState(string.Empty, string.Empty, true, true, true));

        Assert.Multiple(() =>
        {
            Assert.That(trustedUrls, Is.EqualTo(new[]
            {
                "https://multimedia.nt.qq.com.cn/download?appid=1406&fileid=abc"
            }));
            Assert.That(routeInput, Is.EqualTo(modelInput + Environment.NewLine + trustedUrls[0]));
            Assert.That(route.Allows("saveimage"), Is.True);
            Assert.That(route.BoundParameters["source"], Is.EqualTo(trustedUrls[0]));
            Assert.That(modelInput, Does.Not.Contain("https://"));
        });
    }

    [Test]
    public void ExtractsFileOnlyImageAsMissingUrl()
    {
        IReadOnlyList<QChatImageCandidate> images = QChatImageSegmentParser.Extract("[CQ:image,file=abc.jpg]");

        Assert.Multiple(() =>
        {
            Assert.That(images, Has.Count.EqualTo(1));
            Assert.That(images[0].File, Is.EqualTo("abc.jpg"));
            Assert.That(images[0].Url, Is.Null);
            Assert.That(images[0].SourceKind, Is.EqualTo(QChatImageSourceKind.MissingUrl));
        });
    }

    [Test]
    public void ExtractsMultipleImagesInOrder()
    {
        IReadOnlyList<QChatImageCandidate> images = QChatImageSegmentParser.Extract(
            "[CQ:image,file=a.jpg,url=https://example.invalid/a.jpg] text [CQ:image,file=b.png,url=https://example.invalid/b.png]");

        Assert.That(images.Select(image => image.Url), Is.EqualTo(new[]
        {
            "https://example.invalid/a.jpg",
            "https://example.invalid/b.png"
        }));
    }

    [Test]
    public void IgnoresFaceAndMfaceSegments()
    {
        IReadOnlyList<QChatImageCandidate> images = QChatImageSegmentParser.Extract(
            "[CQ:face,id=14][CQ:mface,emoji_id=123]");

        Assert.That(images, Is.Empty);
    }
}
