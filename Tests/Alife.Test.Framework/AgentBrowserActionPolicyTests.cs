using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentBrowserActionPolicyTests
{
    [Test]
    public void Evaluate_AllowsOwnerNavigatePublicHttps()
    {
        AgentBrowserActionDecision decision = AgentBrowserActionPolicy.Evaluate(new AgentBrowserActionPolicyRequest(
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.NavigatePublicUrl, "https://example.com/docs"),
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 5, MaxPages = 3 },
            StepIndex: 0,
            OpenedPageCount: 0));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.True);
            Assert.That(decision.Reason, Is.EqualTo("allowed"));
        });
    }

    [TestCase(AgentWebAccessActorRole.GroupMember)]
    [TestCase(AgentWebAccessActorRole.PrivateGuest)]
    [TestCase(AgentWebAccessActorRole.Unknown)]
    public void Evaluate_DeniesNonOwnerBrowserActions(AgentWebAccessActorRole role)
    {
        AgentBrowserActionDecision decision = AgentBrowserActionPolicy.Evaluate(new AgentBrowserActionPolicyRequest(
            role,
            new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.CaptureSnapshot, "https://example.com"),
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 5, MaxPages = 3 },
            StepIndex: 0,
            OpenedPageCount: 0));

        Assert.That(decision, Is.EqualTo(new AgentBrowserActionDecision(false, "browser_agent_owner_required")));
    }

    [TestCase("http://127.0.0.1:3000")]
    [TestCase("http://localhost:8080")]
    [TestCase("file:///C:/Users/test/secret.txt")]
    [TestCase("javascript:alert(1)")]
    [TestCase("data:text/html,hello")]
    [TestCase("http://10.0.0.5")]
    [TestCase("http://172.16.0.5")]
    [TestCase("http://192.168.1.20")]
    [TestCase("http://0.0.0.0")]
    [TestCase("http://100.64.0.1")]
    [TestCase("http://169.254.169.254")]
    [TestCase("http://198.18.0.1")]
    [TestCase("http://198.51.100.1")]
    [TestCase("http://203.0.113.1")]
    [TestCase("http://224.0.0.1")]
    [TestCase("http://[fc00::1]/")]
    [TestCase("http://[fd00::1]/")]
    [TestCase("http://[::ffff:10.0.0.1]/")]
    [TestCase("http://[::ffff:192.168.1.1]/")]
    [TestCase("http://[::ffff:169.254.169.254]/")]
    public void Evaluate_DeniesUnsafeNavigationTargets(string url)
    {
        AgentBrowserActionDecision decision = AgentBrowserActionPolicy.Evaluate(new AgentBrowserActionPolicyRequest(
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.NavigatePublicUrl, url),
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 5, MaxPages = 3 },
            StepIndex: 0,
            OpenedPageCount: 0));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.Reason, Is.EqualTo("browser_agent_unsafe_url"));
    }

    [TestCase(AgentBrowserAutomationActionKind.TypeText)]
    [TestCase(AgentBrowserAutomationActionKind.SubmitForm)]
    [TestCase(AgentBrowserAutomationActionKind.Download)]
    [TestCase(AgentBrowserAutomationActionKind.Upload)]
    [TestCase(AgentBrowserAutomationActionKind.Login)]
    [TestCase(AgentBrowserAutomationActionKind.ExecuteJsFromModel)]
    public void Evaluate_DeniesHighRiskActions(AgentBrowserAutomationActionKind kind)
    {
        AgentBrowserActionDecision decision = AgentBrowserActionPolicy.Evaluate(new AgentBrowserActionPolicyRequest(
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationAction(kind, "https://example.com"),
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 5, MaxPages = 3 },
            StepIndex: 0,
            OpenedPageCount: 0));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.Reason, Is.EqualTo("browser_agent_action_denied"));
    }

    [Test]
    public void Evaluate_AllowsOwnerPublicImageDownloadAction()
    {
        AgentBrowserActionDecision decision = AgentBrowserActionPolicy.Evaluate(new AgentBrowserActionPolicyRequest(
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.DownloadPublicImage, "https://example.com/cat.png"),
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 5, MaxPages = 3 },
            StepIndex: 0,
            OpenedPageCount: 0));

        Assert.That(decision.Allowed, Is.True);
    }

    [TestCase("file:///C:/Users/test/cat.png")]
    [TestCase("http://127.0.0.1/cat.png")]
    [TestCase("http://192.168.1.20/cat.png")]
    public void Evaluate_DeniesUnsafePublicImageDownloadActionTargets(string url)
    {
        AgentBrowserActionDecision decision = AgentBrowserActionPolicy.Evaluate(new AgentBrowserActionPolicyRequest(
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.DownloadPublicImage, url),
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 5, MaxPages = 3 },
            StepIndex: 0,
            OpenedPageCount: 0));

        Assert.That(decision, Is.EqualTo(new AgentBrowserActionDecision(false, "browser_agent_unsafe_url")));
    }

    [Test]
    public void Evaluate_AllowsOwnerPublicVideoLinkActionWithoutDownload()
    {
        AgentBrowserActionDecision decision = AgentBrowserActionPolicy.Evaluate(new AgentBrowserActionPolicyRequest(
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.ReturnPublicVideoLink, "https://example.com/demo.mp4"),
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 5, MaxPages = 3 },
            StepIndex: 0,
            OpenedPageCount: 0));

        Assert.That(decision.Allowed, Is.True);
    }

    [TestCase("file:///C:/Users/test/demo.mp4")]
    [TestCase("http://localhost/demo.mp4")]
    [TestCase("http://10.0.0.5/demo.mp4")]
    [TestCase("http://[::ffff:10.0.0.1]/demo.mp4")]
    public void Evaluate_DeniesUnsafePublicVideoLinkActionTargets(string url)
    {
        AgentBrowserActionDecision decision = AgentBrowserActionPolicy.Evaluate(new AgentBrowserActionPolicyRequest(
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.ReturnPublicVideoLink, url),
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 5, MaxPages = 3 },
            StepIndex: 0,
            OpenedPageCount: 0));

        Assert.That(decision, Is.EqualTo(new AgentBrowserActionDecision(false, "browser_agent_unsafe_url")));
    }

    [Test]
    public void Evaluate_DeniesWhenStepLimitReached()
    {
        AgentBrowserActionDecision decision = AgentBrowserActionPolicy.Evaluate(new AgentBrowserActionPolicyRequest(
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.CaptureSnapshot, "https://example.com"),
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 2, MaxPages = 3 },
            StepIndex: 2,
            OpenedPageCount: 0));

        Assert.That(decision, Is.EqualTo(new AgentBrowserActionDecision(false, "browser_agent_step_limit")));
    }

    [TestCase(AgentBrowserAutomationActionKind.NavigatePublicUrl)]
    [TestCase(AgentBrowserAutomationActionKind.ClickPublicLink)]
    [TestCase(AgentBrowserAutomationActionKind.ClickSamePageNavigation)]
    public void Evaluate_DeniesPageOpeningActionsWhenPageLimitReached(AgentBrowserAutomationActionKind kind)
    {
        AgentBrowserActionDecision decision = AgentBrowserActionPolicy.Evaluate(new AgentBrowserActionPolicyRequest(
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationAction(kind, "https://example.com/docs"),
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 5, MaxPages = 2 },
            StepIndex: 1,
            OpenedPageCount: 2));

        Assert.That(decision, Is.EqualTo(new AgentBrowserActionDecision(false, "browser_agent_page_limit")));
    }
}
