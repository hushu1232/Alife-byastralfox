namespace Alife.Function.DesktopControl;

public enum DesktopCapabilityRisk
{
    ReadOnly,
    Low,
    Medium,
    High,
    Critical
}

public sealed record DesktopCapabilityDescriptor(
    string Name,
    DesktopCapabilityRisk Risk,
    bool Enabled,
    string Summary);

public sealed class DesktopCapabilityRegistry(IReadOnlyList<DesktopCapabilityDescriptor> capabilities)
{
    readonly IReadOnlyList<DesktopCapabilityDescriptor> capabilities = capabilities;

    public bool IsShellExecutionEnabled { get; init; }
    public bool IsMutationEnabled { get; init; }

    public static DesktopCapabilityRegistry CreateDefault()
    {
        return new DesktopCapabilityRegistry([
            new DesktopCapabilityDescriptor("/qchat desktop status", DesktopCapabilityRisk.ReadOnly, true, "read-only desktop status"),
            new DesktopCapabilityDescriptor("/qchat desktop health", DesktopCapabilityRisk.ReadOnly, true, "read-only desktop health"),
            new DesktopCapabilityDescriptor("/qchat desktop processes", DesktopCapabilityRisk.ReadOnly, true, "read-only process summary"),
            new DesktopCapabilityDescriptor("/qchat desktop windows", DesktopCapabilityRisk.ReadOnly, true, "read-only window summary"),
            new DesktopCapabilityDescriptor("/qchat desktop audit recent", DesktopCapabilityRisk.ReadOnly, true, "recent desktop action audit summary"),
            new DesktopCapabilityDescriptor("/qchat desktop audit health", DesktopCapabilityRisk.ReadOnly, true, "desktop action audit health summary"),
            new DesktopCapabilityDescriptor("/qchat desktop request <action>", DesktopCapabilityRisk.ReadOnly, true, "create a pending desktop action draft without execution"),
            new DesktopCapabilityDescriptor("/qchat desktop drafts recent", DesktopCapabilityRisk.ReadOnly, true, "recent desktop action draft summary"),
            new DesktopCapabilityDescriptor("/qchat desktop draft reject <draft_id>", DesktopCapabilityRisk.ReadOnly, true, "reject a pending desktop action draft without execution"),
            new DesktopCapabilityDescriptor("/qchat desktop draft approve <draft_id>", DesktopCapabilityRisk.ReadOnly, true, "approve a pending desktop action draft without execution"),
            new DesktopCapabilityDescriptor("/qchat desktop draft execute <draft_id>", DesktopCapabilityRisk.Low, true, "queue an approved whitelisted desktop action draft for execution"),
            new DesktopCapabilityDescriptor("/qchat desktop jobs recent", DesktopCapabilityRisk.ReadOnly, true, "recent desktop business jobs summary"),
            new DesktopCapabilityDescriptor("/qchat desktop job <job_id>", DesktopCapabilityRisk.ReadOnly, true, "desktop business job detail"),
            new DesktopCapabilityDescriptor("/qchat desktop file policy", DesktopCapabilityRisk.ReadOnly, true, "desktop file access policy summary")
        ])
        {
            IsMutationEnabled = true
        };
    }

    public IReadOnlyList<DesktopCapabilityDescriptor> GetAll() => capabilities;

    public string FormatForOwner()
    {
        List<string> lines = [$"desktop_capabilities={capabilities.Count}"];
        lines.AddRange(capabilities.Select(capability =>
            $"{capability.Name} risk={capability.Risk} enabled={FormatBool(capability.Enabled)} summary={capability.Summary}"));
        lines.Add($"desktop_mutation={FormatEnabled(IsMutationEnabled)}");
        lines.Add($"shell_execution={FormatEnabled(IsShellExecutionEnabled)}");
        return string.Join(Environment.NewLine, lines);
    }

    static string FormatBool(bool value) => value ? "true" : "false";

    static string FormatEnabled(bool value) => value ? "enabled" : "disabled";
}
