using System.Net;
using System.Net.Http;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatRuntimeHealthReasonClassifierTests
{
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    public void Authentication_rejections_map_to_model_auth_rejected(HttpStatusCode statusCode)
    {
        HttpRequestException exception = new("model request failed", null, statusCode);

        Assert.That(
            QChatRuntimeHealthReasonClassifier.ForModelFailure(new InvalidOperationException("dispatch", exception)),
            Is.EqualTo("ModelAuthRejected"));
    }

    [Test]
    public void Non_authentication_model_failure_maps_to_health_probe_failed()
    {
        Assert.That(
            QChatRuntimeHealthReasonClassifier.ForModelFailure(new InvalidOperationException("transport")),
            Is.EqualTo("HealthProbeFailed"));
    }

    [Test]
    public void Onebot_connection_failure_maps_to_onebot_unavailable()
    {
        Assert.That(QChatRuntimeHealthReasonClassifier.ForOneBotConnectionFailure(), Is.EqualTo("OneBotUnavailable"));
    }
}
