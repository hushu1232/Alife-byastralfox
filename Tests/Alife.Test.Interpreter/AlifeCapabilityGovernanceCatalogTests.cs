using Alife.Function.FunctionCaller;

namespace Alife.Test.Interpreter;

[TestFixture]
public sealed class AlifeCapabilityGovernanceCatalogTests
{
    [Test]
    public void DefaultCatalogClassifiesCurrentPluginAreas()
    {
        IReadOnlyList<AlifeCapabilityGovernanceDescriptor> catalog =
            AlifeCapabilityGovernanceCatalog.CreateDefault();

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Select(item => item.Owner), Does.Contain("QChat"));
            Assert.That(catalog.Select(item => item.Owner), Does.Contain("FunctionCaller"));
            Assert.That(catalog.Select(item => item.Owner), Does.Contain("DataAgent"));
            Assert.That(catalog.Select(item => item.Owner), Does.Contain("Browser"));
            Assert.That(catalog.Select(item => item.Owner), Does.Contain("DesktopControl"));
            Assert.That(catalog.Select(item => item.Owner), Does.Contain("Memory"));
            Assert.That(catalog.Select(item => item.Owner), Does.Contain("Vision"));
            Assert.That(catalog.Select(item => item.Owner), Does.Contain("Speech"));
            Assert.That(catalog.Select(item => item.Owner), Does.Contain("DeskPet"));
            Assert.That(catalog.Select(item => item.Owner), Does.Contain("Emotion"));
            Assert.That(catalog.Select(item => item.Owner), Does.Contain("Mcp"));
            Assert.That(catalog.Select(item => item.Owner), Does.Contain("Developer"));
        });
    }

    [Test]
    public void DataAgentIsWorkflowCandidateButSafetyAuthorityRemainsDeterministic()
    {
        AlifeCapabilityGovernanceDescriptor dataAgent =
            AlifeCapabilityGovernanceCatalog.FindByOwner("DataAgent")!;

        Assert.Multiple(() =>
        {
            Assert.That(dataAgent.Role, Is.EqualTo(AlifeCapabilityGovernanceRole.AgentWorkflowCandidate));
            Assert.That(dataAgent.OrchestrationKind, Is.EqualTo(AlifeCapabilityOrchestrationKind.NativeWorkflow));
            Assert.That(dataAgent.RiskBoundary, Is.EqualTo(AlifeCapabilityRiskBoundary.DeterministicSafetyGate));
            Assert.That(dataAgent.Summary, Does.Contain("QueryPlan"));
            Assert.That(dataAgent.Summary, Does.Contain("SQL safety"));
        });
    }

    [Test]
    public void QChatIsInteractionSurfaceNotWorkflowOwner()
    {
        AlifeCapabilityGovernanceDescriptor qchat =
            AlifeCapabilityGovernanceCatalog.FindByOwner("QChat")!;

        Assert.Multiple(() =>
        {
            Assert.That(qchat.Role, Is.EqualTo(AlifeCapabilityGovernanceRole.InteractionSurface));
            Assert.That(qchat.OrchestrationKind, Is.EqualTo(AlifeCapabilityOrchestrationKind.None));
            Assert.That(qchat.RiskBoundary, Is.EqualTo(AlifeCapabilityRiskBoundary.OwnerGate));
        });
    }

    [Test]
    public void PerceptionAndPresentationPluginsAreNotAgentWorkflowCandidates()
    {
        string[] nonWorkflowOwners =
        [
            "Memory",
            "Vision",
            "Speech",
            "Auditory",
            "DeskPet",
            "Emotion",
            "VirtualWorld"
        ];

        IReadOnlyDictionary<string, AlifeCapabilityGovernanceDescriptor> catalog =
            AlifeCapabilityGovernanceCatalog.CreateDefault()
                .ToDictionary(item => item.Owner, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            foreach (string owner in nonWorkflowOwners)
            {
                Assert.That(catalog, Does.ContainKey(owner), owner);
                Assert.That(catalog[owner].Role,
                    Is.Not.EqualTo(AlifeCapabilityGovernanceRole.AgentWorkflowCandidate),
                    $"{owner} must not be treated as a free-form workflow agent.");
            }
        });
    }

    [Test]
    public void HighRiskCapabilitiesRequireApprovalOrSafetyGate()
    {
        IReadOnlyDictionary<string, AlifeCapabilityGovernanceDescriptor> catalog =
            AlifeCapabilityGovernanceCatalog.CreateDefault()
                .ToDictionary(item => item.Owner, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(catalog["DesktopControl"].RiskBoundary,
                Is.EqualTo(AlifeCapabilityRiskBoundary.ApprovalRequired));
            Assert.That(catalog["Developer"].RiskBoundary,
                Is.EqualTo(AlifeCapabilityRiskBoundary.ApprovalRequired));
            Assert.That(catalog["Mcp"].RiskBoundary,
                Is.EqualTo(AlifeCapabilityRiskBoundary.OwnerGate));
        });
    }

    [Test]
    public void FindByOwnerIsCaseInsensitiveAndReturnsNullForUnknownOwner()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AlifeCapabilityGovernanceCatalog.FindByOwner("dataagent"), Is.Not.Null);
            Assert.That(AlifeCapabilityGovernanceCatalog.FindByOwner("DATAAGENT"), Is.Not.Null);
            Assert.That(AlifeCapabilityGovernanceCatalog.FindByOwner("unknown-plugin"), Is.Null);
        });
    }
}
