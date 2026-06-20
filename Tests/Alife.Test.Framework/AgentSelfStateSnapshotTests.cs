using Alife.Framework;

namespace Alife.Test.Framework;

public class AgentSelfStateSnapshotTests
{
    [Test]
    public void FormatCompact_OrdersItemsByPriority()
    {
        AgentSelfStateSnapshot snapshot = new([
            new AgentSelfStateItem("browser", "ready", "Browser window is open.", Priority: 10),
            new AgentSelfStateItem("qq", "connected", "QQ OneBot is connected.", Priority: 100),
        ]);

        string compact = snapshot.FormatCompact(maxLength: 200);

        Assert.That(compact.IndexOf("qq: connected", StringComparison.Ordinal), Is.LessThan(
            compact.IndexOf("browser: ready", StringComparison.Ordinal)));
    }

    [Test]
    public void FormatCompact_RespectsMaxLength()
    {
        AgentSelfStateSnapshot snapshot = new([
            new AgentSelfStateItem("qq", "connected", "012345678901234567890123456789", Priority: 100),
        ]);

        string compact = snapshot.FormatCompact(maxLength: 24);

        Assert.That(compact.Length, Is.LessThanOrEqualTo(24));
        Assert.That(compact, Does.Contain("..."));
    }

    [Test]
    public void ToContextContribution_ReturnsTrustedHighPrioritySummary()
    {
        AgentSelfStateSnapshot snapshot = new([
            new AgentSelfStateItem("qq", "connected", "QQ OneBot is connected.", Priority: 100),
        ]);

        ContextContribution contribution = snapshot.ToContextContribution(maxLength: 200);

        Assert.That(contribution.Key, Is.EqualTo("agent.self-state"));
        Assert.That(contribution.Priority, Is.GreaterThanOrEqualTo(900));
        Assert.That(contribution.TrustLevel, Is.EqualTo(ContextTrustLevel.Trusted));
        Assert.That(contribution.Content, Does.Contain("QQ OneBot is connected."));
    }
}
