using Alife.Framework;
using Alife.Function.MessageFilter;

namespace Alife.Test.Framework;

public class ModuleHealthTests
{
    [Test]
    public void FormatHealthSnapshot_RendersStatusAndSummary()
    {
        ModuleHealth[] health = [
            new("Browser", ModuleHealthStatus.Healthy, "Browser runtime is ready."),
            new("QChat", ModuleHealthStatus.Degraded, "OneBot is configured but disconnected.")
        ];

        string report = SystemHealthService.FormatHealthSnapshot(health);

        Assert.That(report, Does.Contain("[Healthy] Browser: Browser runtime is ready."));
        Assert.That(report, Does.Contain("[Degraded] QChat: OneBot is configured but disconnected."));
    }

    [Test]
    public void GetHealthSnapshot_ReturnsUnavailableEntryWhenReporterThrows()
    {
        SystemHealthService service = new()
        {
            HealthReporterSourceOverride =
            [
                new StubHealthReporter("Memory", ModuleHealthStatus.Healthy, "Memory is ready."),
                new ThrowingHealthReporter()
            ]
        };

        IReadOnlyList<ModuleHealth> snapshot = service.GetHealthSnapshot();

        Assert.That(snapshot, Has.Count.EqualTo(2));
        Assert.That(snapshot[0].Name, Is.EqualTo("Memory"));
        Assert.That(snapshot[0].Status, Is.EqualTo(ModuleHealthStatus.Healthy));
        Assert.That(snapshot[1].Name, Is.EqualTo(nameof(ThrowingHealthReporter)));
        Assert.That(snapshot[1].Status, Is.EqualTo(ModuleHealthStatus.Unavailable));
        Assert.That(snapshot[1].Summary, Does.Contain("health check failed"));
    }

    [Test]
    public void FormatHealthSnapshot_ReturnsClearMessageForNoReporters()
    {
        string report = SystemHealthService.FormatHealthSnapshot([]);

        Assert.That(report, Is.EqualTo("No module health reporters are available."));
    }

    sealed record StubHealthReporter(
        string Name,
        ModuleHealthStatus Status,
        string Summary) : IModuleHealthReporter
    {
        public ModuleHealth GetHealth() => new(Name, Status, Summary);
    }

    sealed class ThrowingHealthReporter : IModuleHealthReporter
    {
        public ModuleHealth GetHealth() => throw new InvalidOperationException("broken reporter");
    }
}
