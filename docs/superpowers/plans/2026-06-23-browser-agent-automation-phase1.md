# Browser Agent Automation Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an owner-only bounded browser Agent that can open/search public pages, inspect read-only snapshots, follow safe public links, return public images as QQ images, return public videos as links, and return compact sourced QQ answers.

**Architecture:** Add a deterministic browser automation layer in `Alife.Function.MessageFilter`, governed by an explicit action policy and hard task limits. QChat only detects owner private browser requests, calls the automation service, formats the result, and denies all non-owner/group browser automation attempts before model dispatch. Media output remains narrow: public images may be downloaded to a D-drive runtime cache and sent as QQ images; public videos are never downloaded and are returned as links only.

**Tech Stack:** C#/.NET 9, existing `AgentWebAccessRouter`, `AgentPublicSearchService`, `IAgentBrowserProvider`, `AgentBrowserSiteExperienceStore`, `QChatService`, NUnit tests.

---

## File Structure

- Create `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserAutomationModels.cs`
  - Owns request/action/observation/result/evidence/config records.

- Create `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserActionPolicy.cs`
  - Owns actor, URL, action, step, and page-budget authorization.

- Create `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserTaskPlanner.cs`
  - Owns deterministic first-action planning and safe link selection.

- Create `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserAutomationService.cs`
  - Owns bounded execution loop and calls public search/browser snapshot providers.

- Create `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserMediaOutputService.cs`
  - Owns public image download validation and video-link-only validation.

- Create `sources/Alife.Function/Alife.Function.QChat/QChatBrowserAgentTriggerPolicy.cs`
  - Detects owner private browser automation requests and denies non-owner/group attempts.

- Create `sources/Alife.Function/Alife.Function.QChat/QChatBrowserAgentFormatter.cs`
  - Formats compact QQ output for browser automation results.

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatConfig.cs`
  - Add disabled-by-default or owner-only config switches and limits.

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - Wire owner private browser automation before normal model dispatch.

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
  - Add browser-agent status/smoke text.

- Create `Tests/Alife.Test.Framework/AgentBrowserActionPolicyTests.cs`
- Create `Tests/Alife.Test.Framework/AgentBrowserTaskPlannerTests.cs`
- Create `Tests/Alife.Test.Framework/AgentBrowserAutomationServiceTests.cs`
- Create `Tests/Alife.Test.Framework/AgentBrowserMediaOutputServiceTests.cs`
- Create `Tests/Alife.Test.QChat/QChatBrowserAgentTriggerPolicyTests.cs`
- Create `Tests/Alife.Test.QChat/QChatBrowserAgentFormatterTests.cs`
- Modify `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`
- Modify `docs/agent-browser-web-research.md`
- Modify `docs/browser-global-task-plan.md`

Upload hygiene:

- If implementation uses a worktree, the final upload must either sync the completed files back into `D:\Alife` or run the upload script from a source root whose `git ls-files` includes the full tracked project file set.
- Prefer final upload with default source root:

```powershell
powershell -ExecutionPolicy Bypass -File D:\Alife\tools\upload-alife-service-via-foxd.ps1
```

Do not upload from a partial worktree with fewer tracked files than `D:\Alife`; it can cause remote `alife-service` deletions.

---

### Task 1: Browser Automation Models And Action Policy

**Files:**
- Create: `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserAutomationModels.cs`
- Create: `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserActionPolicy.cs`
- Test: `Tests/Alife.Test.Framework/AgentBrowserActionPolicyTests.cs`

- [ ] **Step 1: Write failing action policy tests**

Create `Tests/Alife.Test.Framework/AgentBrowserActionPolicyTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run failing action policy tests**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter AgentBrowserActionPolicyTests
```

Expected:

```text
Compilation fails because AgentBrowserAutomationAction, AgentBrowserAutomationActionKind, AgentBrowserAutomationConfig, AgentBrowserActionPolicyRequest, AgentBrowserActionDecision, and AgentBrowserActionPolicy do not exist.
```

- [ ] **Step 3: Implement models**

Create `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserAutomationModels.cs`:

```csharp
namespace Alife.Function.Agent;

public enum AgentBrowserAutomationActionKind
{
    SearchPublicWeb,
    NavigatePublicUrl,
    CaptureSnapshot,
    Scroll,
    ClickPublicLink,
    ClickSamePageNavigation,
    GoBack,
    Stop,
    TypeText,
    SubmitForm,
    Download,
    Upload,
    DownloadPublicImage,
    ReturnPublicVideoLink,
    Login,
    ExecuteJsFromModel
}

public sealed class AgentBrowserAutomationConfig
{
    public bool Enabled { get; set; }
    public int MaxSteps { get; set; } = 5;
    public int MaxPages { get; set; } = 3;
    public int MaxLinksPerPage { get; set; } = 20;
    public int MaxTextCharsPerPage { get; set; } = 4000;
    public int MaxEvidenceItems { get; set; } = 3;
    public int MaxImageBytes { get; set; } = 20 * 1024 * 1024;
    public int MaxImageItems { get; set; } = 2;
    public string MediaCacheRoot { get; set; } = "";
}

public sealed record AgentBrowserAutomationRequest(
    string Task,
    AgentWebAccessActorRole ActorRole,
    AgentBrowserAutomationConfig Config,
    long ActorUserId = 0,
    long? GroupId = null);

public sealed record AgentBrowserAutomationAction(
    AgentBrowserAutomationActionKind Kind,
    string Target,
    string Reason = "");

public sealed record AgentBrowserObservation(
    string Url,
    string Title,
    string Text,
    IReadOnlyList<AgentBrowserSnapshotLink> Links,
    string Reason);

public sealed record AgentBrowserEvidence(
    string Title,
    string Url,
    string Summary);

public sealed record AgentBrowserAutomationStep(
    int Index,
    AgentBrowserAutomationAction Action,
    bool Allowed,
    string Reason,
    string? Url = null);

public sealed record AgentBrowserAutomationResult(
    bool Success,
    string Reason,
    string Answer,
    IReadOnlyList<AgentBrowserEvidence> Evidence,
    IReadOnlyList<AgentBrowserAutomationStep> Steps,
    int OpenedPageCount);

public sealed record AgentBrowserActionPolicyRequest(
    AgentWebAccessActorRole ActorRole,
    AgentBrowserAutomationAction Action,
    AgentBrowserAutomationConfig Config,
    int StepIndex,
    int OpenedPageCount);

public sealed record AgentBrowserActionDecision(
    bool Allowed,
    string Reason);
```

- [ ] **Step 4: Implement action policy**

Create `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserActionPolicy.cs`:

```csharp
using System.Net;

namespace Alife.Function.Agent;

public static class AgentBrowserActionPolicy
{
    static readonly HashSet<AgentBrowserAutomationActionKind> AllowedActions =
    [
        AgentBrowserAutomationActionKind.SearchPublicWeb,
        AgentBrowserAutomationActionKind.NavigatePublicUrl,
        AgentBrowserAutomationActionKind.CaptureSnapshot,
        AgentBrowserAutomationActionKind.Scroll,
        AgentBrowserAutomationActionKind.ClickPublicLink,
        AgentBrowserAutomationActionKind.ClickSamePageNavigation,
        AgentBrowserAutomationActionKind.GoBack,
        AgentBrowserAutomationActionKind.DownloadPublicImage,
        AgentBrowserAutomationActionKind.ReturnPublicVideoLink,
        AgentBrowserAutomationActionKind.Stop
    ];

    static readonly HashSet<AgentBrowserAutomationActionKind> UrlActions =
    [
        AgentBrowserAutomationActionKind.NavigatePublicUrl,
        AgentBrowserAutomationActionKind.ClickPublicLink,
        AgentBrowserAutomationActionKind.ClickSamePageNavigation,
        AgentBrowserAutomationActionKind.DownloadPublicImage,
        AgentBrowserAutomationActionKind.ReturnPublicVideoLink
    ];

    public static AgentBrowserActionDecision Evaluate(AgentBrowserActionPolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        AgentBrowserAutomationConfig config = request.Config ?? new AgentBrowserAutomationConfig();

        if (config.Enabled == false)
            return Deny("browser_agent_disabled");
        if (request.ActorRole != AgentWebAccessActorRole.Owner)
            return Deny("browser_agent_owner_required");
        if (request.StepIndex >= Math.Max(config.MaxSteps, 1))
            return Deny("browser_agent_step_limit");
        if (request.OpenedPageCount >= Math.Max(config.MaxPages, 1) &&
            OpensPage(request.Action.Kind))
            return Deny("browser_agent_page_limit");
        if (AllowedActions.Contains(request.Action.Kind) == false)
            return Deny("browser_agent_action_denied");
        if (UrlActions.Contains(request.Action.Kind) &&
            IsPublicHttpUrl(request.Action.Target) == false)
            return Deny("browser_agent_unsafe_url");

        return new AgentBrowserActionDecision(true, "allowed");
    }

    public static bool IsPublicHttpUrl(string? value)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) == false)
            return false;
        if (uri.Scheme is not ("http" or "https"))
            return false;
        if (uri.IsLoopback)
            return false;
        if (IPAddress.TryParse(uri.Host, out IPAddress? address))
        {
            byte[] bytes = address.GetAddressBytes();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return bytes[0] switch
                {
                    10 => false,
                    127 => false,
                    172 when bytes[1] >= 16 && bytes[1] <= 31 => false,
                    192 when bytes[1] == 168 => false,
                    _ => true
                };
            }
            return address.IsIPv6LinkLocal == false && address.IsIPv6SiteLocal == false;
        }
        return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) == false;
    }

    static bool OpensPage(AgentBrowserAutomationActionKind kind) =>
        kind is AgentBrowserAutomationActionKind.NavigatePublicUrl
            or AgentBrowserAutomationActionKind.ClickPublicLink
            or AgentBrowserAutomationActionKind.ClickSamePageNavigation;

    static AgentBrowserActionDecision Deny(string reason) => new(false, reason);
}
```

- [ ] **Step 5: Run action policy tests**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter AgentBrowserActionPolicyTests
```

Expected:

```text
Passed! - Failed: 0
```

---

### Task 2: Deterministic Browser Task Planner

**Files:**
- Create: `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserTaskPlanner.cs`
- Test: `Tests/Alife.Test.Framework/AgentBrowserTaskPlannerTests.cs`

- [ ] **Step 1: Write failing planner tests**

Create `Tests/Alife.Test.Framework/AgentBrowserTaskPlannerTests.cs`:

```csharp
using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentBrowserTaskPlannerTests
{
    [Test]
    public void PlanInitialAction_PublicUrl_ReturnsNavigate()
    {
        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.PlanInitialAction(
            "browse https://example.com/docs and summarize install steps");

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(AgentBrowserAutomationActionKind.NavigatePublicUrl));
            Assert.That(action.Target, Is.EqualTo("https://example.com/docs"));
        });
    }

    [Test]
    public void PlanInitialAction_NoUrl_ReturnsSearch()
    {
        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.PlanInitialAction(
            "browse vercel agent browser docs");

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(AgentBrowserAutomationActionKind.SearchPublicWeb));
            Assert.That(action.Target, Is.EqualTo("vercel agent browser docs"));
        });
    }

    [Test]
    public void SelectNextLink_PrefersDocsAndReadmeLinks()
    {
        AgentBrowserSnapshotLink[] links =
        [
            new("Pricing", "https://example.com/pricing"),
            new("Getting Started", "https://example.com/docs/getting-started"),
            new("Contact", "mailto:test@example.com")
        ];

        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.SelectNextLink(
            "install steps",
            links,
            visitedUrls: ["https://example.com"]);

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(AgentBrowserAutomationActionKind.ClickPublicLink));
            Assert.That(action.Target, Is.EqualTo("https://example.com/docs/getting-started"));
        });
    }

    [Test]
    public void SelectNextLink_NoSafeUsefulLink_ReturnsStop()
    {
        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.SelectNextLink(
            "install steps",
            [new AgentBrowserSnapshotLink("Email", "mailto:test@example.com")],
            visitedUrls: []);

        Assert.That(action.Kind, Is.EqualTo(AgentBrowserAutomationActionKind.Stop));
    }
}
```

- [ ] **Step 2: Run failing planner tests**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter AgentBrowserTaskPlannerTests
```

Expected:

```text
Compilation fails because AgentBrowserTaskPlanner does not exist.
```

- [ ] **Step 3: Implement planner**

Create `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserTaskPlanner.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Alife.Function.Agent;

public static class AgentBrowserTaskPlanner
{
    static readonly Regex UrlRegex = new(@"https?://[^\s<>()""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static AgentBrowserAutomationAction PlanInitialAction(string task)
    {
        string normalized = Normalize(task);
        Match match = UrlRegex.Match(normalized);
        if (match.Success)
            return new AgentBrowserAutomationAction(
                AgentBrowserAutomationActionKind.NavigatePublicUrl,
                TrimTrailingPunctuation(match.Value),
                "explicit_public_url");

        return new AgentBrowserAutomationAction(
            AgentBrowserAutomationActionKind.SearchPublicWeb,
            StripBrowseWords(normalized),
            "search_from_task");
    }

    public static AgentBrowserAutomationAction SelectNextLink(
        string task,
        IReadOnlyList<AgentBrowserSnapshotLink> links,
        IReadOnlySet<string> visitedUrls)
    {
        ArgumentNullException.ThrowIfNull(links);
        visitedUrls ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AgentBrowserSnapshotLink? best = links
            .Where(link => AgentBrowserActionPolicy.IsPublicHttpUrl(link.Url))
            .Where(link => visitedUrls.Contains(link.Url) == false)
            .Select(link => new { Link = link, Score = ScoreLink(task, link) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Link.Text.Length)
            .Select(item => item.Link)
            .FirstOrDefault();

        return best == null
            ? new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.Stop, "", "no_safe_useful_link")
            : new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.ClickPublicLink, best.Url, "selected_public_link");
    }

    static int ScoreLink(string task, AgentBrowserSnapshotLink link)
    {
        string haystack = (link.Text + " " + link.Url).ToLowerInvariant();
        int score = 0;
        if (haystack.Contains("docs")) score += 4;
        if (haystack.Contains("readme")) score += 4;
        if (haystack.Contains("getting-started")) score += 4;
        if (haystack.Contains("guide")) score += 3;
        if (haystack.Contains("install")) score += 3;
        if (haystack.Contains("api")) score += 2;
        if (haystack.Contains("github.com")) score += 2;

        foreach (string token in Normalize(task).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length >= 4 && haystack.Contains(token.ToLowerInvariant()))
                score++;
        }

        return score;
    }

    static string StripBrowseWords(string text)
    {
        string result = Regex.Replace(text, @"\b(open|browse|inspect|check|summarize|use|browser|website|page|site)\b", " ", RegexOptions.IgnoreCase);
        return Normalize(result);
    }

    static string Normalize(string value) =>
        Regex.Replace((value ?? "").Trim(), @"\s+", " ");

    static string TrimTrailingPunctuation(string value) =>
        value.TrimEnd('.', ',', ';', ':', ')', ']', '}');
}
```

- [ ] **Step 4: Run planner tests**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter AgentBrowserTaskPlannerTests
```

Expected:

```text
Passed! - Failed: 0
```

---

### Task 3: Browser Automation Service Core Loop

**Files:**
- Create: `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserAutomationService.cs`
- Test: `Tests/Alife.Test.Framework/AgentBrowserAutomationServiceTests.cs`

- [ ] **Step 1: Write failing automation service tests**

Create `Tests/Alife.Test.Framework/AgentBrowserAutomationServiceTests.cs`:

```csharp
using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentBrowserAutomationServiceTests
{
    [Test]
    public async Task ExecuteAsync_PublicUrlCapturesSnapshotAndReturnsEvidence()
    {
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(
            true,
            "ok",
            "https://example.com/docs",
            "Docs",
            "Install with dotnet tool install. Configure the API key after install.",
            [new AgentBrowserSnapshotLink("Getting Started", "https://example.com/docs/getting-started")]));
        AgentBrowserAutomationService service = new(browserProvider: browser);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse https://example.com/docs install steps",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(browser.Calls, Is.EqualTo(1));
            Assert.That(browser.LastRequest?.Url, Is.EqualTo("https://example.com/docs"));
            Assert.That(result.Evidence.Single().Summary, Does.Contain("Install"));
            Assert.That(result.Answer, Does.Contain("Docs"));
        });
    }

    [Test]
    public async Task ExecuteAsync_SearchesWhenTaskHasNoUrl()
    {
        FakePublicSearchProvider search = new(new AgentPublicSearchResult(
            "Project Docs",
            "https://example.com/project",
            "project docs snippet"));
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(
            true,
            "ok",
            "https://example.com/project",
            "Project Docs",
            "Project documentation content.",
            []));
        AgentBrowserAutomationService service = new(searchProvider: search, browserProvider: browser);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse project docs",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(search.Calls, Is.EqualTo(1));
            Assert.That(browser.LastRequest?.Url, Is.EqualTo("https://example.com/project"));
        });
    }

    [Test]
    public async Task ExecuteAsync_LoginWallStopsWithSafeReason()
    {
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(
            false,
            "login_required",
            "https://example.com/private",
            "Login",
            "Sign in required.",
            []));
        AgentBrowserAutomationService service = new(browserProvider: browser);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse https://example.com/private",
            AgentWebAccessActorRole.Owner,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_login_required"));
            Assert.That(result.Answer, Does.Not.Contain("Sign in required"));
        });
    }

    [Test]
    public async Task ExecuteAsync_DeniesNonOwnerBeforeBrowserProvider()
    {
        FakeBrowserProvider browser = new(new AgentBrowserSnapshot(true, "ok", "https://example.com", "x", "x", []));
        AgentBrowserAutomationService service = new(browserProvider: browser);

        AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
            "browse https://example.com",
            AgentWebAccessActorRole.GroupMember,
            new AgentBrowserAutomationConfig { Enabled = true, MaxSteps = 3, MaxPages = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_owner_required"));
            Assert.That(browser.Calls, Is.Zero);
        });
    }

    sealed class FakeBrowserProvider(AgentBrowserSnapshot snapshot) : IAgentBrowserProvider
    {
        public int Calls { get; private set; }
        public AgentBrowserSnapshotRequest? LastRequest { get; private set; }

        public Task<AgentBrowserSnapshot> CaptureSnapshotAsync(
            AgentBrowserSnapshotRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(snapshot);
        }
    }

    sealed class FakePublicSearchProvider(params AgentPublicSearchResult[] results) : IAgentPublicSearchProvider
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<AgentPublicSearchResult>> SearchAsync(
            string query,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<AgentPublicSearchResult>>(results);
        }
    }
}
```

- [ ] **Step 2: Run failing automation service tests**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter AgentBrowserAutomationServiceTests
```

Expected:

```text
Compilation fails because AgentBrowserAutomationService does not exist.
```

- [ ] **Step 3: Implement service**

Create `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserAutomationService.cs`:

```csharp
namespace Alife.Function.Agent;

public sealed class AgentBrowserAutomationService(
    IAgentBrowserProvider? browserProvider = null,
    IAgentPublicSearchProvider? searchProvider = null,
    AgentBrowserSiteExperienceStore? siteExperienceStore = null)
{
    readonly IAgentBrowserProvider? browserProvider = browserProvider;
    readonly IAgentPublicSearchProvider? searchProvider = searchProvider;
    readonly AgentBrowserSiteExperienceStore? siteExperienceStore = siteExperienceStore;

    public async Task<AgentBrowserAutomationResult> ExecuteAsync(
        AgentBrowserAutomationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Task))
            return Failure("browser_agent_empty_task", "No browser task was provided.", [], [], 0);

        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.PlanInitialAction(request.Task);
        List<AgentBrowserAutomationStep> steps = [];
        List<AgentBrowserEvidence> evidence = [];
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        int openedPages = 0;

        for (int step = 0; step < Math.Max(request.Config.MaxSteps, 1); step++)
        {
            AgentBrowserActionDecision decision = AgentBrowserActionPolicy.Evaluate(new AgentBrowserActionPolicyRequest(
                request.ActorRole,
                action,
                request.Config,
                step,
                openedPages));
            steps.Add(new AgentBrowserAutomationStep(step, action, decision.Allowed, decision.Reason, action.Target));

            if (decision.Allowed == false)
                return Failure(decision.Reason, FormatFailure(decision.Reason), evidence, steps, openedPages);

            if (action.Kind == AgentBrowserAutomationActionKind.Stop)
                break;

            if (action.Kind == AgentBrowserAutomationActionKind.SearchPublicWeb)
            {
                action = await SearchToNavigationActionAsync(action.Target, cancellationToken);
                continue;
            }

            if (action.Kind is AgentBrowserAutomationActionKind.NavigatePublicUrl
                or AgentBrowserAutomationActionKind.ClickPublicLink
                or AgentBrowserAutomationActionKind.ClickSamePageNavigation)
            {
                if (browserProvider == null)
                    return Failure("browser_agent_runtime_unavailable", "Browser runtime is unavailable.", evidence, steps, openedPages);

                openedPages++;
                visited.Add(action.Target);
                AgentBrowserSnapshot snapshot = await browserProvider.CaptureSnapshotAsync(new AgentBrowserSnapshotRequest(
                    action.Target,
                    MaxTextChars: Math.Max(request.Config.MaxTextCharsPerPage, 0),
                    MaxElements: Math.Max(request.Config.MaxLinksPerPage, 0)), cancellationToken);

                if (snapshot.Success == false)
                {
                    string reason = snapshot.Reason switch
                    {
                        "login_required" => "browser_agent_login_required",
                        "anti_bot_challenge" => "browser_agent_anti_bot_challenge",
                        _ => "browser_agent_runtime_unavailable"
                    };
                    return Failure(reason, FormatFailure(reason), evidence, steps, openedPages);
                }

                evidence.Add(new AgentBrowserEvidence(
                    Compact(snapshot.Title, 80),
                    snapshot.Url,
                    Compact(snapshot.Text, 220)));

                if (evidence.Count >= Math.Max(request.Config.MaxEvidenceItems, 1))
                    break;

                action = AgentBrowserTaskPlanner.SelectNextLink(request.Task, snapshot.Links, visited);
                continue;
            }
        }

        if (evidence.Count == 0)
            return Failure("browser_agent_no_reliable_evidence", "No reliable browser evidence was found.", evidence, steps, openedPages);

        return new AgentBrowserAutomationResult(
            true,
            "ok",
            ComposeAnswer(evidence),
            evidence,
            steps,
            openedPages);
    }

    async Task<AgentBrowserAutomationAction> SearchToNavigationActionAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (searchProvider == null)
            return new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.Stop, "", "search_not_configured");

        IReadOnlyList<AgentPublicSearchResult> results = await searchProvider.SearchAsync(query, 5, cancellationToken);
        AgentPublicSearchResult? result = results.FirstOrDefault(item => AgentBrowserActionPolicy.IsPublicHttpUrl(item.Url));
        return result == null
            ? new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.Stop, "", "no_public_search_result")
            : new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.NavigatePublicUrl, result.Url, "search_result");
    }

    static AgentBrowserAutomationResult Failure(
        string reason,
        string answer,
        IReadOnlyList<AgentBrowserEvidence> evidence,
        IReadOnlyList<AgentBrowserAutomationStep> steps,
        int openedPages) =>
        new(false, reason, answer, evidence, steps, openedPages);

    static string ComposeAnswer(IReadOnlyList<AgentBrowserEvidence> evidence)
    {
        AgentBrowserEvidence first = evidence[0];
        return $"Conclusion: {first.Summary}{Environment.NewLine}Sources: {string.Join(" / ", evidence.Select(item => item.Url).Distinct())}";
    }

    static string FormatFailure(string reason) => reason switch
    {
        "browser_agent_owner_required" => "Browser automation is owner-only.",
        "browser_agent_disabled" => "Browser automation is disabled.",
        "browser_agent_login_required" => "Cannot use that page because it requires login.",
        "browser_agent_anti_bot_challenge" => "Cannot use that page because it shows anti-bot verification.",
        "browser_agent_step_limit" => "Browser task stopped at the step limit.",
        "browser_agent_page_limit" => "Browser task stopped at the page limit.",
        "browser_agent_unsafe_url" => "That browser target is not a safe public URL.",
        _ => "Browser automation failed."
    };

    static string Compact(string value, int maxChars)
    {
        value = string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length <= maxChars ? value : value[..maxChars].TrimEnd() + "...";
    }
}
```

- [ ] **Step 4: Run automation service tests**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter AgentBrowserAutomationServiceTests
```

Expected:

```text
Passed! - Failed: 0
```

---

### Task 3A: Image Output And Video Link Policy

**Files:**
- Create: `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserMediaOutputService.cs`
- Test: `Tests/Alife.Test.Framework/AgentBrowserMediaOutputServiceTests.cs`

- [ ] **Step 1: Write failing media output tests**

Create `Tests/Alife.Test.Framework/AgentBrowserMediaOutputServiceTests.cs`:

```csharp
using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentBrowserMediaOutputServiceTests
{
    [Test]
    public async Task PrepareAsync_PublicPng_DownloadsImageToDDriveRuntimeCache()
    {
        string root = Path.Combine("D:\\tmp", "alife-browser-media", Guid.NewGuid().ToString("N"));
        FakeMediaFetcher fetcher = new("image/png", [1, 2, 3, 4]);
        AgentBrowserMediaOutputService service = new(fetcher.FetchAsync, root);

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            "https://example.com/cat.png",
            AgentBrowserMediaOutputKind.Image,
            new AgentBrowserAutomationConfig { Enabled = true, MaxImageBytes = 1024, MediaCacheRoot = root }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Kind, Is.EqualTo(AgentBrowserMediaOutputKind.Image));
            Assert.That(result.LocalPath, Does.StartWith(root));
            Assert.That(File.Exists(result.LocalPath!), Is.True);
            Assert.That(result.ReturnText, Is.Empty);
        });
    }

    [Test]
    public async Task PrepareAsync_PublicVideo_ReturnsLinkOnlyWithoutFetchingBody()
    {
        string root = Path.Combine("D:\\tmp", "alife-browser-media", Guid.NewGuid().ToString("N"));
        FakeMediaFetcher fetcher = new("video/mp4", [1, 2, 3, 4]);
        AgentBrowserMediaOutputService service = new(fetcher.FetchAsync, root);

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            "https://example.com/demo.mp4",
            AgentBrowserMediaOutputKind.VideoLink,
            new AgentBrowserAutomationConfig { Enabled = true, MaxImageBytes = 1024, MediaCacheRoot = root }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Kind, Is.EqualTo(AgentBrowserMediaOutputKind.VideoLink));
            Assert.That(result.LocalPath, Is.Null);
            Assert.That(result.ReturnText, Is.EqualTo("https://example.com/demo.mp4"));
            Assert.That(fetcher.Calls, Is.Zero);
        });
    }

    [TestCase("https://example.com/archive.zip")]
    [TestCase("https://example.com/script.js")]
    [TestCase("file:///C:/secret.png")]
    [TestCase("http://127.0.0.1/cat.png")]
    public async Task PrepareAsync_DeniesUnsafeOrNonMediaTargets(string url)
    {
        string root = Path.Combine("D:\\tmp", "alife-browser-media", Guid.NewGuid().ToString("N"));
        AgentBrowserMediaOutputService service = new((_, _) => Task.FromResult(new AgentBrowserMediaFetchResult("application/octet-stream", [1])), root);

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            url,
            AgentBrowserMediaOutputKind.Image,
            new AgentBrowserAutomationConfig { Enabled = true, MaxImageBytes = 1024, MediaCacheRoot = root }));

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task PrepareAsync_OversizedImageFailsBeforeWrite()
    {
        string root = Path.Combine("D:\\tmp", "alife-browser-media", Guid.NewGuid().ToString("N"));
        AgentBrowserMediaOutputService service = new((_, _) => Task.FromResult(new AgentBrowserMediaFetchResult("image/png", [1, 2, 3])), root);

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            "https://example.com/cat.png",
            AgentBrowserMediaOutputKind.Image,
            new AgentBrowserAutomationConfig { Enabled = true, MaxImageBytes = 2, MediaCacheRoot = root }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_media_too_large"));
            Assert.That(Directory.Exists(root), Is.False);
        });
    }

    sealed class FakeMediaFetcher(string contentType, byte[] bytes)
    {
        public int Calls { get; private set; }

        public Task<AgentBrowserMediaFetchResult> FetchAsync(string url, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AgentBrowserMediaFetchResult(contentType, bytes));
        }
    }
}
```

- [ ] **Step 2: Run failing media output tests**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter AgentBrowserMediaOutputServiceTests
```

Expected:

```text
Compilation fails because AgentBrowserMediaOutputService and related media records do not exist.
```

- [ ] **Step 3: Add media records to automation models**

Append to `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserAutomationModels.cs`:

```csharp
public enum AgentBrowserMediaOutputKind
{
    Image,
    VideoLink
}

public sealed record AgentBrowserMediaOutputRequest(
    string Url,
    AgentBrowserMediaOutputKind Kind,
    AgentBrowserAutomationConfig Config);

public sealed record AgentBrowserMediaFetchResult(
    string ContentType,
    byte[] Bytes);

public sealed record AgentBrowserMediaOutputResult(
    bool Success,
    string Reason,
    AgentBrowserMediaOutputKind Kind,
    string Url,
    string? LocalPath,
    string ReturnText);
```

- [ ] **Step 4: Implement media output service**

Create `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserMediaOutputService.cs`:

```csharp
namespace Alife.Function.Agent;

public sealed class AgentBrowserMediaOutputService(
    Func<string, CancellationToken, Task<AgentBrowserMediaFetchResult>>? fetcher = null,
    string? defaultRoot = null)
{
    static readonly HashSet<string> ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    static readonly HashSet<string> VideoExtensions = [".mp4", ".webm", ".mov", ".m4v"];

    readonly Func<string, CancellationToken, Task<AgentBrowserMediaFetchResult>> fetcher =
        fetcher ?? DefaultFetchAsync;
    readonly string defaultRoot = defaultRoot ?? Path.Combine("D:\\Alife", "Runtime", "BrowserAgentMedia");

    public async Task<AgentBrowserMediaOutputResult> PrepareAsync(
        AgentBrowserMediaOutputRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (AgentBrowserActionPolicy.IsPublicHttpUrl(request.Url) == false)
            return Failure(request, "browser_agent_unsafe_url");

        string extension = Path.GetExtension(new Uri(request.Url).AbsolutePath).ToLowerInvariant();
        if (request.Kind == AgentBrowserMediaOutputKind.VideoLink)
        {
            return VideoExtensions.Contains(extension)
                ? new AgentBrowserMediaOutputResult(true, "ok", request.Kind, request.Url, null, request.Url)
                : Failure(request, "browser_agent_media_type_denied");
        }

        if (ImageExtensions.Contains(extension) == false)
            return Failure(request, "browser_agent_media_type_denied");

        AgentBrowserMediaFetchResult fetched;
        try
        {
            fetched = await fetcher(request.Url, cancellationToken);
        }
        catch
        {
            return Failure(request, "browser_agent_media_download_failed");
        }

        if (IsImageContentType(fetched.ContentType) == false)
            return Failure(request, "browser_agent_media_type_denied");
        if (fetched.Bytes.Length > Math.Max(request.Config.MaxImageBytes, 1))
            return Failure(request, "browser_agent_media_too_large");

        string root = string.IsNullOrWhiteSpace(request.Config.MediaCacheRoot)
            ? defaultRoot
            : request.Config.MediaCacheRoot;
        Directory.CreateDirectory(root);
        string fileName = $"{DateTimeOffset.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{extension}";
        string path = Path.Combine(root, fileName);
        await File.WriteAllBytesAsync(path, fetched.Bytes, cancellationToken);
        return new AgentBrowserMediaOutputResult(true, "ok", request.Kind, request.Url, path, "");
    }

    static bool IsImageContentType(string value) =>
        value.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    static AgentBrowserMediaOutputResult Failure(AgentBrowserMediaOutputRequest request, string reason) =>
        new(false, reason, request.Kind, request.Url, null, "");

    static async Task<AgentBrowserMediaFetchResult> DefaultFetchAsync(string url, CancellationToken cancellationToken)
    {
        using HttpClient client = new();
        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new AgentBrowserMediaFetchResult(contentType, bytes);
    }
}
```

- [ ] **Step 5: Run media output tests**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter AgentBrowserMediaOutputServiceTests
```

Expected:

```text
Passed! - Failed: 0
```

---

### Task 4: QChat Trigger Policy And Formatter

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatBrowserAgentTriggerPolicy.cs`
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatBrowserAgentFormatter.cs`
- Test: `Tests/Alife.Test.QChat/QChatBrowserAgentTriggerPolicyTests.cs`
- Test: `Tests/Alife.Test.QChat/QChatBrowserAgentFormatterTests.cs`

- [ ] **Step 1: Write failing trigger and formatter tests**

Create `Tests/Alife.Test.QChat/QChatBrowserAgentTriggerPolicyTests.cs`:

```csharp
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatBrowserAgentTriggerPolicyTests
{
    [Test]
    public void Parse_OwnerPrivateBrowserRequest_ReturnsCommand()
    {
        QChatBrowserAgentTrigger trigger = QChatBrowserAgentTriggerPolicy.Parse(
            OneBotMessageType.Private,
            QChatSenderRole.Owner,
            "browse https://example.com/docs");

        Assert.Multiple(() =>
        {
            Assert.That(trigger.Kind, Is.EqualTo(QChatBrowserAgentTriggerKind.RunBrowserTask));
            Assert.That(trigger.Task, Is.EqualTo("browse https://example.com/docs"));
        });
    }

    [TestCase(QChatSenderRole.GroupMember)]
    [TestCase(QChatSenderRole.PrivateGuest)]
    public void Parse_NonOwnerBrowserRequest_ReturnsDenied(QChatSenderRole role)
    {
        QChatBrowserAgentTrigger trigger = QChatBrowserAgentTriggerPolicy.Parse(
            OneBotMessageType.Private,
            role,
            "browse https://example.com/docs");

        Assert.That(trigger.Kind, Is.EqualTo(QChatBrowserAgentTriggerKind.Denied));
    }

    [Test]
    public void Parse_GroupOwnerMention_DoesNotRunBrowserAutomation()
    {
        QChatBrowserAgentTrigger trigger = QChatBrowserAgentTriggerPolicy.Parse(
            OneBotMessageType.Group,
            QChatSenderRole.Owner,
            "[CQ:at,qq=999] browse https://example.com/docs");

        Assert.That(trigger.Kind, Is.EqualTo(QChatBrowserAgentTriggerKind.None));
    }

    [Test]
    public void Parse_SearchOnlyRequest_DoesNotStealWebResearch()
    {
        QChatBrowserAgentTrigger trigger = QChatBrowserAgentTriggerPolicy.Parse(
            OneBotMessageType.Private,
            QChatSenderRole.Owner,
            "search dotnet release notes");

        Assert.That(trigger.Kind, Is.EqualTo(QChatBrowserAgentTriggerKind.None));
    }
}
```

Create `Tests/Alife.Test.QChat/QChatBrowserAgentFormatterTests.cs`:

```csharp
using Alife.Function.Agent;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatBrowserAgentFormatterTests
{
    [Test]
    public void Format_Success_ReturnsCompactSourcedReply()
    {
        AgentBrowserAutomationResult result = new(
            true,
            "ok",
            "raw answer that should not be trusted as-is",
            [
                new AgentBrowserEvidence("Docs", "https://example.com/docs", "Install with dotnet tool install."),
                new AgentBrowserEvidence("Guide", "https://example.com/guide", "Configure the API key.")
            ],
            [],
            2);

        string text = QChatBrowserAgentFormatter.Format(result);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("Conclusion:"));
            Assert.That(text, Does.Contain("Docs"));
            Assert.That(text, Does.Contain("https://example.com/docs"));
            Assert.That(text.Length, Is.LessThanOrEqualTo(760));
        });
    }

    [Test]
    public void Format_LoginRequired_ReturnsShortBoundary()
    {
        AgentBrowserAutomationResult result = new(false, "browser_agent_login_required", "", [], [], 1);

        string text = QChatBrowserAgentFormatter.Format(result);

        Assert.That(text, Is.EqualTo("Cannot use that page because it requires login."));
    }
}
```

- [ ] **Step 2: Run failing QChat policy/formatter tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatBrowserAgentTriggerPolicyTests|QChatBrowserAgentFormatterTests"
```

Expected:

```text
Compilation fails because QChatBrowserAgentTriggerPolicy and QChatBrowserAgentFormatter do not exist.
```

- [ ] **Step 3: Implement trigger policy**

Create `sources/Alife.Function/Alife.Function.QChat/QChatBrowserAgentTriggerPolicy.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

public enum QChatBrowserAgentTriggerKind
{
    None,
    RunBrowserTask,
    Denied
}

public sealed record QChatBrowserAgentTrigger(
    QChatBrowserAgentTriggerKind Kind,
    string Task = "",
    string Reason = "");

public static class QChatBrowserAgentTriggerPolicy
{
    static readonly Regex BrowserIntent = new(
        @"\b(open|browse|inspect|browser|website|web page|readme|official site|docs|documentation)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex SearchOnly = new(
        @"^\s*(search|look up|find|google|bing)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static QChatBrowserAgentTrigger Parse(
        OneBotMessageType messageType,
        QChatSenderRole senderRole,
        string rawText)
    {
        string text = OneBotSegment.GetPlainText(rawText ?? "").Trim();
        if (text.Length == 0)
            return new QChatBrowserAgentTrigger(QChatBrowserAgentTriggerKind.None);
        if (BrowserIntent.IsMatch(text) == false)
            return new QChatBrowserAgentTrigger(QChatBrowserAgentTriggerKind.None);
        if (SearchOnly.IsMatch(text))
            return new QChatBrowserAgentTrigger(QChatBrowserAgentTriggerKind.None);
        if (messageType != OneBotMessageType.Private)
            return new QChatBrowserAgentTrigger(QChatBrowserAgentTriggerKind.None);
        if (senderRole != QChatSenderRole.Owner)
            return new QChatBrowserAgentTrigger(QChatBrowserAgentTriggerKind.Denied, Reason: "browser_agent_owner_required");

        return new QChatBrowserAgentTrigger(QChatBrowserAgentTriggerKind.RunBrowserTask, text, "owner_private_browser_request");
    }
}
```

- [ ] **Step 4: Implement formatter**

Create `sources/Alife.Function/Alife.Function.QChat/QChatBrowserAgentFormatter.cs`:

```csharp
using Alife.Function.Agent;

namespace Alife.Function.QChat;

public static class QChatBrowserAgentFormatter
{
    public static string Format(AgentBrowserAutomationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Success == false)
            return FormatFailure(result.Reason);

        List<string> lines = ["Conclusion: " + Compact(result.Evidence.FirstOrDefault()?.Summary ?? result.Answer, 180)];
        foreach (AgentBrowserEvidence item in result.Evidence.Take(3))
            lines.Add($"- {Compact(item.Title, 40)}: {Compact(item.Summary, 100)}");

        string sources = string.Join(" / ", result.Evidence.Take(3).Select(item => item.Url).Distinct());
        if (sources.Length > 0)
            lines.Add("Sources: " + sources);

        return Limit(string.Join(Environment.NewLine, lines), 760);
    }

    static string FormatFailure(string reason) => reason switch
    {
        "browser_agent_owner_required" => "Browser automation is owner-only.",
        "browser_agent_disabled" => "Browser automation is disabled.",
        "browser_agent_login_required" => "Cannot use that page because it requires login.",
        "browser_agent_anti_bot_challenge" => "Cannot use that page because it shows anti-bot verification.",
        "browser_agent_unsafe_url" => "That browser target is not a safe public URL.",
        "browser_agent_step_limit" => "Browser task stopped at the step limit.",
        "browser_agent_page_limit" => "Browser task stopped at the page limit.",
        _ => "Browser automation failed."
    };

    static string Compact(string value, int maxChars)
    {
        value = string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length <= maxChars ? value : value[..maxChars].TrimEnd() + "...";
    }

    static string Limit(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars].TrimEnd() + "...";
}
```

- [ ] **Step 5: Run QChat policy/formatter tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatBrowserAgentTriggerPolicyTests|QChatBrowserAgentFormatterTests"
```

Expected:

```text
Passed! - Failed: 0
```

---

### Task 5: QChat Service Wiring

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatConfig.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Test: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Add failing QChat service tests**

Add tests near existing web research/browser tests in `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`:

```csharp
[Test]
public async Task OwnerPrivateBrowserAgentRequestRunsAutomationWithoutModelDispatch()
{
    FakeOneBotRuntime runtime = new();
    FakeBrowserProvider browser = new(new AgentBrowserSnapshot(
        true,
        "ok",
        "https://example.com/docs",
        "Docs",
        "Install steps are listed here.",
        []));
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        EnableBrowserAgentAutomation = true,
        EnableBalancedTextStreaming = false
    }, browserProvider: browser);
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        Interlocked.Increment(ref dispatchCount);
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 1001,
        RawMessage = "browse https://example.com/docs install steps"
    });

    await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);

    Assert.Multiple(() =>
    {
        Assert.That(browser.Calls, Is.EqualTo(1));
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("Conclusion:"));
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("https://example.com/docs"));
    });
}

[Test]
public async Task NonOwnerPrivateBrowserAgentRequestDoesNotRunAutomationOrModel()
{
    FakeOneBotRuntime runtime = new();
    FakeBrowserProvider browser = new(new AgentBrowserSnapshot(true, "ok", "https://example.com", "x", "x", []));
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        AllowPrivateGuestChat = true,
        EnableBrowserAgentAutomation = true,
        EnableBalancedTextStreaming = false
    }, browserProvider: browser);
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        Interlocked.Increment(ref dispatchCount);
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 2002,
        RawMessage = "browse https://example.com/docs"
    });

    await Task.Delay(100);

    Assert.Multiple(() =>
    {
        Assert.That(browser.Calls, Is.Zero);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.PrivateMessages, Is.Empty);
    });
}

[Test]
public async Task GroupBrowserAgentRequestDoesNotRunAutomation()
{
    FakeOneBotRuntime runtime = new();
    FakeBrowserProvider browser = new(new AgentBrowserSnapshot(true, "ok", "https://example.com", "x", "x", []));
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        AllowGroupMemberMentions = true,
        EnableBrowserAgentAutomation = true,
        EnableBalancedTextStreaming = false
    }, browserProvider: browser);

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 1001,
        GroupId = 3003,
        RawMessage = "[CQ:at,qq=999] browse https://example.com/docs"
    });

    await Task.Delay(100);

    Assert.Multiple(() =>
    {
        Assert.That(browser.Calls, Is.Zero);
        Assert.That(runtime.GroupMessages, Is.Empty);
    });
}
```

If the file already has `FakeBrowserProvider`, reuse it instead of duplicating the fake.

- [ ] **Step 2: Run failing QChat service tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "OwnerPrivateBrowserAgentRequestRunsAutomationWithoutModelDispatch|NonOwnerPrivateBrowserAgentRequestDoesNotRunAutomationOrModel|GroupBrowserAgentRequestDoesNotRunAutomation"
```

Expected:

```text
Compilation fails because QChatConfig.EnableBrowserAgentAutomation does not exist or QChatService is not wired.
```

- [ ] **Step 3: Add QChat config properties**

Modify `sources/Alife.Function/Alife.Function.QChat/QChatConfig.cs` and add properties near existing internet/browser settings:

```csharp
public bool EnableBrowserAgentAutomation { get; set; }
public int BrowserAgentMaxSteps { get; set; } = 5;
public int BrowserAgentMaxPages { get; set; } = 3;
public int BrowserAgentMaxLinksPerPage { get; set; } = 20;
public int BrowserAgentMaxTextCharsPerPage { get; set; } = 4000;
public int BrowserAgentMaxEvidenceItems { get; set; } = 3;
```

Default remains disabled unless explicitly enabled. Browser automation remains owner-only even when enabled.

- [ ] **Step 4: Wire QChatService before model dispatch**

Modify `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`.

Add helper:

```csharp
AgentBrowserAutomationConfig CreateBrowserAutomationConfig(QChatConfig config) => new()
{
    Enabled = config.EnableBrowserAgentAutomation,
    MaxSteps = config.BrowserAgentMaxSteps,
    MaxPages = config.BrowserAgentMaxPages,
    MaxLinksPerPage = config.BrowserAgentMaxLinksPerPage,
    MaxTextCharsPerPage = config.BrowserAgentMaxTextCharsPerPage,
    MaxEvidenceItems = config.BrowserAgentMaxEvidenceItems
};
```

Add handler method near other deterministic pre-model handlers:

```csharp
async Task<bool> TryHandleBrowserAgentAutomationAsync(
    OneBotMessageEvent messageEvent,
    QChatSenderRole senderRole,
    string targetType,
    long targetId)
{
    QChatConfig config = Configuration!;
    QChatBrowserAgentTrigger trigger = QChatBrowserAgentTriggerPolicy.Parse(
        messageEvent.MessageType,
        senderRole,
        messageEvent.RawMessage);
    if (trigger.Kind == QChatBrowserAgentTriggerKind.None)
        return false;
    if (trigger.Kind == QChatBrowserAgentTriggerKind.Denied)
    {
        WriteQChatDiagnostic("qchat-browser-agent-denied", "QChat browser automation request was denied.", new {
            messageEvent.MessageType,
            messageEvent.UserId,
            messageEvent.GroupId,
            senderRole,
            trigger.Reason
        });
        return true;
    }

    AgentBrowserAutomationService service = new(
        browserProvider: injectedBrowserProvider,
        searchProvider: injectedPublicSearchProvider,
        siteExperienceStore: browserSiteExperienceStore);
    AgentBrowserAutomationResult result = await service.ExecuteAsync(new AgentBrowserAutomationRequest(
        trigger.Task,
        AgentWebAccessActorRole.Owner,
        CreateBrowserAutomationConfig(config),
        ActorUserId: messageEvent.UserId,
        GroupId: messageEvent.GroupId));

    WriteQChatDiagnostic("qchat-browser-agent-result", "QChat browser automation request completed.", new {
        messageEvent.MessageType,
        messageEvent.UserId,
        messageEvent.GroupId,
        result.Success,
        result.Reason,
        StepCount = result.Steps.Count,
        result.OpenedPageCount,
        EvidenceCount = result.Evidence.Count
    });

    await SendCommandReplyAsync(messageEvent, senderRole, targetType, targetId, QChatBrowserAgentFormatter.Format(result));
    return true;
}
```

Place the call before normal model dispatch and after hard owner/role identification is known:

```csharp
if (await TryHandleBrowserAgentAutomationAsync(messageEvent, senderRole, targetType, targetId))
    return;
```

If `QChatService` does not expose `injectedBrowserProvider` as a field yet, add:

```csharp
readonly IAgentBrowserProvider? injectedBrowserProvider = browserProvider;
```

Use existing `SendCommandReplyAsync`/diagnostic patterns from nearby deterministic command handlers. Do not route non-owner denied requests to model.

- [ ] **Step 5: Run QChat service tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "OwnerPrivateBrowserAgentRequestRunsAutomationWithoutModelDispatch|NonOwnerPrivateBrowserAgentRequestDoesNotRunAutomationOrModel|GroupBrowserAgentRequestDoesNotRunAutomation"
```

Expected:

```text
Passed! - Failed: 0
```

---

### Task 6: Diagnostics And Documentation

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`
- Modify: `docs/agent-browser-web-research.md`
- Modify: `docs/browser-global-task-plan.md`

- [ ] **Step 1: Add failing diagnostics test**

Add to `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`:

```csharp
[Test]
public void TryHandle_WebBrowserAgent_ReturnsOwnerOnlyPhaseOneSummary()
{
    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/qchat web browser-agent",
        CreateRoute(),
        CreateProfile());

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("browser-agent=phase1"));
        Assert.That(result.Text, Does.Contain("owner-only"));
        Assert.That(result.Text, Does.Contain("no-login"));
        Assert.That(result.Text, Does.Contain("image-ok"));
        Assert.That(result.Text, Does.Contain("video-link-only"));
    });
}
```

Use existing helper names in `QChatDiagnosticsServiceTests`; if they differ, adapt to the local test helper pattern.

- [ ] **Step 2: Implement diagnostics text**

Modify `QChatDiagnosticsService.TryHandle(...)` command switch:

```csharp
"web browser-agent" => Handled(BuildBrowserAgentText()),
```

Add:

```csharp
static string BuildBrowserAgentText()
{
    return string.Join(Environment.NewLine,
        "browser-agent=phase1",
        "scope=owner-only private-chat",
        "allowed=search,navigate,snapshot,scroll,public-link,image-return,video-link,back,stop",
        "blocked=no-login no-form-submit no-video-download no-local-upload no-js no-private-network",
        "limits=steps:5 pages:3",
        "smoke.owner.private=browse https://github.com/vercel-labs/agent-browser");
}
```

- [ ] **Step 3: Update docs**

Append to `docs/agent-browser-web-research.md`:

```markdown
## Browser Agent Automation Phase 1

Status: planned / implementation in progress until focused tests and owner live smoke pass.

Phase 1 is owner-only and private-chat only. It allows bounded public browsing:

- public web search;
- public HTTP/HTTPS navigation;
- read-only browser snapshots;
- scroll/page observation;
- safe public link navigation;
- public image return as QQ image;
- public video return as link only;
- compact sourced QQ output.

It does not allow login, form submission, arbitrary downloads, local-file uploads, video downloads, arbitrary JavaScript, private network URLs, payment, posting, or browser automation for group members.
```

Append to `docs/browser-global-task-plan.md`:

```markdown
## Priority 8: Browser Agent Automation Phase 1

Status: planned.

Goal: add owner-only bounded browser automation over public pages.

Completion evidence required:

- action policy tests pass;
- automation service tests pass;
- QChat owner/private trigger tests pass;
- non-owner and group denial tests pass;
- diagnostics text exists;
- image output and video-link-only tests pass;
- focused build/test verification passes;
- GitHub upload through `D:\FOXD` uses full `D:\Alife` source root.
```

- [ ] **Step 4: Run diagnostics tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter QChatDiagnosticsServiceTests
```

Expected:

```text
Passed! - Failed: 0
```

---

### Task 7: Final Verification And Upload

**Files:**
- Verify all files created/modified above.
- Upload through `D:\FOXD` after tests pass.

- [ ] **Step 1: Run focused framework tests**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter "AgentBrowserActionPolicyTests|AgentBrowserTaskPlannerTests|AgentBrowserAutomationServiceTests|AgentBrowserMediaOutputServiceTests|AgentWebAccessRouterTests|AgentBrowserProviderModelsTests"
```

Expected:

```text
Passed! - Failed: 0
```

- [ ] **Step 2: Run focused QChat tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatBrowserAgentTriggerPolicyTests|QChatBrowserAgentFormatterTests|OwnerPrivateBrowserAgentRequestRunsAutomationWithoutModelDispatch|NonOwnerPrivateBrowserAgentRequestDoesNotRunAutomationOrModel|GroupBrowserAgentRequestDoesNotRunAutomation|QChatDiagnosticsServiceTests"
```

Expected:

```text
Passed! - Failed: 0
```

- [ ] **Step 3: Run build**

Run:

```powershell
dotnet build --no-restore
```

Expected:

```text
Build succeeded.
0 Error(s)
```

Existing warnings are acceptable only if they predate this task and are documented in the final report.

- [ ] **Step 4: Stage all new tracked files**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserAutomationModels.cs `
        sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserActionPolicy.cs `
        sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserTaskPlanner.cs `
        sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserAutomationService.cs `
        sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserMediaOutputService.cs `
        sources/Alife.Function/Alife.Function.QChat/QChatBrowserAgentTriggerPolicy.cs `
        sources/Alife.Function/Alife.Function.QChat/QChatBrowserAgentFormatter.cs `
        sources/Alife.Function/Alife.Function.QChat/QChatConfig.cs `
        sources/Alife.Function/Alife.Function.QChat/QChatService.cs `
        sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs `
        Tests/Alife.Test.Framework/AgentBrowserActionPolicyTests.cs `
        Tests/Alife.Test.Framework/AgentBrowserTaskPlannerTests.cs `
        Tests/Alife.Test.Framework/AgentBrowserAutomationServiceTests.cs `
        Tests/Alife.Test.Framework/AgentBrowserMediaOutputServiceTests.cs `
        Tests/Alife.Test.QChat/QChatBrowserAgentTriggerPolicyTests.cs `
        Tests/Alife.Test.QChat/QChatBrowserAgentFormatterTests.cs `
        Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs `
        Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs `
        docs/agent-browser-web-research.md `
        docs/browser-global-task-plan.md
```

Force-add ignored superpowers docs if needed:

```powershell
git add -f docs/superpowers/specs/2026-06-23-browser-agent-automation-phase1-design.md `
           docs/superpowers/plans/2026-06-23-browser-agent-automation-phase1.md
```

- [ ] **Step 5: Upload using full source root**

Run from `D:\Alife`:

```powershell
powershell -ExecutionPolicy Bypass -File D:\Alife\tools\upload-alife-service-via-foxd.ps1
```

Expected:

```text
Upload complete.
Verified remote: <commit> refs/heads/master
```

Do not use a partial worktree as `-SourceRoot` unless `git ls-files` count and key browser/web files are verified against `D:\Alife`.

- [ ] **Step 6: Report final state**

Final report must include:

- created/modified files;
- test commands and pass counts;
- build result;
- GitHub commit hash;
- explicit remaining gaps:
  - no login;
  - no form submission;
  - image download only for validated public images;
  - video link only, no video download/upload;
  - no arbitrary local-file upload;
  - no arbitrary JavaScript;
  - no group-member browser automation;
  - no full browser Agent beyond Phase 1.

---

## Self-Review

- Spec coverage: every Phase 1 requirement maps to a task: action policy, deterministic planner, bounded executor, image output/video link policy, QChat owner-only trigger, formatter, diagnostics, docs, verification, and upload.
- Placeholder scan: no TBD/TODO/fill-in placeholders remain.
- Type consistency: the plan consistently uses `AgentBrowserAutomationRequest`, `AgentBrowserAutomationAction`, `AgentBrowserAutomationResult`, `AgentBrowserActionPolicy`, `AgentBrowserTaskPlanner`, `AgentBrowserAutomationService`, `QChatBrowserAgentTriggerPolicy`, and `QChatBrowserAgentFormatter`.
- Scope check: this remains Phase 1 owner-only bounded browsing. It does not add login, form submission, arbitrary downloads, video downloads/uploads, local-file uploads, arbitrary JS, or group browser automation.
