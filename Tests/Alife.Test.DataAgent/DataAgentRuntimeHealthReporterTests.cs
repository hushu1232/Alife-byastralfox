using Alife.Function.DataAgent;
using System.Text.Json;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentRuntimeHealthReporterTests
{
    [Test]
    public void Runtime_health_event_rejects_unknown_reason_code()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataAgentRuntimeHealthEvent(
            "account-a",
            "model",
            DataAgentRuntimeHealthState.Unavailable,
            "raw exception"));
    }

    [Test]
    public void Runtime_health_event_rejects_unrecognized_account_or_component()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DataAgentRuntimeHealthEvent(
                "other-account",
                "model",
                DataAgentRuntimeHealthState.Unavailable,
                "ModelAuthRejected"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DataAgentRuntimeHealthEvent(
                "account-a",
                "unknown",
                DataAgentRuntimeHealthState.Unavailable,
                "ModelAuthRejected"));
        });
    }

    [Test]
    public void Runtime_health_event_accepts_model_auth_rejected()
    {
        DataAgentRuntimeHealthEvent value = new(
            "account-a",
            "model",
            DataAgentRuntimeHealthState.Unavailable,
            "ModelAuthRejected");

        Assert.That(value.ReasonCode, Is.EqualTo("ModelAuthRejected"));
    }

    [Test]
    public void Reporter_writes_only_changed_events_to_the_account_local_readiness_audit()
    {
        string storageRoot = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"runtime-health-{Guid.NewGuid():N}");
        DataAgentRuntimeHealthReporter reporter = DataAgentRuntimeHealthReporter.Create(storageRoot, "account-a");
        DataAgentRuntimeHealthEvent connected = new(
            "account-a",
            DataAgentRuntimeHealthEvent.OneBotComponent,
            DataAgentRuntimeHealthState.Healthy,
            "OneBotConnected");

        reporter.Report(connected);
        reporter.Report(connected);
        DataAgentRuntimeHealthReporter.Create(storageRoot, "account-a").Report(connected);
        reporter.Report(new(
            "account-a",
            DataAgentRuntimeHealthEvent.OneBotComponent,
            DataAgentRuntimeHealthState.Unavailable,
            "OneBotUnavailable"));

        IReadOnlyList<DataAgentRuntimeHealthEvent> audit = reporter.ReadAudit();
        string snapshotPath = Path.Combine(storageRoot, "runtime-health.json");
        using JsonDocument snapshot = JsonDocument.Parse(File.ReadAllText(snapshotPath));

        Assert.Multiple(() =>
        {
            Assert.That(audit, Has.Count.EqualTo(2));
            Assert.That(audit.Select(item => item.ReasonCode), Is.EqualTo(["OneBotConnected", "OneBotUnavailable"]));
            Assert.That(File.Exists(Path.Combine(storageRoot, "DataAgent", "dataagent.sqlite")), Is.True);
            Assert.That(snapshot.RootElement.GetProperty("account").GetString(), Is.EqualTo("account-a"));
            Assert.That(snapshot.RootElement.GetProperty("components")[0].GetProperty("reason").GetString(), Is.EqualTo("OneBotUnavailable"));
            Assert.That(File.Exists(snapshotPath + ".tmp"), Is.False);
        });
    }

    [Test]
    public void Reporter_returns_null_for_an_unknown_account()
    {
        Assert.That(DataAgentRuntimeHealthReporter.TryCreate(TestContext.CurrentContext.WorkDirectory, "unknown"), Is.Null);
    }

    [Test]
    public void Reporter_reuses_the_account_local_instance_and_merges_component_states()
    {
        string storageRoot = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"runtime-health-{Guid.NewGuid():N}");
        DataAgentRuntimeHealthReporter first = DataAgentRuntimeHealthReporter.Create(storageRoot, "account-a");
        DataAgentRuntimeHealthReporter second = DataAgentRuntimeHealthReporter.Create(storageRoot, "account-a");

        first.Report(new(
            "account-a",
            DataAgentRuntimeHealthEvent.OneBotComponent,
            DataAgentRuntimeHealthState.Healthy,
            "OneBotConnected"));
        second.Report(new(
            "account-a",
            DataAgentRuntimeHealthEvent.QZoneOperatorComponent,
            DataAgentRuntimeHealthState.Healthy,
            "QZoneOperatorReady"));
        using JsonDocument snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(storageRoot, "runtime-health.json")));

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(snapshot.RootElement.GetProperty("components").GetArrayLength(), Is.EqualTo(2));
        });
    }
}
