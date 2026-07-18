using System.Text.Json;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QZoneLoopbackOperatorTests
{
    [TestCase(QZoneLoopbackOperatorOperation.Read)]
    [TestCase(QZoneLoopbackOperatorOperation.Post)]
    [TestCase(QZoneLoopbackOperatorOperation.Comment)]
    [TestCase(QZoneLoopbackOperatorOperation.Like)]
    [TestCase(QZoneLoopbackOperatorOperation.Image)]
    [TestCase(QZoneLoopbackOperatorOperation.Delete)]
    public void RequestValidation_AcceptsEachDeclaredOperation(QZoneLoopbackOperatorOperation operation)
    {
        QZoneLoopbackOperatorResult result = new QZoneLoopbackOperatorRequest { Operation = operation }.Validate();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Code, Is.EqualTo(QZoneLoopbackOperatorResultCode.Accepted));
        });
    }

    [Test]
    public void RequestValidation_RejectsUnknownOperationWithSafeCode()
    {
        QZoneLoopbackOperatorResult result = new QZoneLoopbackOperatorRequest
        {
            Operation = (QZoneLoopbackOperatorOperation)999
        }.Validate();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(QZoneLoopbackOperatorResultCode.InvalidOperation));
        });
    }

    [TestCase("http://127.0.0.1:5101/")]
    [TestCase("http://localhost:5102/qzone/")]
    [TestCase("http://[::1]:5103/")]
    public void EndpointTryCreate_AcceptsAbsoluteHttpUrlForApprovedLoopbackHost(string value)
    {
        bool created = QZoneLoopbackOperatorEndpoint.TryCreate(
            value,
            out QZoneLoopbackOperatorEndpoint? endpoint,
            out QZoneLoopbackOperatorResultCode code);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.True);
            Assert.That(endpoint, Is.Not.Null);
            Assert.That(endpoint!.Uri, Is.EqualTo(new Uri(value)));
            Assert.That(code, Is.EqualTo(QZoneLoopbackOperatorResultCode.Accepted));
        });
    }

    [TestCase("http://127.0.0.1:5104/qzone")]
    [TestCase("http://localhost:5105/operator")]
    public void EndpointTryCreate_NormalizesAcceptedPrefixWithTrailingSlash(string value)
    {
        bool created = QZoneLoopbackOperatorEndpoint.TryCreate(
            value,
            out QZoneLoopbackOperatorEndpoint? endpoint,
            out QZoneLoopbackOperatorResultCode code);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.True);
            Assert.That(endpoint, Is.Not.Null);
            Assert.That(endpoint!.Uri.AbsoluteUri, Is.EqualTo(value + "/"));
            Assert.That(code, Is.EqualTo(QZoneLoopbackOperatorResultCode.Accepted));
        });
    }

    [TestCase("operator")]
    [TestCase("https://127.0.0.1:5101/")]
    [TestCase("http://192.168.1.10:5101/")]
    [TestCase("http://[::2]:5101/")]
    [TestCase("http://user:pass@127.0.0.1:5101/")]
    [TestCase("http://127.0.0.1:5101/?request=invalid")]
    [TestCase("http://127.0.0.1:5101/#fragment")]
    public void EndpointTryCreate_RejectsNonLocalOrAmbiguousUrlsWithSafeCode(string value)
    {
        bool created = QZoneLoopbackOperatorEndpoint.TryCreate(
            value,
            out QZoneLoopbackOperatorEndpoint? endpoint,
            out QZoneLoopbackOperatorResultCode code);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.False);
            Assert.That(endpoint, Is.Null);
            Assert.That(code, Is.EqualTo(QZoneLoopbackOperatorResultCode.InvalidEndpoint));
        });
    }

    [Test]
    public void ResultSerialization_ExposesOnlySucceededAndSafeCode()
    {
        QZoneLoopbackOperatorResult result = QZoneLoopbackOperatorResult.Rejected(
            QZoneLoopbackOperatorResultCode.InvalidOperation);

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(result));
        JsonElement root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(new[] { "succeeded", "code" }));
            Assert.That(root.GetProperty("succeeded").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("code").GetString(), Is.EqualTo("InvalidOperation"));
        });
    }
}
