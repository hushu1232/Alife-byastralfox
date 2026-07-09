using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentCrossModulePlannerManifestTests
{
    [Test]
    public void DefaultManifestsArePlannerOnlyForExpectedModules()
    {
        IReadOnlyList<DataAgentCrossModulePlannerManifest> manifests =
            DataAgentCrossModulePlannerManifestFactory.CreateDefault();

        Assert.Multiple(() =>
        {
            Assert.That(manifests.Select(item => item.CapabilityName), Is.EquivalentTo(new[]
            {
                "qchat.intent_hint",
                "memory.candidate_summary",
                "browser.task_plan",
                "desktop.task_plan",
                "emotion.expression_hint",
                "deskpet.expression_hint"
            }));
            Assert.That(manifests.All(item => item.PlannerOnly), Is.True);
            Assert.That(manifests.All(item => item.AllowsExecution == false), Is.True);
            Assert.That(manifests.All(item => item.AllowsStateWrite == false), Is.True);
            Assert.That(manifests.All(item => item.AllowsVisibleText == false), Is.True);
        });
    }

    [Test]
    public void ValidatorAcceptsDefaultPlannerManifests()
    {
        IReadOnlyList<DataAgentCrossModulePlannerManifest> manifests =
            DataAgentCrossModulePlannerManifestFactory.CreateDefault();

        foreach (DataAgentCrossModulePlannerManifest manifest in manifests)
        {
            DataAgentCrossModulePlannerManifestValidationResult result =
                DataAgentCrossModulePlannerManifestValidator.Validate(manifest);

            Assert.That(result.Accepted, Is.True, manifest.CapabilityName);
            Assert.That(result.ReasonCode, Is.EqualTo("planner_manifest_accepted"), manifest.CapabilityName);
        }
    }

    [TestCase("qchat.send")]
    [TestCase("qq.ingress")]
    [TestCase("tool.execute")]
    [TestCase("sql.execute")]
    [TestCase("checkpoint.write")]
    [TestCase("memory.write")]
    [TestCase("browser.execute")]
    [TestCase("desktop.execute")]
    [TestCase("file.write")]
    [TestCase("voice.output")]
    [TestCase("tts.output")]
    [TestCase("audit.write")]
    [TestCase("progress.write")]
    [TestCase("diagnostics.write")]
    public void DefaultManifestsDenyForbiddenAuthorityMarkers(string forbiddenMarker)
    {
        IReadOnlyList<DataAgentCrossModulePlannerManifest> manifests =
            DataAgentCrossModulePlannerManifestFactory.CreateDefault();

        Assert.That(
            manifests.All(item => item.DeniedCapabilityMarkers.Contains(forbiddenMarker, StringComparer.Ordinal)),
            Is.True);
    }

    [Test]
    public void ValidatorRejectsManifestThatClaimsExecutionWriteOrVisibleAuthority()
    {
        DataAgentCrossModulePlannerManifest manifest = new(
            "desktop.task_plan",
            "desktop",
            PlannerOnly: true,
            AllowsExecution: true,
            AllowsStateWrite: true,
            AllowsVisibleText: true,
            AllowedAdvisoryActions: ["plan"],
            DeniedCapabilityMarkers: ["desktop.execute"],
            SafetyNotes: "unsafe");

        DataAgentCrossModulePlannerManifestValidationResult result =
            DataAgentCrossModulePlannerManifestValidator.Validate(manifest);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("planner_manifest_authority_claimed"));
        });
    }

    [Test]
    public void ValidatorRejectsManifestMissingRequiredDeniedMarkers()
    {
        DataAgentCrossModulePlannerManifest manifest = new(
            "memory.candidate_summary",
            "memory",
            PlannerOnly: true,
            AllowsExecution: false,
            AllowsStateWrite: false,
            AllowsVisibleText: false,
            AllowedAdvisoryActions: ["summarize"],
            DeniedCapabilityMarkers: ["memory.write"],
            SafetyNotes: "missing most denied markers");

        DataAgentCrossModulePlannerManifestValidationResult result =
            DataAgentCrossModulePlannerManifestValidator.Validate(manifest);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("planner_manifest_missing_denied_marker"));
        });
    }
}
