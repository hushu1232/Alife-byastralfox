using System;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatSmartWebSearchPluginDetectorTests
{
    [Test]
    public void Detect_WhenDisabled_DoesNotClaimPluginIsLoaded()
    {
        QChatSmartWebSearchPluginStatus status = QChatSmartWebSearchPluginDetector.Detect(
            enabled: false,
            assemblyNames: ["Alife.Plugin.SmartWebSearch"]);

        Assert.Multiple(() =>
        {
            Assert.That(status.DetectionEnabled, Is.False);
            Assert.That(status.IsLoaded, Is.False);
            Assert.That(status.Code, Is.EqualTo("disabled"));
        });
    }

    [Test]
    public void Detect_WhenPluginIsAbsent_RemainsInformationalAndSearchIndependent()
    {
        QChatSmartWebSearchPluginStatus status = QChatSmartWebSearchPluginDetector.Detect(
            enabled: true,
            assemblyNames: []);

        Assert.Multiple(() =>
        {
            Assert.That(status.DetectionEnabled, Is.True);
            Assert.That(status.IsLoaded, Is.False);
            Assert.That(status.Code, Is.EqualTo("not_loaded"));
            Assert.That(status.Description, Does.Contain("QChat"));
        });
    }

    [Test]
    public void Detect_WhenPluginAssemblyNameMatchesIgnoringCase_ReportsLoaded()
    {
        QChatSmartWebSearchPluginStatus status = QChatSmartWebSearchPluginDetector.Detect(
            enabled: true,
            assemblyNames: ["alife.plugin.smartwebsearch"]);

        Assert.Multiple(() =>
        {
            Assert.That(status.DetectionEnabled, Is.True);
            Assert.That(status.IsLoaded, Is.True);
            Assert.That(status.Code, Is.EqualTo("loaded"));
        });
    }
}
