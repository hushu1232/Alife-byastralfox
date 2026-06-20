using Alife;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public class ControlCenterWebView2ProfileTests
{
    [Test]
    public void ResolveUserDataFolderCreatesDedicatedControlCenterProfile()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "control-center-webview2-profile-test");
        string expected = Path.Combine(root, "ControlCenter", "WebView2Data");

        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);

        string actual = ControlCenterWebView2Profile.ResolveUserDataFolder(root);

        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(Directory.Exists(actual), Is.True);
    }

    [Test]
    public void ResolveUserDataFolderUsesEnvironmentOverrideWhenProvided()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "control-center-webview2-profile-test-root");
        string overridePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "control-center-webview2-profile-override");

        if (Directory.Exists(overridePath))
            Directory.Delete(overridePath, recursive: true);

        string actual = ControlCenterWebView2Profile.ResolveUserDataFolder(root, overridePath);

        Assert.That(actual, Is.EqualTo(overridePath));
        Assert.That(Directory.Exists(actual), Is.True);
    }
}
