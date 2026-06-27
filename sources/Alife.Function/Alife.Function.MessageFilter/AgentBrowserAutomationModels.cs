using System.Collections.Generic;

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

public sealed record AgentBrowserSnapshotLink(
    string Text,
    string Href);

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

public enum AgentBrowserMediaOutputKind
{
    Image,
    VideoLink
}

public sealed record AgentBrowserMediaOutputRequest(
    AgentBrowserMediaOutputKind Kind,
    string Url,
    AgentBrowserAutomationConfig Config);

public sealed record AgentBrowserMediaFetchResult(
    bool Success,
    string Reason,
    string ContentType,
    byte[] Body);

public sealed record AgentBrowserMediaOutputResult(
    bool Success,
    string Reason,
    AgentBrowserMediaOutputKind Kind,
    string Url,
    string ReturnText,
    string? LocalPath);
