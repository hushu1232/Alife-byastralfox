using Alife.Function.FunctionCaller;

namespace Alife.Test.Interpreter;

public sealed class ToolCapabilityRouterTests
{
    [Test]
    public void ManifestStoresDomainIntentAndPreconditions()
    {
        ToolCapabilityManifest manifest = new(
            "dataagent_analysis_continue",
            ToolCapabilityDomain.DataAgent,
            "analysis_continue",
            ToolCapabilityRisk.Low,
            [ToolCapabilityPrecondition.ActiveDataAgentAnalysisSession],
            [ToolCapabilitySurface.OwnerPrivate, ToolCapabilitySurface.TrustedRuntime],
            ToolStateEffect.AppendsAnalysisTurn);

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Name, Is.EqualTo("dataagent_analysis_continue"));
            Assert.That(manifest.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(manifest.Intent, Is.EqualTo("analysis_continue"));
            Assert.That(manifest.Preconditions, Does.Contain(ToolCapabilityPrecondition.ActiveDataAgentAnalysisSession));
            Assert.That(manifest.Surfaces, Does.Contain(ToolCapabilitySurface.OwnerPrivate));
            Assert.That(manifest.StateEffect, Is.EqualTo(ToolStateEffect.AppendsAnalysisTurn));
        });
    }
}
