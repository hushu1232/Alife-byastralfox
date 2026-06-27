using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentWebAccessRouterTests
{
    [Test]
    public void Evaluate_AllowsGroupMemberPublicSearchWhenEnabled()
    {
        AgentWebAccessDecision decision = AgentWebAccessRouter.Evaluate(new AgentWebAccessRequest(
            AgentWebAccessActorRole.GroupMember,
            AgentWebAccessCapability.PublicSearch,
            "dotnet release",
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                AllowGroupMemberPublicSearch = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.True);
            Assert.That(decision.Reason, Is.EqualTo("allowed"));
        });
    }

    [Test]
    public void Evaluate_DeniesGroupMemberPublicSearchWhenDisabled()
    {
        AgentWebAccessDecision decision = AgentWebAccessRouter.Evaluate(new AgentWebAccessRequest(
            AgentWebAccessActorRole.GroupMember,
            AgentWebAccessCapability.PublicSearch,
            "dotnet release",
            new AgentWebAccessConfig
            {
                EnablePublicSearch = false,
                AllowGroupMemberPublicSearch = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("public_search_disabled"));
        });
    }

    [Test]
    public void Evaluate_DeniesGroupMemberPublicFetchEvenWhenEnabled()
    {
        AgentWebAccessDecision decision = AgentWebAccessRouter.Evaluate(new AgentWebAccessRequest(
            AgentWebAccessActorRole.GroupMember,
            AgentWebAccessCapability.PublicFetch,
            "https://example.com",
            new AgentWebAccessConfig
            {
                EnablePublicFetch = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("owner_required"));
        });
    }

    [Test]
    public void Evaluate_AllowsOwnerPublicFetchWhenEnabled()
    {
        AgentWebAccessDecision decision = AgentWebAccessRouter.Evaluate(new AgentWebAccessRequest(
            AgentWebAccessActorRole.Owner,
            AgentWebAccessCapability.PublicFetch,
            "https://example.com",
            new AgentWebAccessConfig
            {
                EnablePublicFetch = true
            }));

        Assert.That(decision.Allowed, Is.True);
    }

    [Test]
    public void Evaluate_AllowsOwnerAutoReadWhenEnabled()
    {
        AgentWebAccessDecision decision = AgentWebAccessRouter.Evaluate(new AgentWebAccessRequest(
            AgentWebAccessActorRole.Owner,
            AgentWebAccessCapability.AutoRead,
            "https://example.com",
            new AgentWebAccessConfig
            {
                EnableAutoRead = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.True);
            Assert.That(decision.Reason, Is.EqualTo("allowed"));
        });
    }

    [Test]
    public void Evaluate_DeniesAutoReadWhenDisabled()
    {
        AgentWebAccessDecision decision = AgentWebAccessRouter.Evaluate(new AgentWebAccessRequest(
            AgentWebAccessActorRole.Owner,
            AgentWebAccessCapability.AutoRead,
            "https://example.com",
            new AgentWebAccessConfig
            {
                EnableAutoRead = false
            }));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("auto_read_disabled"));
        });
    }

    [Test]
    public void Evaluate_DeniesGroupMemberAutoReadEvenWhenEnabled()
    {
        AgentWebAccessDecision decision = AgentWebAccessRouter.Evaluate(new AgentWebAccessRequest(
            AgentWebAccessActorRole.GroupMember,
            AgentWebAccessCapability.AutoRead,
            "https://example.com",
            new AgentWebAccessConfig
            {
                EnableAutoRead = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("owner_required"));
        });
    }

    [Test]
    public void Evaluate_AllowsGroupMemberExternalRagQueryWhenEnabled()
    {
        AgentWebAccessDecision decision = AgentWebAccessRouter.Evaluate(new AgentWebAccessRequest(
            AgentWebAccessActorRole.GroupMember,
            AgentWebAccessCapability.ExternalRagQuery,
            "project boundary",
            new AgentWebAccessConfig
            {
                EnableExternalRagQuery = true,
                AllowGroupMemberExternalRagQuery = true
            }));

        Assert.That(decision.Allowed, Is.True);
    }

    [Test]
    public void Evaluate_DeniesGroupMemberExternalRagMutation()
    {
        AgentWebAccessDecision decision = AgentWebAccessRouter.Evaluate(new AgentWebAccessRequest(
            AgentWebAccessActorRole.GroupMember,
            AgentWebAccessCapability.ExternalRagMutation,
            "https://example.com/rag",
            new AgentWebAccessConfig
            {
                EnableExternalRagMutation = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("owner_required"));
        });
    }

    [TestCase(AgentWebAccessCapability.BrowserSnapshot)]
    [TestCase(AgentWebAccessCapability.BrowserInteract)]
    public void Evaluate_DeniesBrowserCapabilitiesForGroupMembers(AgentWebAccessCapability capability)
    {
        AgentWebAccessDecision decision = AgentWebAccessRouter.Evaluate(new AgentWebAccessRequest(
            AgentWebAccessActorRole.GroupMember,
            capability,
            "https://example.com",
            new AgentWebAccessConfig
            {
                EnableBrowserSnapshot = true,
                EnableBrowserInteract = true
            }));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("owner_required"));
        });
    }

    [Test]
    public void Evaluate_DeniesOverlongQuery()
    {
        AgentWebAccessDecision decision = AgentWebAccessRouter.Evaluate(new AgentWebAccessRequest(
            AgentWebAccessActorRole.Owner,
            AgentWebAccessCapability.PublicSearch,
            "123456",
            new AgentWebAccessConfig
            {
                EnablePublicSearch = true,
                MaxQueryChars = 5
            }));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("query_too_long"));
        });
    }
}
