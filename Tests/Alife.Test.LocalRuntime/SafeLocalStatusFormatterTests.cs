using Alife.Function.LocalRuntime;

namespace Alife.Test.LocalRuntime;

public sealed class SafeLocalStatusFormatterTests
{
    [Test]
    public void Format_never_emits_sensitive_or_path_text()
    {
        SafeLocalStatus status = new("degraded", new Dictionary<string, LocalAccountHealth>{{"account-a",LocalAccountHealth.Degraded}}, new Dictionary<CapabilityKind,string>{{CapabilityKind.Browser,"unavailable"}}, SafeReasonCode.DependencyUnavailable);
        string text = new SafeLocalStatusFormatter().Format(status, @"Bearer secret D:\Alife\storage\account-a SELECT * FROM chat");
        Assert.That(text, Does.Not.Contain("secret").And.Not.Contain(@"D:\").And.Not.Contain("SELECT"));
        Assert.That(text, Does.Contain("degraded").And.Contain("DependencyUnavailable"));
    }
}
