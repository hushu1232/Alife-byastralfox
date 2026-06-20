using Alife.Function.QChat;
using NUnit.Framework;
using System;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatInboundPipelineTests
{
    [Test]
    public void BuildContextCarriesRouteProfileAndOwnerRole()
    {
        QChatInboundPipeline pipeline = CreatePipeline();
        OneBotMessageEvent message = new()
        {
            UserId = 3045846738,
            RawMessage = " \u9192\u9192 "
        };

        QChatInboundContext context = pipeline.BuildContext(new QChatInboundEnvelope(2905391496, message));

        Assert.Multiple(() =>
        {
            Assert.That(context.Envelope.BotAccountId, Is.EqualTo(2905391496));
            Assert.That(context.Envelope.Message, Is.SameAs(message));
            Assert.That(context.Route.AgentId, Is.EqualTo("xiayu"));
            Assert.That(context.Route.IsOwner, Is.True);
            Assert.That(context.Profile.DisplayName, Is.EqualTo("\u590f\u7fbd"));
            Assert.That(context.RawText, Is.EqualTo("\u9192\u9192"));
        });
    }

    [Test]
    public void BuildContextForBasicMessageUsesEmptyRawText()
    {
        QChatInboundPipeline pipeline = CreatePipeline();
        OneBotBasicMessageEvent message = new()
        {
            UserId = 3045846738
        };

        QChatInboundContext context = pipeline.BuildContext(new QChatInboundEnvelope(2905391496, message));

        Assert.That(context.RawText, Is.Empty);
    }

    [Test]
    public void BuildContextForNullRawMessageUsesEmptyRawText()
    {
        QChatInboundPipeline pipeline = CreatePipeline();
        OneBotMessageEvent message = new()
        {
            UserId = 3045846738,
            RawMessage = null!
        };

        QChatInboundContext context = pipeline.BuildContext(new QChatInboundEnvelope(2905391496, message));

        Assert.That(context.RawText, Is.Empty);
    }

    [Test]
    public void BuildContextNullEnvelopeThrowsArgumentNullException()
    {
        QChatInboundPipeline pipeline = CreatePipeline();

        Assert.Throws<ArgumentNullException>(() => pipeline.BuildContext(null!));
    }

    [Test]
    public void BuildContextNullMessageThrowsArgumentNullException()
    {
        QChatInboundPipeline pipeline = CreatePipeline();
        QChatInboundEnvelope envelope = new(2905391496, null!);

        Assert.Throws<ArgumentNullException>(() => pipeline.BuildContext(envelope));
    }

    static QChatInboundPipeline CreatePipeline()
    {
        QChatAgentRouteService routes = new(new QChatAgentRouteConfig
        {
            OwnerUserId = 3045846738,
            BotAgents =
            {
                [2905391496] = "xiayu"
            }
        });

        return new QChatInboundPipeline(routes, QChatProfileService.CreateDefault());
    }
}
