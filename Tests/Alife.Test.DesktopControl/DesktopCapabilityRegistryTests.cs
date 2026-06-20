using Alife.Function.DesktopControl;

namespace Alife.Test.DesktopControl;

public sealed class DesktopCapabilityRegistryTests
{
    [Test]
    public void CreateDefault_RegistersOnlyReadOnlyCapabilities()
    {
        DesktopCapabilityRegistry registry = DesktopCapabilityRegistry.CreateDefault();

        IReadOnlyList<DesktopCapabilityDescriptor> capabilities = registry.GetAll();

        Assert.That(capabilities, Has.Count.EqualTo(4));
        Assert.That(capabilities.Select(capability => capability.Name), Is.EqualTo(new[]
        {
            "/qchat desktop status",
            "/qchat desktop health",
            "/qchat desktop processes",
            "/qchat desktop windows"
        }));
        Assert.That(capabilities.All(capability => capability.Risk == DesktopCapabilityRisk.ReadOnly), Is.True);
        Assert.That(capabilities.All(capability => capability.Enabled), Is.True);
        Assert.That(registry.IsShellExecutionEnabled, Is.False);
        Assert.That(registry.IsMutationEnabled, Is.False);
    }

    [Test]
    public void FormatForOwner_ReportsDisabledMutationAndShellExecution()
    {
        DesktopCapabilityRegistry registry = DesktopCapabilityRegistry.CreateDefault();

        string text = registry.FormatForOwner();

        Assert.That(text, Does.Contain("desktop_capabilities=4"));
        Assert.That(text, Does.Contain("/qchat desktop status risk=ReadOnly enabled=true"));
        Assert.That(text, Does.Contain("/qchat desktop processes risk=ReadOnly enabled=true"));
        Assert.That(text, Does.Contain("desktop_mutation=disabled"));
        Assert.That(text, Does.Contain("shell_execution=disabled"));
        Assert.That(text, Does.Not.Contain("delete"));
        Assert.That(text, Does.Not.Contain("kill"));
    }
}
