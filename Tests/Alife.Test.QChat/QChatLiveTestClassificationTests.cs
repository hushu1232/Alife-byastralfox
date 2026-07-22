using NUnit.Framework;
using System.Reflection;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatLiveTestClassificationTests
{
    static readonly Type[] LiveFixtures =
    [
        typeof(QChatVoiceOutputLiveTests),
        typeof(QChatOwnerNotificationLiveTests),
        typeof(QChatModelReplyLoopLiveTests),
        typeof(QChatFunctionTests)
    ];

    [TestCaseSource(nameof(LiveFixtures))]
    public void LiveFixturesAreExplicitIntegrationTests(Type fixtureType)
    {
        string[] categories = fixtureType
            .GetCustomAttributes<CategoryAttribute>(inherit: false)
            .Select(attribute => attribute.Name)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(categories, Does.Contain("Integration"));
            Assert.That(categories, Does.Contain("Live"));
            Assert.That(
                fixtureType.GetCustomAttributes<ExplicitAttribute>(inherit: false),
                Is.Not.Empty);
        });
    }

    [Test]
    public void InjectedRuntimeAdapterSuiteIsIntegrationButNotLive()
    {
        string[] categories = typeof(QChatServiceAdapterTests)
            .GetCustomAttributes<CategoryAttribute>(inherit: false)
            .Select(attribute => attribute.Name)
            .ToArray();

        Assert.That(categories, Does.Contain("Integration"));
        Assert.That(categories, Does.Not.Contain("Live"));
        Assert.That(
            typeof(QChatServiceAdapterTests).GetCustomAttributes<ExplicitAttribute>(inherit: false),
            Is.Empty);
    }
}
