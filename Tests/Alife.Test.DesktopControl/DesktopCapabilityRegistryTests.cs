using Alife.Function.DesktopControl;

namespace Alife.Test.DesktopControl;

public sealed class DesktopCapabilityRegistryTests
{
    [Test]
    public void CreateDefault_RegistersOnlyReadOnlyCapabilities()
    {
        DesktopCapabilityRegistry registry = DesktopCapabilityRegistry.CreateDefault();

        IReadOnlyList<DesktopCapabilityDescriptor> capabilities = registry.GetAll();

        Assert.That(capabilities, Has.Count.EqualTo(14));
        Assert.That(capabilities.Select(capability => capability.Name), Is.EqualTo(new[]
        {
            "/qchat desktop status",
            "/qchat desktop health",
            "/qchat desktop processes",
            "/qchat desktop windows",
            "/qchat desktop audit recent",
            "/qchat desktop audit health",
            "/qchat desktop request <action>",
            "/qchat desktop drafts recent",
            "/qchat desktop draft reject <draft_id>",
            "/qchat desktop draft approve <draft_id>",
            "/qchat desktop draft execute <draft_id>",
            "/qchat desktop jobs recent",
            "/qchat desktop job <job_id>",
            "/qchat desktop file policy"
        }));
        Assert.That(capabilities.Count(capability => capability.Risk == DesktopCapabilityRisk.Low), Is.EqualTo(1));
        Assert.That(capabilities.Single(capability => capability.Risk == DesktopCapabilityRisk.Low).Name, Is.EqualTo("/qchat desktop draft execute <draft_id>"));
        Assert.That(capabilities.Where(capability => capability.Risk != DesktopCapabilityRisk.Low).All(capability => capability.Risk == DesktopCapabilityRisk.ReadOnly), Is.True);
        Assert.That(capabilities.All(capability => capability.Enabled), Is.True);
        Assert.That(registry.IsShellExecutionEnabled, Is.False);
        Assert.That(registry.IsMutationEnabled, Is.True);
    }

    [Test]
    public void FormatForOwner_ReportsDisabledMutationAndShellExecution()
    {
        DesktopCapabilityRegistry registry = DesktopCapabilityRegistry.CreateDefault();

        string text = registry.FormatForOwner();

        Assert.That(text, Does.Contain("desktop_capabilities=14"));
        Assert.That(text, Does.Contain("/qchat desktop status risk=ReadOnly enabled=true"));
        Assert.That(text, Does.Contain("/qchat desktop processes risk=ReadOnly enabled=true"));
        Assert.That(text, Does.Contain("/qchat desktop audit recent risk=ReadOnly enabled=true"));
        Assert.That(text, Does.Contain("/qchat desktop audit health risk=ReadOnly enabled=true"));
        Assert.That(text, Does.Contain("/qchat desktop request <action> risk=ReadOnly enabled=true"));
        Assert.That(text, Does.Contain("/qchat desktop drafts recent risk=ReadOnly enabled=true"));
        Assert.That(text, Does.Contain("/qchat desktop draft reject <draft_id> risk=ReadOnly enabled=true"));
        Assert.That(text, Does.Contain("/qchat desktop draft approve <draft_id> risk=ReadOnly enabled=true"));
        Assert.That(text, Does.Contain("/qchat desktop draft execute <draft_id> risk=Low enabled=true"));
        Assert.That(text, Does.Contain("/qchat desktop jobs recent risk=ReadOnly enabled=true"));
        Assert.That(text, Does.Contain("/qchat desktop job <job_id> risk=ReadOnly enabled=true"));
        Assert.That(text, Does.Contain("/qchat desktop file policy risk=ReadOnly enabled=true"));
        Assert.That(text, Does.Contain("desktop_mutation=enabled"));
        Assert.That(text, Does.Contain("shell_execution=disabled"));
        Assert.That(text, Does.Not.Contain("delete"));
        Assert.That(text, Does.Not.Contain("kill"));
    }
}
