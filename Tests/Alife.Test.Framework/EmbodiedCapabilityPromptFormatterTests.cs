using Alife.Framework;
using Alife.Function.MessageFilter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace Alife.Test.Framework;

public class EmbodiedCapabilityPromptFormatterTests
{
    [Test]
    public void Format_GroupsCapabilitiesByKindAndOmitsEmptyGroups()
    {
        IEmbodiedCapability[] capabilities = [
            new StubCapability("Desk pet", EmbodiedCapabilityKind.Body, "A visible Live2D body.", "Connected"),
            new StubCapability("Browser", EmbodiedCapabilityKind.Sense, "A real browser.", null)
        ];

        string prompt = EmbodiedCapabilityPromptFormatter.Format(capabilities);

        Assert.That(prompt, Does.Contain("## Body"));
        Assert.That(prompt, Does.Contain("- Desk pet: A visible Live2D body."));
        Assert.That(prompt, Does.Contain("Current state: Connected"));
        Assert.That(prompt, Does.Contain("## Senses"));
        Assert.That(prompt, Does.Contain("- Browser: A real browser."));
        Assert.That(prompt, Does.Not.Contain("## Memory"));
    }

    [Test]
    public void Format_SurvivesCapabilityStateFailure()
    {
        List<Exception> logged = new();
        IEmbodiedCapability[] capabilities = [
            new ThrowingCapability("Broken", EmbodiedCapabilityKind.Tool, "A failing state reporter.")
        ];

        string prompt = EmbodiedCapabilityPromptFormatter.Format(capabilities, logged.Add);

        Assert.That(prompt, Does.Contain("## Action Tools"));
        Assert.That(prompt, Does.Contain("- Broken: A failing state reporter."));
        Assert.That(logged, Has.Count.EqualTo(1));
    }

    [Test]
    public void Format_IncludesDigitalLifeFraming()
    {
        string prompt = EmbodiedCapabilityPromptFormatter.Format([]);

        Assert.That(prompt, Does.Contain("digital life running inside Alife"));
        Assert.That(prompt, Does.Contain("not an external menu"));
        Assert.That(prompt, Does.Contain("body, senses, and actions"));
    }

    [Test]
    public void Format_ProducesOnePromptForMultipleCapabilityKinds()
    {
        IEmbodiedCapability[] capabilities = [
            new StubCapability("Desk pet", EmbodiedCapabilityKind.Body, "A visible body.", "Ready"),
            new StubCapability("Voice", EmbodiedCapabilityKind.Expression, "A voice output channel.", "Idle"),
            new StubCapability("QQ", EmbodiedCapabilityKind.Communication, "A social messaging channel.", "Configured")
        ];

        string prompt = EmbodiedCapabilityPromptFormatter.Format(capabilities);

        Assert.That(prompt.Split("digital life running inside Alife").Length - 1, Is.EqualTo(1));
        Assert.That(prompt, Does.Contain("## Body"));
        Assert.That(prompt, Does.Contain("## Expression"));
        Assert.That(prompt, Does.Contain("## Communication"));
    }

    [Test]
    public void SelfContextService_ProvidesFormattedContextContribution()
    {
        SelfContextService service = new()
        {
            CapabilitySourceOverride =
            [
                new StubCapability("Desk pet", EmbodiedCapabilityKind.Body, "A visible body.", "Ready")
            ]
        };

        ContextContribution contribution = service.GetContextContributions().Single();

        Assert.That(contribution.Key, Is.EqualTo("self-context"));
        Assert.That(contribution.Priority, Is.GreaterThan(900));
        Assert.That(contribution.Content, Does.Contain("digital life running inside Alife"));
        Assert.That(contribution.Content, Does.Contain("- Desk pet: A visible body."));
        Assert.That(contribution.Content, Does.Contain("Current state: Ready"));
    }

    sealed record StubCapability(
        string Name,
        EmbodiedCapabilityKind Kind,
        string SelfDescription,
        string? State) : IEmbodiedCapability
    {
        public string? GetCurrentState() => State;
    }

    sealed record ThrowingCapability(
        string Name,
        EmbodiedCapabilityKind Kind,
        string SelfDescription) : IEmbodiedCapability
    {
        public string? GetCurrentState() => throw new InvalidOperationException("state failed");
    }
}
