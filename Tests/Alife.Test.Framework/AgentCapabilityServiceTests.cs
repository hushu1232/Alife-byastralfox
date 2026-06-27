using Alife.Framework;
using Alife.Function.Agent;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Function.MessageFilter;
using Alife.Function.QChat;
using Alife.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace Alife.Test.Framework;

public class AgentCapabilityServiceTests
{
    [Test]
    public void DiagnosticsSnapshotIncludesRuntimeHealthAndCapabilities()
    {
        AgentDiagnosticsService service = new()
        {
            HealthReporterSourceOverride =
            [
                new StubHealthReporter("Memory", ModuleHealthStatus.Healthy, "Memory is ready."),
                new StubHealthReporter("Browser", ModuleHealthStatus.Degraded, "Browser is loading.")
            ],
            CapabilitySourceOverride =
            [
                new StubCapability("Memory", EmbodiedCapabilityKind.Memory, "Persistent memory.", "ready"),
                new StubCapability("Browser", EmbodiedCapabilityKind.Sense, "Real browser.", "loading")
            ]
        };
        ChatRuntimeState runtime = new(
            IsChatting: true,
            PendingPokeCount: 2,
            ChatHistoryCount: 9,
            LastError: "last failure",
            RecentEvents: [new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-14T00:00:00Z"), "Error", "last failure")]);

        AgentStateSnapshot snapshot = service.BuildSnapshot(runtime, "Kira");
        string report = AgentDiagnosticsService.FormatSnapshot(snapshot);

        Assert.That(snapshot.CharacterName, Is.EqualTo("Kira"));
        Assert.That(snapshot.IsChatting, Is.True);
        Assert.That(snapshot.PendingPokeCount, Is.EqualTo(2));
        Assert.That(snapshot.ModuleHealth.Select(health => health.Name), Does.Contain("Memory"));
        Assert.That(snapshot.Capabilities.Select(capability => capability.Name), Does.Contain("Browser"));
        Assert.That(report, Does.Contain("Agent state: Kira"));
        Assert.That(report, Does.Contain("Last error: last failure"));
        Assert.That(report, Does.Contain("[Healthy] Memory: Memory is ready."));
        Assert.That(report, Does.Contain("[Sense] Browser: Real browser. State: loading"));
    }

    [Test]
    public void SelfModelCombinesIdentityCapabilitiesTasksRestrictionsAndRecentExperiences()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentTaskService tasks = new(audit, taskStorePath: Path.Combine(root, "tasks.json"));
        tasks.CreateTask("owner", "Make the bot more human-like", ["inspect", "implement"]);
        LifeEventStreamService lifeEvents = new(storagePath: Path.Combine(root, "life-events"));
        lifeEvents.Publish(new LifeEvent(
            DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
            LifeEventKind.Communication,
            "QChat",
            "Owner asked the bot to continue improving itself."));
        AgentControlCenterService controlCenter = new(auditLog: audit)
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowMentionWakeup = true,
                AllowPassiveGroupListening = true,
                AllowProactiveChat = false,
                RequireOwnerConfirmationForHighRiskConfiguration = true,
            }
        };
        AgentSelfModelService service = new(
            new AgentDiagnosticsService
            {
                HealthReporterSourceOverride = [new StubHealthReporter("QChat", ModuleHealthStatus.Degraded, "OneBot disconnected.")],
                CapabilitySourceOverride = [new StubCapability("QChat", EmbodiedCapabilityKind.Communication, "QQ channel.", "offline")]
            },
            tasks,
            controlCenter,
            lifeEvents);

        AgentSelfModelSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 2, null, []),
            "AstralFox");
        string prompt = AgentSelfModelService.FormatForPrompt(snapshot);

        Assert.That(snapshot.CharacterName, Is.EqualTo("AstralFox"));
        Assert.That(snapshot.Capabilities.Select(capability => capability.Name), Does.Contain("QChat"));
        Assert.That(snapshot.ModuleHealth.Select(health => health.Name), Does.Contain("QChat"));
        Assert.That(snapshot.LatestTask?.Goal, Is.EqualTo("Make the bot more human-like"));
        Assert.That(snapshot.SafetyBoundaries, Does.Contain("High-risk actions require owner confirmation."));
        Assert.That(snapshot.RecentExperiences.Select(item => item.Summary), Does.Contain("Owner asked the bot to continue improving itself."));
        Assert.That(prompt, Does.Contain("[Self model]"));
        Assert.That(prompt, Does.Contain("AstralFox"));
        Assert.That(prompt, Does.Contain("High-risk actions require owner confirmation."));
        Assert.That(prompt, Does.Contain("Owner asked the bot to continue improving itself."));
    }

    [Test]
    public void CapabilityInventoryDescribesVerifiedToolBoundaries()
    {
        AgentCapabilityInventoryService service = new();

        IReadOnlyList<AgentCapabilityBoundary> inventory = service.BuildInventory();
        string prompt = AgentCapabilityInventoryService.FormatForPrompt(inventory);

        Assert.That(inventory.Select(item => item.ToolName), Does.Contain("agent_run"));
        Assert.That(inventory.Select(item => item.ToolName), Does.Contain("workspace_write"));
        Assert.That(inventory.Select(item => item.ToolName), Does.Contain("qchat_joined_groups_refresh"));
        Assert.That(inventory.Select(item => item.ToolName), Does.Contain("qchat_allowlist_update"));
        Assert.That(inventory.Select(item => item.ToolName), Does.Contain("qzone_proactive_execute"));

        AgentCapabilityBoundary agentRun = inventory.Single(item => item.ToolName == "agent_run");
        Assert.That(agentRun.RiskLevel, Is.EqualTo(XmlFunctionRiskLevel.High));
        Assert.That(agentRun.DefaultAllowed, Is.False);
        Assert.That(agentRun.Requires, Does.Contain("predefined"));

        AgentCapabilityBoundary qzone = inventory.Single(item => item.ToolName == "qzone_proactive_execute");
        Assert.That(qzone.TruthfulnessRule, Does.Contain("dry-run"));
        Assert.That(prompt, Does.Contain("[Tool capability boundaries]"));
        Assert.That(prompt, Does.Contain("Do not claim high-risk actions were executed unless the tool result says executed"));
        Assert.That(prompt, Does.Contain("Do not rely on memory for live QQ group lists"));
    }

    [Test]
    public void SelfModelIncludesCapabilityBoundariesForToolTruthfulness()
    {
        AgentCapabilityInventoryService inventory = new();
        AgentSelfModelService service = new(
            new AgentDiagnosticsService(),
            capabilityInventory: inventory);

        AgentSelfModelSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "AstralFox");
        string prompt = AgentSelfModelService.FormatForPrompt(snapshot);

        Assert.That(snapshot.CapabilityBoundaries.Select(item => item.ToolName), Does.Contain("workspace_write"));
        Assert.That(snapshot.CapabilityBoundaries.Select(item => item.ToolName), Does.Contain("agent_run"));
        Assert.That(prompt, Does.Contain("[Tool capability boundaries]"));
        Assert.That(prompt, Does.Contain("workspace_write"));
        Assert.That(prompt, Does.Contain("owner confirmation"));
        Assert.That(prompt, Does.Contain("Do not rely on memory for live QQ group lists"));
    }

    [Test]
    public void CapabilityInventoryExposesXmlTool()
    {
        string[] xmlFunctionNames = typeof(AgentCapabilityInventoryService)
            .GetMethods()
            .Select(method => method.GetCustomAttributes(typeof(XmlFunctionAttribute), inherit: false)
                .OfType<XmlFunctionAttribute>()
                .FirstOrDefault())
            .OfType<XmlFunctionAttribute>()
            .Select(attribute => attribute.Name ?? string.Empty)
            .ToArray();

        Assert.That(xmlFunctionNames, Does.Contain("agent_capability_inventory"));
    }

    [Test]
    public void SelfModelExposesXmlTool()
    {
        string[] xmlFunctionNames = typeof(AgentSelfModelService)
            .GetMethods()
            .Select(method => method.GetCustomAttributes(typeof(XmlFunctionAttribute), inherit: false)
                .OfType<XmlFunctionAttribute>()
                .FirstOrDefault())
            .OfType<XmlFunctionAttribute>()
            .Select(attribute => attribute.Name ?? string.Empty)
            .ToArray();

        Assert.That(xmlFunctionNames, Does.Contain("agent_self_model"));
    }

    [Test]
    public void WorkspaceReadAndSearchStayInsideAllowedRoot()
    {
        string root = CreateTempWorkspace();
        string file = Path.Combine(root, "notes", "status.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "alpha\nimportant status\nomega");
        AgentWorkspaceService workspace = new(new AgentWorkspacePolicy([root]));

        AgentWorkspaceReadResult read = workspace.ReadText("notes/status.txt", maxChars: 100);
        IReadOnlyList<AgentWorkspaceSearchMatch> matches = workspace.SearchText("important", ".", maxMatches: 10);

        Assert.That(read.RelativePath, Is.EqualTo("notes/status.txt"));
        Assert.That(read.Content, Does.Contain("important status"));
        Assert.That(matches, Has.Count.EqualTo(1));
        Assert.That(matches[0].LineNumber, Is.EqualTo(2));
        Assert.Throws<UnauthorizedAccessException>(() => workspace.ReadText("../outside.txt"));
    }

    [Test]
    public void WorkspaceListShowsDirectChildrenInsideAllowedRoot()
    {
        string root = CreateTempWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.WriteAllText(Path.Combine(root, "README.md"), "hello");
        File.WriteAllText(Path.Combine(root, "src", "Program.cs"), "class Program {}");
        AgentWorkspaceService workspace = new(new AgentWorkspacePolicy([root]));

        IReadOnlyList<AgentWorkspaceEntry> entries = workspace.ListEntries(".", maxEntries: 10);

        Assert.That(entries.Select(entry => entry.RelativePath), Is.EqualTo(new[] { "docs", "src", "README.md" }));
        Assert.That(entries.Single(entry => entry.RelativePath == "src").IsDirectory, Is.True);
        Assert.That(entries.Single(entry => entry.RelativePath == "README.md").IsDirectory, Is.False);
        Assert.That(entries.Single(entry => entry.RelativePath == "README.md").SizeBytes, Is.GreaterThan(0));
        Assert.Throws<UnauthorizedAccessException>(() => workspace.ListEntries("../outside", maxEntries: 10));
    }

    [Test]
    public void WorkspaceReadLinesReturnsLineNumberedRangeInsideAllowedRoot()
    {
        string root = CreateTempWorkspace();
        string file = Path.Combine(root, "src", "Program.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "line one\nline two\nline three\nline four");
        AgentWorkspaceService workspace = new(new AgentWorkspacePolicy([root]));

        AgentWorkspaceLineReadResult result = workspace.ReadLines("src/Program.cs", startLine: 2, lineCount: 2);

        Assert.That(result.RelativePath, Is.EqualTo("src/Program.cs"));
        Assert.That(result.TotalLines, Is.EqualTo(4));
        Assert.That(result.StartLine, Is.EqualTo(2));
        Assert.That(result.EndLine, Is.EqualTo(3));
        Assert.That(result.Truncated, Is.True);
        Assert.That(result.Lines.Select(line => $"{line.LineNumber}:{line.Text}"),
            Is.EqualTo(new[] { "2:line two", "3:line three" }));
        Assert.Throws<UnauthorizedAccessException>(() => workspace.ReadLines("../outside.cs", startLine: 1, lineCount: 2));
    }

    [Test]
    public void WorkspaceWriteAndReplaceRequireAllowedRootAndExactMatch()
    {
        string root = CreateTempWorkspace();
        AgentWorkspaceService workspace = new(new AgentWorkspacePolicy([root]));

        AgentWorkspaceWriteResult write = workspace.WriteText("src/AgentNote.cs", "class AgentNote {}", overwrite: false);
        AgentWorkspaceReplaceResult replace = workspace.ReplaceText("src/AgentNote.cs", "AgentNote", "GeneratedAgentNote");

        Assert.That(write.Created, Is.True);
        Assert.That(replace.ReplacedCount, Is.EqualTo(1));
        Assert.That(File.ReadAllText(Path.Combine(root, "src", "AgentNote.cs")), Does.Contain("GeneratedAgentNote"));
        Assert.Throws<InvalidOperationException>(() => workspace.WriteText("src/AgentNote.cs", "overwrite", overwrite: false));
        Assert.Throws<UnauthorizedAccessException>(() => workspace.WriteText("../escape.cs", "bad", overwrite: true));
    }

    [Test]
    public void WorkspaceMutationsRecordHighRiskAuditEntries()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentWorkspaceService workspace = new(new AgentWorkspacePolicy([root]), auditLog: audit);

        workspace.WriteText("src/AgentNote.cs", "class AgentNote {}", overwrite: false);
        workspace.ReplaceText("src/AgentNote.cs", "AgentNote", "GeneratedAgentNote");
        Assert.Throws<InvalidOperationException>(() =>
            workspace.WriteText("src/AgentNote.cs", "overwrite", overwrite: false));

        IReadOnlyList<AgentAuditLogEntry> entries = audit.GetRecentEntries(10);

        Assert.That(entries.Select(entry => entry.Action), Does.Contain("workspace.write"));
        Assert.That(entries.Select(entry => entry.Action), Does.Contain("workspace.replace"));
        Assert.That(entries.Count(entry => entry.Action == "workspace.write"), Is.EqualTo(2));
        Assert.That(entries.Where(entry => entry.Action.StartsWith("workspace.", StringComparison.Ordinal))
            .All(entry => entry.RiskLevel == AgentAuditRiskLevel.High), Is.True);
        Assert.That(entries.Any(entry => entry.Action == "workspace.write" && entry.Succeeded), Is.True);
        Assert.That(entries.Any(entry => entry.Action == "workspace.write" && entry.Succeeded == false), Is.True);
        Assert.That(entries.Select(entry => entry.Detail), Has.Some.Contains("src/AgentNote.cs"));
    }

    [Test]
    public void WorkspaceReplaceProposalPreviewsBeforeApplyingMutation()
    {
        string root = CreateTempWorkspace();
        string file = Path.Combine(root, "src", "AgentNote.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "namespace Demo;\nclass AgentNote {}\n");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentWorkspaceService workspace = new(new AgentWorkspacePolicy([root]), auditLog: audit);

        AgentWorkspacePatchProposal proposal = workspace.ProposeReplace(
            "src/AgentNote.cs",
            "class AgentNote {}",
            "class GeneratedAgentNote {}");

        Assert.That(proposal.RelativePath, Is.EqualTo("src/AgentNote.cs"));
        Assert.That(proposal.Preview, Does.Contain("- class AgentNote {}"));
        Assert.That(proposal.Preview, Does.Contain("+ class GeneratedAgentNote {}"));
        Assert.That(File.ReadAllText(file), Does.Contain("class AgentNote {}"));

        AgentWorkspaceReplaceResult result = workspace.ApplyProposedReplace(proposal.Id);

        Assert.That(result.ReplacedCount, Is.EqualTo(1));
        Assert.That(File.ReadAllText(file), Does.Contain("class GeneratedAgentNote {}"));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("workspace.replace"));
    }

    [Test]
    public void WorkspaceApplyProposalUsesSecurityGatewayForExternalRequests()
    {
        string root = CreateTempWorkspace();
        string file = Path.Combine(root, "src", "AgentNote.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "class AgentNote {}\n");
        AgentWorkspaceService workspace = new(new AgentWorkspacePolicy([root]));
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };
        AgentWorkspacePatchProposal blockedProposal = workspace.ProposeReplace(
            "src/AgentNote.cs",
            "AgentNote",
            "BlockedAgentNote");
        AgentWorkspacePatchProposal ownerProposal = workspace.ProposeReplace(
            "src/AgentNote.cs",
            "AgentNote",
            "GeneratedAgentNote");

        AgentWorkspaceApplyProposalResult blocked = workspace.ApplyProposedReplace(
            blockedProposal.Id,
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "workspace.apply"),
            config);
        AgentWorkspaceApplyProposalResult ownerApplied = workspace.ApplyProposedReplace(
            ownerProposal.Id,
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: false,
                Action: "workspace.apply"),
            config);

        Assert.That(blocked.Applied, Is.False);
        Assert.That(blocked.GatewayDecision.Status, Is.EqualTo(AgentExecutionDecisionStatus.OwnerConfirmationRequired));
        Assert.That(blocked.GatewayDecision.RiskLevel, Is.EqualTo(AgentRiskLevel.High));
        Assert.That(ownerApplied.Applied, Is.True);
        Assert.That(ownerApplied.GatewayDecision.Status, Is.EqualTo(AgentExecutionDecisionStatus.AllowedAutomatically));
        Assert.That(ownerApplied.Result?.ReplacedCount, Is.EqualTo(1));
        Assert.That(File.ReadAllText(file), Does.Contain("GeneratedAgentNote"));
        Assert.That(File.ReadAllText(file), Does.Not.Contain("BlockedAgentNote"));
        Assert.That(workspace.GetPendingProposals().Select(item => item.Id), Does.Contain(blockedProposal.Id));
        Assert.That(workspace.GetPendingProposals().Select(item => item.Id), Does.Not.Contain(ownerProposal.Id));
    }

    [Test]
    public void WorkspaceServiceListsPendingReplaceProposals()
    {
        string root = CreateTempWorkspace();
        string file = Path.Combine(root, "src", "AgentNote.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "class AgentNote {}\n");
        AgentWorkspaceService workspace = new(new AgentWorkspacePolicy([root]));

        AgentWorkspacePatchProposal proposal = workspace.ProposeReplace(
            "src/AgentNote.cs",
            "AgentNote",
            "GeneratedAgentNote");

        IReadOnlyList<AgentWorkspacePatchProposal> proposals = workspace.GetPendingProposals();

        Assert.That(proposals.Select(item => item.Id), Does.Contain(proposal.Id));
        Assert.That(proposals[0].RelativePath, Is.EqualTo("src/AgentNote.cs"));
        Assert.That(proposals[0].Preview, Does.Contain("- AgentNote"));
        Assert.That(File.ReadAllText(file), Does.Contain("class AgentNote {}"));
    }

    [Test]
    public void GitHubUploadPlanUsesSecurityGatewayAndDoesNotExecuteUpload()
    {
        AgentGitHubUploadService service = new();
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };

        AgentGitHubUploadPlanResult blocked = service.BuildUploadPlan(
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "github.upload"),
            config);
        AgentGitHubUploadPlanResult needsConfirmation = service.BuildUploadPlan(
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: false,
                Action: "github.upload"),
            config);
        AgentGitHubUploadPlanResult allowed = service.BuildUploadPlan(
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "github.upload"),
            config);

        Assert.That(blocked.ReadyToRun, Is.False);
        Assert.That(blocked.GatewayDecision.Status, Is.EqualTo(AgentExecutionDecisionStatus.OwnerConfirmationRequired));
        Assert.That(blocked.GatewayDecision.RiskLevel, Is.EqualTo(AgentRiskLevel.High));
        Assert.That(needsConfirmation.ReadyToRun, Is.True);
        Assert.That(needsConfirmation.GatewayDecision.Status, Is.EqualTo(AgentExecutionDecisionStatus.AllowedAutomatically));
        Assert.That(allowed.ReadyToRun, Is.True);
        Assert.That(allowed.Command, Does.Contain("upload-alife-service-via-foxd.ps1"));
    }

    [Test]
    public void WorkspaceDefaultPolicyIncludesCurrentDirectoryForProjectCodeWork()
    {
        AgentWorkspaceService workspace = new();

        Assert.That(workspace.AllowedRoots, Does.Contain(Path.GetFullPath(Environment.CurrentDirectory)));
    }

    [Test]
    public void WorkspaceDefaultPolicyIncludesProjectRootWhenClientRunsFromOutputDirectory()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        string root = CreateTempWorkspace();
        string outputDirectory = Path.Combine(root, "Outputs", "Alife.Client");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(root, "Alife.slnx"), "<Solution />");

        try
        {
            Environment.CurrentDirectory = outputDirectory;

            AgentWorkspaceService workspace = new();

            Assert.That(workspace.AllowedRoots, Does.Contain(Path.GetFullPath(root)));
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Test]
    public void AuditLogRecordsRecentEntriesAndPersistsJsonLines()
    {
        string root = CreateTempWorkspace();
        string auditFile = Path.Combine(root, "audit.jsonl");
        AgentAuditLogService audit = new(auditFile, maxRetainedEntries: 2);

        audit.Record("workspace.write", "owner", "created a file", AgentAuditRiskLevel.High, true);
        audit.Record("agent.command", "owner", "ran test command", AgentAuditRiskLevel.High, false, "exit 1");
        audit.Record("agent.state", "system", "read status", AgentAuditRiskLevel.Low, true);

        IReadOnlyList<AgentAuditLogEntry> recent = audit.GetRecentEntries(10);
        string persisted = File.ReadAllText(auditFile);

        Assert.That(recent, Has.Count.EqualTo(2));
        Assert.That(recent[0].Action, Is.EqualTo("agent.command"));
        Assert.That(recent[1].Action, Is.EqualTo("agent.state"));
        Assert.That(persisted, Does.Contain("workspace.write"));
        Assert.That(persisted, Does.Contain("exit 1"));
    }

    [Test]
    public void AuditLogReloadsRecentEntriesFromExistingJsonLines()
    {
        string root = CreateTempWorkspace();
        string auditFile = Path.Combine(root, "audit.jsonl");
        AgentAuditLogService audit = new(auditFile, maxRetainedEntries: 2);
        audit.Record("agent.first", "owner", "first", AgentAuditRiskLevel.Low, true);
        audit.Record("agent.second", "owner", "second", AgentAuditRiskLevel.Medium, true);
        audit.Record("agent.third", "owner", "third", AgentAuditRiskLevel.High, false, "failed");

        AgentAuditLogService reloaded = new(auditFile, maxRetainedEntries: 2);
        IReadOnlyList<AgentAuditLogEntry> recent = reloaded.GetRecentEntries(10);

        Assert.That(recent.Select(entry => entry.Action), Is.EqualTo(new[] { "agent.second", "agent.third" }));
        Assert.That(recent[1].Error, Is.EqualTo("failed"));
    }

    [Test]
    public async Task CommandServiceRunsOnlyWhitelistedCommandsAndAuditsResult()
    {
        AgentAuditLogService audit = new(Path.Combine(CreateTempWorkspace(), "audit.jsonl"));
        FakeCommandRunner runner = new();
        AgentCommandService service = new(
            new AgentCommandPolicy([
                new AgentCommandDefinition("check", "Check project", "dotnet", "test", CreateTempWorkspace(), TimeSpan.FromSeconds(5))
            ]),
            runner,
            audit);

        AgentCommandResult result = await service.RunAllowedCommandAsync("check", "owner", CancellationToken.None);

        Assert.That(result.CommandId, Is.EqualTo("check"));
        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Is.EqualTo("ok"));
        Assert.That(runner.LastRequest?.FileName, Is.EqualTo("dotnet"));
        Assert.That(runner.LastRequest?.Arguments, Is.EqualTo("test"));
        Assert.That(audit.GetRecentEntries(1)[0].Action, Is.EqualTo("agent.command.check"));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RunAllowedCommandAsync("unknown", "owner", CancellationToken.None));
    }

    [Test]
    public void CommandServiceDefaultPolicyIncludesBuildAndTestVerificationCommands()
    {
        AgentCommandService service = new();

        string[] commandIds = service.AllowedCommands.Select(command => command.Id).ToArray();

        Assert.That(commandIds, Does.Contain("git-status"));
        Assert.That(commandIds, Does.Contain("git-diff"));
        Assert.That(commandIds, Does.Contain("dotnet-build-solution"));
        Assert.That(commandIds, Does.Contain("dotnet-test-solution"));
        Assert.That(service.AllowedCommands.Single(command => command.Id == "dotnet-test-solution").Arguments,
            Does.Contain("--no-restore"));
    }

    [Test]
    public void CommandServiceDefaultPolicyUsesConfiguredDotnetExecutable()
    {
        string? previous = Environment.GetEnvironmentVariable("ALIFE_AGENT_DOTNET_PATH");
        string configuredDotnet = Path.Combine(CreateTempWorkspace(), "dotnet.exe");
        try
        {
            Environment.SetEnvironmentVariable("ALIFE_AGENT_DOTNET_PATH", configuredDotnet);

            AgentCommandService service = new();

            Assert.That(service.AllowedCommands.Single(command => command.Id == "dotnet-build-solution").FileName,
                Is.EqualTo(configuredDotnet));
            Assert.That(service.AllowedCommands.Single(command => command.Id == "dotnet-test-solution").FileName,
                Is.EqualTo(configuredDotnet));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ALIFE_AGENT_DOTNET_PATH", previous);
        }
    }

    [Test]
    public void PermissionPolicyAllowsOwnerHighRiskWithoutExplicitConfirmation()
    {
        AgentPermissionPolicy policy = new(new AgentPermissionConfig
        {
            OwnerUserIds = [10001],
            AllowGroupLowRisk = true,
            AllowGroupMediumRiskWhenMentioned = true,
            RequireConfirmationForHighRisk = true
        });

        AgentPermissionDecision ownerNoConfirm = policy.Evaluate(new AgentPermissionRequest(
            ActorUserId: 10001,
            Source: AgentRequestSource.PrivateChat,
            IsMentioned: false,
            RiskLevel: AgentRiskLevel.High,
            HasExplicitConfirmation: false,
            Action: "agent.run"));
        AgentPermissionDecision ownerConfirmed = policy.Evaluate(new AgentPermissionRequest(
            ActorUserId: 10001,
            Source: AgentRequestSource.PrivateChat,
            IsMentioned: false,
            RiskLevel: AgentRiskLevel.High,
            HasExplicitConfirmation: true,
            Action: "agent.run"));
        AgentPermissionDecision guestGroup = policy.Evaluate(new AgentPermissionRequest(
            ActorUserId: 20002,
            Source: AgentRequestSource.GroupChat,
            IsMentioned: false,
            RiskLevel: AgentRiskLevel.Medium,
            HasExplicitConfirmation: true,
            Action: "workspace.write"));

        Assert.That(ownerNoConfirm.Allowed, Is.True);
        Assert.That(ownerNoConfirm.Priority, Is.EqualTo(AgentActorPriority.Owner));
        Assert.That(ownerConfirmed.Allowed, Is.True);
        Assert.That(ownerConfirmed.Priority, Is.EqualTo(AgentActorPriority.Owner));
        Assert.That(guestGroup.Allowed, Is.False);
    }

    [Test]
    public void ActionAuthorizationAllowsOwnerConfirmedHighRiskXmlAndBlocksMemberSpoof()
    {
        AgentActionAuthorizationService service = new();
        XmlFunction function = new()
        {
            Name = "dangeroustool",
            Mode = FunctionMode.OneShot,
            RiskLevel = XmlFunctionRiskLevel.High,
            Invoker = (_, _) => Task.CompletedTask,
        };
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true,
        };

        XmlFunctionExecutionDecision ownerDecision = service.AuthorizeXmlFunction(
            function,
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.message"),
            config);
        XmlFunctionExecutionDecision memberDecision = service.AuthorizeXmlFunction(
            function,
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.message"),
            config);

        Assert.That(ownerDecision.IsAllowed, Is.True);
        Assert.That(memberDecision.IsAllowed, Is.False);
        Assert.That(memberDecision.Reason, Does.Contain("owner authority"));
    }

    [Test]
    public void ActionAuthorizationGatewayClassifiesAutomaticConfirmationAndBlockedDecisions()
    {
        AgentActionAuthorizationService service = new();
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            AllowGroupLowRisk = true,
            RequireConfirmationForHighRisk = true
        };

        AgentExecutionGatewayDecision lowRiskGroup = service.EvaluateExecution(
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: false,
                Action: "qq.reply"),
            config);
        AgentExecutionGatewayDecision ownerNeedsConfirmation = service.EvaluateExecution(
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.High,
                HasExplicitConfirmation: false,
                Action: "workspace.apply"),
            config);
        AgentExecutionGatewayDecision memberBlocked = service.EvaluateExecution(
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.High,
                HasExplicitConfirmation: true,
                Action: "github.upload"),
            config);
        AgentExecutionGatewayDecision ownerConfirmed = service.EvaluateExecution(
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.High,
                HasExplicitConfirmation: true,
                Action: "workspace.apply"),
            config);

        Assert.That(lowRiskGroup.Status, Is.EqualTo(AgentExecutionDecisionStatus.AllowedAutomatically));
        Assert.That(lowRiskGroup.AllowedNow, Is.True);
        Assert.That(ownerNeedsConfirmation.Status, Is.EqualTo(AgentExecutionDecisionStatus.AllowedAutomatically));
        Assert.That(ownerNeedsConfirmation.RequiresOwnerConfirmation, Is.False);
        Assert.That(memberBlocked.Status, Is.EqualTo(AgentExecutionDecisionStatus.OwnerConfirmationRequired));
        Assert.That(memberBlocked.AllowedNow, Is.False);
        Assert.That(ownerConfirmed.Status, Is.EqualTo(AgentExecutionDecisionStatus.AllowedAutomatically));
        Assert.That(ownerConfirmed.AllowedNow, Is.True);
    }

    [Test]
    public async Task ActionGatewayBlocksUnauthorizedExternalActionAndAuditsDecision()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentActionGatewayService gateway = new(auditLog: audit);
        bool externalActionCalled = false;
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };

        AgentActionGatewayResult<string> result = await gateway.ExecuteAsync(
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.High,
                HasExplicitConfirmation: true,
                Action: "qzone.like"),
            config,
            async () =>
            {
                externalActionCalled = true;
                await Task.Yield();
                return "liked";
            },
            detail: "target=1001 post=post-a");
        AgentAuditLogEntry entry = audit.GetRecentEntries(1).Single();

        Assert.That(result.Executed, Is.False);
        Assert.That(result.Decision.Status, Is.EqualTo(AgentExecutionDecisionStatus.OwnerConfirmationRequired));
        Assert.That(externalActionCalled, Is.False);
        Assert.That(entry.Action, Is.EqualTo("qzone.like"));
        Assert.That(entry.Succeeded, Is.False);
        Assert.That(entry.Detail, Does.Contain("target=1001"));
        Assert.That(entry.Error, Does.Contain("Owner confirmation required"));
    }

    [Test]
    public async Task ActionGatewayExecutesAuthorizedExternalActionAndAuditsSuccess()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentActionGatewayService gateway = new(auditLog: audit);
        int externalActionCalls = 0;
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };

        AgentActionGatewayResult<string> result = await gateway.ExecuteAsync(
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.High,
                HasExplicitConfirmation: true,
                Action: "qzone.reply"),
            config,
            () =>
            {
                externalActionCalls++;
                return Task.FromResult("replied");
            },
            detail: "target=1001 post=post-a comment=comment-a");
        AgentAuditLogEntry entry = audit.GetRecentEntries(1).Single();

        Assert.That(result.Executed, Is.True);
        Assert.That(result.Value, Is.EqualTo("replied"));
        Assert.That(result.Decision.Status, Is.EqualTo(AgentExecutionDecisionStatus.AllowedAutomatically));
        Assert.That(externalActionCalls, Is.EqualTo(1));
        Assert.That(entry.Action, Is.EqualTo("qzone.reply"));
        Assert.That(entry.Actor, Is.EqualTo("owner:10001"));
        Assert.That(entry.Succeeded, Is.True);
        Assert.That(entry.Error, Is.Null);
    }

    [Test]
    public void TaskServiceTracksLifecycleAndAuditTrail()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentTaskService tasks = new(audit, taskStorePath: Path.Combine(root, "agent-tasks.json"));

        AgentTaskState created = tasks.CreateTask("owner", "Improve agent safety", ["inspect", "implement"]);
        AgentTaskState running = tasks.StartTask(created.Id, "owner");
        AgentTaskState progressed = tasks.RecordProgress(created.Id, "owner", "implemented permission policy");
        AgentTaskState completed = tasks.CompleteTask(created.Id, "owner", "all checks passed");

        Assert.That(created.Status, Is.EqualTo(AgentTaskStatus.Planned));
        Assert.That(running.Status, Is.EqualTo(AgentTaskStatus.Running));
        Assert.That(progressed.Events.Last().Detail, Is.EqualTo("implemented permission policy"));
        Assert.That(completed.Status, Is.EqualTo(AgentTaskStatus.Completed));
        Assert.That(tasks.GetTask(created.Id), Is.EqualTo(completed));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.task.completed"));
        Assert.Throws<InvalidOperationException>(() => tasks.CancelTask(created.Id, "owner", "too late"));
    }

    [Test]
    public void TaskServicePersistsAndReloadsTaskState()
    {
        string root = CreateTempWorkspace();
        string taskStorePath = Path.Combine(root, "agent-tasks.json");
        AgentTaskService tasks = new(taskStorePath: taskStorePath);

        AgentTaskState created = tasks.CreateTask("owner", "Persist agent task", ["inspect", "verify"]);
        AgentTaskState running = tasks.StartTask(created.Id, "owner");
        tasks.RecordProgress(running.Id, "owner", "state written to disk");

        AgentTaskService reloaded = new(taskStorePath: taskStorePath);
        AgentTaskState? restored = reloaded.GetTask(created.Id);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Goal, Is.EqualTo("Persist agent task"));
        Assert.That(restored.Status, Is.EqualTo(AgentTaskStatus.Running));
        Assert.That(restored.Steps, Is.EqualTo(new[] { "inspect", "verify" }));
        Assert.That(restored.Events.Select(taskEvent => taskEvent.Kind), Does.Contain("progress"));
        Assert.That(reloaded.GetLatestTask()?.Id, Is.EqualTo(created.Id));
    }

    [Test]
    public void TaskServiceClosesStaleActiveTasksOnLoad()
    {
        string root = CreateTempWorkspace();
        string taskStorePath = Path.Combine(root, "agent-tasks.json");
        DateTimeOffset staleTime = DateTimeOffset.Now.AddDays(-3);
        AgentTaskState staleRunning = new(
            "stale-running",
            "Old interrupted work",
            ["inspect"],
            AgentTaskStatus.Running,
            staleTime,
            staleTime,
            [new AgentTaskEvent(staleTime, "agent", "started", "Task started.")]);
        AgentTaskState freshRunning = new(
            "fresh-running",
            "Current work",
            ["inspect"],
            AgentTaskStatus.Running,
            DateTimeOffset.Now.AddMinutes(-5),
            DateTimeOffset.Now.AddMinutes(-5),
            [new AgentTaskEvent(DateTimeOffset.Now.AddMinutes(-5), "agent", "started", "Task started.")]);
        File.WriteAllText(taskStorePath, System.Text.Json.JsonSerializer.Serialize(new[] { staleRunning, freshRunning }));

        AgentTaskService reloaded = new(taskStorePath: taskStorePath);

        AgentTaskState? stale = reloaded.GetTask("stale-running");
        AgentTaskState? fresh = reloaded.GetTask("fresh-running");
        Assert.That(stale, Is.Not.Null);
        Assert.That(stale!.Status, Is.EqualTo(AgentTaskStatus.Cancelled));
        Assert.That(stale.Events.Last().Kind, Is.EqualTo("stale-closed"));
        Assert.That(fresh?.Status, Is.EqualTo(AgentTaskStatus.Running));
    }

    [Test]
    public void TaskServiceXmlToolsExposeLifecycleAndParseStepText()
    {
        AgentTaskService tasks = new();

        AgentTaskState task = tasks.CreateTaskFromText("agent", "Improve agent task tools", "inspect\nimplement; verify");
        AgentTaskState running = tasks.StartTask(task.Id, "agent");
        AgentTaskState progressed = tasks.RecordProgress(running.Id, "agent", "implemented XML methods");

        string[] xmlFunctionNames = typeof(AgentTaskService)
            .GetMethods()
            .Select(method => method.GetCustomAttributes(typeof(XmlFunctionAttribute), inherit: false)
                .OfType<XmlFunctionAttribute>()
                .FirstOrDefault())
            .OfType<XmlFunctionAttribute>()
            .Select(attribute => attribute.Name ?? string.Empty)
            .ToArray();

        Assert.That(task.Steps, Is.EqualTo(new[] { "inspect", "implement", "verify" }));
        Assert.That(progressed.Events.Last().Kind, Is.EqualTo("progress"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_task_create"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_task_start"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_task_progress"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_task_complete"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_task_fail"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_task_cancel"));
    }

    [Test]
    public void ProjectStatusSummarizesWorkspaceCommandsAndRecentAudit()
    {
        string root = CreateTempWorkspace();
        AgentWorkspacePolicy workspacePolicy = new([root]);
        AgentCommandPolicy commandPolicy = new([
            new AgentCommandDefinition("test", "Run focused tests", "dotnet", "test", root, TimeSpan.FromSeconds(30))
        ]);
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        audit.Record("workspace.read", "agent", "read status", AgentAuditRiskLevel.Low, true);
        AgentProjectStatusService service = new(workspacePolicy, commandPolicy, audit);

        AgentProjectStatusSnapshot snapshot = service.BuildSnapshot(maxAuditEntries: 5);
        string report = AgentProjectStatusService.FormatSnapshot(snapshot);

        Assert.That(snapshot.WorkspaceRoots, Does.Contain(Path.GetFullPath(root)));
        Assert.That(snapshot.AllowedCommands.Select(command => command.Id), Does.Contain("test"));
        Assert.That(snapshot.RecentAuditEntries.Select(entry => entry.Action), Does.Contain("workspace.read"));
        Assert.That(report, Does.Contain("Agent project status"));
        Assert.That(report, Does.Contain("Workspace roots:"));
        Assert.That(report, Does.Contain("test: Run focused tests"));
        Assert.That(report, Does.Contain("workspace.read"));
    }

    [Test]
    public void ProjectStatusDefaultPolicyReportsBuildAndTestVerificationCommands()
    {
        AgentProjectStatusService service = new();

        AgentProjectStatusSnapshot snapshot = service.BuildSnapshot();

        Assert.That(snapshot.AllowedCommands.Select(command => command.Id), Does.Contain("dotnet-build-solution"));
        Assert.That(snapshot.AllowedCommands.Select(command => command.Id), Does.Contain("dotnet-test-solution"));
    }

    [Test]
    public void IssueReportCombinesRuntimeErrorsFailedAuditAndUnhealthyModules()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        audit.Record("agent.command.test", "owner", "dotnet test", AgentAuditRiskLevel.High, false, "exit 1");
        AgentIssueReportService service = new(audit)
        {
            HealthReporterSourceOverride =
            [
                new StubHealthReporter("QChat", ModuleHealthStatus.Degraded, "OneBot disconnected."),
                new StubHealthReporter("Memory", ModuleHealthStatus.Healthy, "Memory ready.")
            ]
        };
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 12,
            LastError: "LLM request failed",
            RecentEvents: [
                new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-14T01:00:00Z"), "Info", "started"),
                new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-14T01:01:00Z"), "Error", "LLM request failed")
            ]);

        AgentIssueReportSnapshot snapshot = service.BuildSnapshot(runtime, maxAuditEntries: 5);
        string report = AgentIssueReportService.FormatSnapshot(snapshot);

        Assert.That(snapshot.LastError, Is.EqualTo("LLM request failed"));
        Assert.That(snapshot.RuntimeErrors.Select(runtimeEvent => runtimeEvent.Detail), Does.Contain("LLM request failed"));
        Assert.That(snapshot.FailedAuditEntries.Select(entry => entry.Action), Does.Contain("agent.command.test"));
        Assert.That(snapshot.UnhealthyModules.Select(module => module.Name), Does.Contain("QChat"));
        Assert.That(snapshot.UnhealthyModules.Select(module => module.Name), Does.Not.Contain("Memory"));
        Assert.That(report, Does.Contain("Agent issue report"));
        Assert.That(report, Does.Contain("LLM request failed"));
        Assert.That(report, Does.Contain("agent.command.test"));
        Assert.That(report, Does.Contain("[Degraded] QChat"));
    }

    [Test]
    public void MaintenanceServiceCreatesOwnerConfirmedRepairProposalFromIssueReport()
    {
        AgentIssueReportSnapshot issueReport = new(
            DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
            "System.InvalidOperationException: Browser runtime failed",
            [
                new ChatRuntimeEvent(
                    DateTimeOffset.Parse("2026-06-15T09:59:00Z"),
                    "Error",
                    "System.InvalidOperationException: Browser runtime failed")
            ],
            [
                new AgentAuditLogEntry(
                    DateTimeOffset.Parse("2026-06-15T09:58:00Z"),
                    "agent.command.test",
                    "agent",
                    "dotnet test failed",
                    AgentAuditRiskLevel.High,
                    false,
                    "1 failed test")
            ],
            [
                new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")
            ]);
        AgentMaintenanceService service = new();

        AgentMaintenanceProposal proposal = service.ProposeFromIssueReport(issueReport, "agent");
        string formatted = AgentMaintenanceService.FormatProposal(proposal);

        Assert.That(proposal.CanApplyAutomatically, Is.False);
        Assert.That(proposal.RequiresOwnerConfirmationForExecution, Is.True);
        Assert.That(proposal.RiskLevel, Is.EqualTo(AgentAuditRiskLevel.High));
        Assert.That(proposal.Evidence, Does.Contain("Browser runtime failed"));
        Assert.That(proposal.SuggestedNextSteps, Has.Some.Contains("workspace_propose_replace"));
        Assert.That(formatted, Does.Contain("No files or configuration were changed"));
        Assert.That(formatted, Does.Contain("workspace_apply_proposal"));
        Assert.That(service.GetPendingProposals().Select(item => item.Id), Does.Contain(proposal.Id));
    }

    [Test]
    public void MaintenanceServicePersistsPendingProposalsAcrossRestart()
    {
        string root = CreateTempWorkspace();
        string storePath = Path.Combine(root, "maintenance-proposals.json");
        AgentIssueReportSnapshot issueReport = new(
            DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
            "Browser runtime failed",
            [new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-15T09:59:00Z"), "Error", "Browser runtime failed")],
            [],
            [new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]);
        AgentMaintenanceService writer = new(proposalStorePath: storePath);

        AgentMaintenanceProposal proposal = writer.ProposeFromIssueReport(issueReport, "agent");
        AgentMaintenanceService reader = new(proposalStorePath: storePath);

        AgentMaintenanceProposal restored = reader.GetPendingProposals().Single();
        Assert.That(restored.Id, Is.EqualTo(proposal.Id));
        Assert.That(restored.Title, Does.Contain("Browser runtime failed"));
        Assert.That(restored.CanApplyAutomatically, Is.False);
        Assert.That(restored.RequiresOwnerConfirmationForExecution, Is.True);
    }

    [Test]
    public void MaintenanceServiceArchivesProposalAndPersistsResolution()
    {
        string root = CreateTempWorkspace();
        string storePath = Path.Combine(root, "maintenance-proposals.json");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentMaintenanceService writer = new(auditLog: audit, proposalStorePath: storePath);
        AgentMaintenanceProposal proposal = writer.ProposeFromIssueReport(
            new AgentIssueReportSnapshot(
                DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
                "Browser runtime failed",
                [new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-15T09:59:00Z"), "Error", "Browser runtime failed")],
                [],
                [new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]),
            "agent");

        AgentMaintenanceArchiveResult result = writer.ArchiveProposal(proposal.Id, "owner", "fixed by workspace proposal and tests");
        AgentMaintenanceService reader = new(proposalStorePath: storePath);

        Assert.That(result.Archived, Is.True);
        Assert.That(writer.GetPendingProposals(), Is.Empty);
        Assert.That(reader.GetPendingProposals(), Is.Empty);
        Assert.That(reader.GetArchivedProposals().Select(item => item.Proposal.Id), Does.Contain(proposal.Id));
        Assert.That(reader.GetArchivedProposals().Single().Resolution, Does.Contain("fixed by workspace proposal"));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.maintenance.archived"));
    }

    [Test]
    public void MaintenanceServiceRecordsRepairEvidenceAndPersistsLinks()
    {
        string root = CreateTempWorkspace();
        string storePath = Path.Combine(root, "maintenance-proposals.json");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentMaintenanceService writer = new(auditLog: audit, proposalStorePath: storePath);
        AgentMaintenanceProposal proposal = writer.ProposeFromIssueReport(
            new AgentIssueReportSnapshot(
                DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
                "Browser runtime failed",
                [new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-15T09:59:00Z"), "Error", "Browser runtime failed")],
                [],
                [new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]),
            "agent");

        AgentMaintenanceRepairEvidence evidence = writer.RecordRepairEvidence(
            proposal.Id,
            workspaceProposalId: "workspace-proposal-1",
            verificationCommandId: "dotnet-test-solution",
            verificationSummary: "0 failed; 122 passed",
            actor: "agent",
            notes: "Patched AgentMaintenanceService and reran full solution tests.");
        AgentMaintenanceService reader = new(proposalStorePath: storePath);

        AgentMaintenanceRepairEvidence restored = reader.GetRepairEvidence(proposal.Id).Single();
        Assert.That(evidence.ProposalId, Is.EqualTo(proposal.Id));
        Assert.That(restored.WorkspaceProposalId, Is.EqualTo("workspace-proposal-1"));
        Assert.That(restored.VerificationCommandId, Is.EqualTo("dotnet-test-solution"));
        Assert.That(restored.VerificationSummary, Does.Contain("122 passed"));
        Assert.That(restored.Notes, Does.Contain("Patched AgentMaintenanceService"));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.maintenance.repair_evidence"));
    }

    [Test]
    public void MaintenanceServiceInspectsIssueReportWithDuplicateCooldown()
    {
        string root = CreateTempWorkspace();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T10:00:00Z");
        AgentIssueReportSnapshot issueReport = new(
            now,
            "Browser runtime failed",
            [new ChatRuntimeEvent(now.AddMinutes(-1), "Error", "Browser runtime failed")],
            [],
            [new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]);
        AgentMaintenanceService service = new(
            proposalStorePath: Path.Combine(root, "maintenance-proposals.json"),
            clock: () => now);

        AgentMaintenanceInspectionResult first = service.InspectIssueReport(
            issueReport,
            "agent",
            duplicateCooldown: TimeSpan.FromHours(2));
        AgentMaintenanceInspectionResult duplicate = service.InspectIssueReport(
            issueReport,
            "agent",
            duplicateCooldown: TimeSpan.FromHours(2));
        now = now.AddHours(3);
        AgentMaintenanceInspectionResult afterCooldown = service.InspectIssueReport(
            issueReport,
            "agent",
            duplicateCooldown: TimeSpan.FromHours(2));

        Assert.That(first.Created, Is.True);
        Assert.That(duplicate.Created, Is.False);
        Assert.That(duplicate.Proposal?.Id, Is.EqualTo(first.Proposal?.Id));
        Assert.That(afterCooldown.Created, Is.True);
        Assert.That(service.GetPendingProposals(), Has.Count.EqualTo(2));
    }

    [Test]
    public void MaintenanceServiceIgnoresExpectedWaitingHealthWithoutErrors()
    {
        string root = CreateTempWorkspace();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T10:00:00Z");
        AgentMaintenanceService service = new(
            proposalStorePath: Path.Combine(root, "maintenance-proposals.json"),
            clock: () => now);
        AgentIssueReportSnapshot browserStillStarting = new(
            now,
            null,
            [],
            [],
            [new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized yet.")]);
        AgentIssueReportSnapshot oneBotDisconnected = new(
            now,
            null,
            [],
            [],
            [new ModuleHealth("QChat", ModuleHealthStatus.Degraded, "OneBot is configured but disconnected.")]);

        AgentMaintenanceInspectionResult browserResult = service.InspectIssueReport(
            browserStillStarting,
            "agent",
            duplicateCooldown: TimeSpan.FromHours(2));
        AgentMaintenanceInspectionResult qchatResult = service.InspectIssueReport(
            oneBotDisconnected,
            "agent",
            duplicateCooldown: TimeSpan.FromHours(2));

        Assert.That(browserResult.Created, Is.False);
        Assert.That(qchatResult.Created, Is.False);
        Assert.That(service.GetPendingProposals(), Is.Empty);
    }

    [Test]
    public void MaintenanceServiceTreatsStartupFailureHealthAsActionable()
    {
        string root = CreateTempWorkspace();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T10:00:00Z");
        AgentMaintenanceService service = new(
            proposalStorePath: Path.Combine(root, "maintenance-proposals.json"),
            clock: () => now);
        AgentIssueReportSnapshot startupFailure = new(
            now,
            null,
            [],
            [],
            [new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime did not initialize during module startup: timeout.")]);

        AgentMaintenanceInspectionResult result = service.InspectIssueReport(
            startupFailure,
            "agent",
            duplicateCooldown: TimeSpan.FromHours(2));

        Assert.That(result.Created, Is.True);
        Assert.That(result.Proposal?.Title, Does.Contain("Browser"));
        Assert.That(service.GetPendingProposals(), Has.Count.EqualTo(1));
    }

    [Test]
    public void MaintenanceServiceDeduplicatesSameBrowserRootCauseWithDifferentErrorNoise()
    {
        string root = CreateTempWorkspace();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T10:00:00Z");
        AgentMaintenanceService service = new(
            proposalStorePath: Path.Combine(root, "maintenance-proposals.json"),
            clock: () => now);
        AgentIssueReportSnapshot noisyBrowserFailure = new(
            now,
            "System.InvalidOperationException: Browser runtime failed",
            [new ChatRuntimeEvent(now.AddMinutes(-1), "Error", "System.InvalidOperationException: Browser runtime failed")],
            [
                new AgentAuditLogEntry(
                    now.AddMinutes(-1),
                    "agent.command.test",
                    "agent",
                    "dotnet test failed",
                    AgentAuditRiskLevel.Medium,
                    Succeeded: false,
                    Error: "1 failed test")
            ],
            [new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]);
        AgentIssueReportSnapshot sameBrowserFailureWithoutAuditNoise = new(
            now.AddMinutes(1),
            "Browser runtime failed",
            [new ChatRuntimeEvent(now, "Error", "Browser runtime failed")],
            [],
            [new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]);

        AgentMaintenanceInspectionResult first = service.InspectIssueReport(
            noisyBrowserFailure,
            "agent",
            duplicateCooldown: TimeSpan.FromHours(2));
        AgentMaintenanceInspectionResult duplicate = service.InspectIssueReport(
            sameBrowserFailureWithoutAuditNoise,
            "agent",
            duplicateCooldown: TimeSpan.FromHours(2));

        Assert.That(first.Created, Is.True);
        Assert.That(duplicate.Created, Is.False);
        Assert.That(duplicate.Proposal?.Id, Is.EqualTo(first.Proposal?.Id));
        Assert.That(service.GetPendingProposals(), Has.Count.EqualTo(1));
    }

    [Test]
    public void MaintenanceServiceCompactsDuplicatePersistedPendingProposalsOnLoad()
    {
        string root = CreateTempWorkspace();
        string storePath = Path.Combine(root, "maintenance-proposals.json");
        DateTimeOffset older = DateTimeOffset.Parse("2026-06-15T09:00:00Z");
        DateTimeOffset newer = DateTimeOffset.Parse("2026-06-15T10:00:00Z");
        AgentMaintenanceProposalPersistenceState state = new()
        {
            Pending =
            [
                new AgentMaintenanceProposal(
                    "older-browser",
                    older,
                    "agent",
                    "System.InvalidOperationException: Browser runtime failed",
                    "Last error: System.InvalidOperationException: Browser runtime failed\nUnhealthy module: Browser; Degraded; Browser runtime is not initialized.",
                    ["inspect"],
                    AgentAuditRiskLevel.Medium,
                    RequiresOwnerConfirmationForExecution: true,
                    CanApplyAutomatically: false),
                new AgentMaintenanceProposal(
                    "newer-browser",
                    newer,
                    "agent",
                    "Browser runtime failed",
                    "Last error: Browser runtime failed\nFailed audit: agent.command.test; dotnet test failed; error=1 failed test\nUnhealthy module: Browser; Degraded; Browser runtime is not initialized.",
                    ["inspect"],
                    AgentAuditRiskLevel.Medium,
                    RequiresOwnerConfirmationForExecution: true,
                    CanApplyAutomatically: false)
            ]
        };
        File.WriteAllText(storePath, System.Text.Json.JsonSerializer.Serialize(state));

        AgentMaintenanceService service = new(proposalStorePath: storePath);

        IReadOnlyList<AgentMaintenanceProposal> pending = service.GetPendingProposals();
        Assert.That(pending, Has.Count.EqualTo(1));
        Assert.That(pending[0].Id, Is.EqualTo("newer-browser"));
    }

    [Test]
    public void AgentControlCenterBuildsReadOnlySnapshot()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        audit.Record("workspace.replace", "owner", "path=src/AgentNote.cs", AgentAuditRiskLevel.High, true);
        audit.Record("agent.command.test", "owner", "dotnet test", AgentAuditRiskLevel.High, false, "exit 1");
        AgentTaskService tasks = new(audit, taskStorePath: Path.Combine(root, "tasks.json"));
        AgentTaskState task = tasks.CreateTask("owner", "Build control center", ["inspect", "render"]);
        tasks.StartTask(task.Id, "owner");
        AgentWorkspaceService workspace = new(new AgentWorkspacePolicy([root]), auditLog: audit);
        string file = Path.Combine(root, "src", "AgentNote.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "class AgentNote {}\n");
        workspace.ProposeReplace("src/AgentNote.cs", "AgentNote", "GeneratedAgentNote");
        AgentMaintenanceService maintenance = new(auditLog: audit);
        AgentMaintenanceProposal maintenanceProposal = maintenance.ProposeFromIssueReport(
            new AgentIssueReportSnapshot(
                DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
                "Browser runtime failed",
                [new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-15T09:59:00Z"), "Error", "Browser runtime failed")],
                [],
                [new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]),
            "agent");
        maintenance.RecordRepairEvidence(
            maintenanceProposal.Id,
            "workspace-proposal-1",
            "dotnet-test-solution",
            "0 failed; focused tests passed",
            "agent",
            "linked repair evidence for control center");
        AgentCommandPolicy commandPolicy = new([
            new AgentCommandDefinition("test", "Run tests", "dotnet", "test", root, TimeSpan.FromSeconds(30))
        ]);
        AgentProactiveBehaviorService proactive = new();
        AgentControlCenterService service = new(
            new AgentDiagnosticsService
            {
                HealthReporterSourceOverride = [new StubHealthReporter("QChat", ModuleHealthStatus.Degraded, "OneBot disconnected.")],
                CapabilitySourceOverride = [new StubCapability("Workspace", EmbodiedCapabilityKind.Tool, "Restricted workspace tools.", "ready")]
            },
            new AgentIssueReportService(audit)
            {
                HealthReporterSourceOverride = [new StubHealthReporter("QChat", ModuleHealthStatus.Degraded, "OneBot disconnected.")]
            },
            tasks,
            workspace,
            new AgentWorkspacePolicy([root]),
            commandPolicy,
            audit,
            maintenance: maintenance)
        {
            ProactiveBehavior = proactive
        };
        ChatRuntimeState runtime = new(
            IsChatting: true,
            PendingPokeCount: 1,
            ChatHistoryCount: 5,
            LastError: "LLM request failed",
            RecentEvents: [new ChatRuntimeEvent(DateTimeOffset.Now, "Error", "LLM request failed")]);
        proactive.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneReply,
            "reply to qzone comment",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "reply target=1001 post=post-a"));
        AgentProactivePendingSuggestion confirmed = proactive.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneLike,
            "like private qzone post",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "like target=1001 post=post-b"));
        proactive.ConfirmPendingSuggestion(confirmed.Id, "owner");

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(runtime, "Kira");

        Assert.That(snapshot.AgentState.CharacterName, Is.EqualTo("Kira"));
        Assert.That(snapshot.AgentState.IsChatting, Is.True);
        Assert.That(snapshot.LatestTask?.Goal, Is.EqualTo("Build control center"));
        Assert.That(snapshot.PendingWorkspaceProposals, Has.Count.EqualTo(1));
        Assert.That(snapshot.AllowedCommands.Select(command => command.Id), Does.Contain("test"));
        Assert.That(snapshot.RecentAuditEntries.Select(entry => entry.Action), Does.Contain("workspace.replace"));
        Assert.That(snapshot.IssueReport.LastError, Is.EqualTo("LLM request failed"));
        Assert.That(snapshot.IssueReport.FailedAuditEntries.Select(entry => entry.Action), Does.Contain("agent.command.test"));
        Assert.That(snapshot.WorkspaceRoots, Does.Contain(Path.GetFullPath(root)));
        Assert.That(snapshot.PendingProactiveSuggestions, Has.Count.EqualTo(1));
        Assert.That(snapshot.PendingProactiveSuggestions[0].Suggestion.Kind, Is.EqualTo(AgentProactiveActionKind.QZoneReply));
        Assert.That(snapshot.CompletedProactiveSuggestions, Has.Count.EqualTo(1));
        Assert.That(snapshot.CompletedProactiveSuggestions[0].Status, Is.EqualTo(AgentProactivePendingStatus.Confirmed));
        Assert.That(snapshot.PendingMaintenanceProposals.Select(proposal => proposal.Id), Does.Contain(maintenanceProposal.Id));
        Assert.That(snapshot.PendingMaintenanceProposals[0].CanApplyAutomatically, Is.False);
        Assert.That(snapshot.MaintenanceRepairEvidenceByProposalId[maintenanceProposal.Id][0].WorkspaceProposalId,
            Is.EqualTo("workspace-proposal-1"));
        Assert.That(snapshot.MaintenanceRepairEvidenceByProposalId[maintenanceProposal.Id][0].VerificationSummary,
            Does.Contain("focused tests passed"));
    }

    [Test]
    public void AgentControlCenterBuildsHealthTriageSnapshot()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService service = new(
            issueReports: new AgentIssueReportService(audit)
            {
                HealthReporterSourceOverride =
                [
                    new StubHealthReporter("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized yet."),
                    new StubHealthReporter("QChat", ModuleHealthStatus.Degraded, "OneBot is configured but disconnected."),
                    new StubHealthReporter("DeskPet", ModuleHealthStatus.Degraded, "DeskPet runtime did not initialize during module startup: timeout.")
                ]
            },
            auditLog: audit,
            maintenance: new AgentMaintenanceService(auditLog: audit, proposalStorePath: Path.Combine(root, "maintenance.json")));

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "Kira");

        Assert.That(snapshot.HealthTriage.Waiting.Select(health => health.Name), Does.Contain("Browser"));
        Assert.That(snapshot.HealthTriage.ExternalEnvironment.Select(health => health.Name), Does.Contain("QChat"));
        Assert.That(snapshot.HealthTriage.Faults.Select(health => health.Name), Does.Contain("DeskPet"));
        Assert.That(snapshot.HealthTriage.OwnerConfirmationItems, Is.Empty);
    }

    [Test]
    public void AgentControlCenterCleanupPreviewReportsRuntimeNoise()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentTaskService tasks = new(audit, taskStorePath: Path.Combine(root, "tasks.json"));
        AgentTaskState completed = tasks.CreateTask("agent", "Old finished task", ["verify"]);
        tasks.CompleteTask(completed.Id, "agent", "done");
        string taskStorePath = Path.Combine(root, "tasks.json");
        DateTimeOffset staleTime = DateTimeOffset.Now.AddDays(-45);
        AgentTaskState staleCompleted = tasks.GetTask(completed.Id)! with
        {
            CreatedAt = staleTime,
            UpdatedAt = staleTime
        };
        File.WriteAllText(taskStorePath, System.Text.Json.JsonSerializer.Serialize(new[] { staleCompleted }));
        tasks = new AgentTaskService(audit, taskStorePath: taskStorePath);
        AgentMaintenanceService maintenance = new(auditLog: audit, proposalStorePath: Path.Combine(root, "maintenance.json"));
        maintenance.ProposeFromIssueReport(
            new AgentIssueReportSnapshot(
                DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
                "Browser runtime failed",
                [new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-15T09:59:00Z"), "Error", "Browser runtime failed")],
                [],
                [new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]),
            "agent");
        string diagnosticsPath = Path.Combine(root, "qchat-diagnostics.jsonl");
        File.WriteAllLines(diagnosticsPath, Enumerable.Range(1, 12).Select(index => $"{{\"index\":{index}}}"));
        AgentControlCenterService service = new(
            tasks: tasks,
            auditLog: audit,
            maintenance: maintenance,
            qchatDiagnosticsPath: diagnosticsPath);

        AgentControlCenterCleanupPreview preview = service.BuildRuntimeCleanupPreview(
            maxTerminalTaskAge: TimeSpan.FromDays(30),
            maxPendingMaintenanceAge: TimeSpan.Zero,
            maxDiagnosticLines: 5);

        Assert.That(preview.StaleTerminalTaskCount, Is.EqualTo(1));
        Assert.That(preview.StaleMaintenanceProposalCount, Is.EqualTo(1));
        Assert.That(preview.ExcessDiagnosticLineCount, Is.EqualTo(7));
        Assert.That(preview.RequiresOwnerConfirmation, Is.True);
        Assert.That(preview.HasWork, Is.True);
    }

    [Test]
    public void AgentControlCenterCleanupArchivesTrimsAndAudits()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentTaskService tasks = new(audit, taskStorePath: Path.Combine(root, "tasks.json"));
        AgentTaskState completed = tasks.CreateTask("agent", "Old finished task", ["verify"]);
        tasks.CompleteTask(completed.Id, "agent", "done");
        string taskStorePath = Path.Combine(root, "tasks.json");
        DateTimeOffset staleTime = DateTimeOffset.Now.AddDays(-45);
        AgentTaskState staleCompleted = tasks.GetTask(completed.Id)! with
        {
            CreatedAt = staleTime,
            UpdatedAt = staleTime
        };
        File.WriteAllText(taskStorePath, System.Text.Json.JsonSerializer.Serialize(new[] { staleCompleted }));
        tasks = new AgentTaskService(audit, taskStorePath: taskStorePath);
        AgentMaintenanceService maintenance = new(auditLog: audit, proposalStorePath: Path.Combine(root, "maintenance.json"));
        maintenance.ProposeFromIssueReport(
            new AgentIssueReportSnapshot(
                DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
                "Browser runtime failed",
                [new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-15T09:59:00Z"), "Error", "Browser runtime failed")],
                [],
                [new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]),
            "agent");
        string diagnosticsPath = Path.Combine(root, "qchat-diagnostics.jsonl");
        File.WriteAllLines(diagnosticsPath, Enumerable.Range(1, 12).Select(index => $"{{\"index\":{index}}}"));
        AgentControlCenterService service = new(
            tasks: tasks,
            auditLog: audit,
            maintenance: maintenance,
            qchatDiagnosticsPath: diagnosticsPath);

        AgentControlCenterCleanupResult result = service.CleanupRuntimeNoiseFromControlCenter(
            maxTerminalTaskAge: TimeSpan.FromDays(30),
            maxPendingMaintenanceAge: TimeSpan.Zero,
            maxDiagnosticLines: 5);

        Assert.That(result.Applied, Is.True);
        Assert.That(result.RemovedTerminalTaskCount, Is.EqualTo(1));
        Assert.That(result.ArchivedMaintenanceProposalCount, Is.EqualTo(1));
        Assert.That(result.TrimmedDiagnosticLineCount, Is.EqualTo(7));
        Assert.That(tasks.GetTasks(), Is.Empty);
        Assert.That(maintenance.GetPendingProposals(), Is.Empty);
        Assert.That(maintenance.GetArchivedProposals(), Has.Count.EqualTo(1));
        Assert.That(File.ReadAllLines(diagnosticsPath), Has.Length.EqualTo(5));
        Assert.That(audit.GetRecentEntries(20).Select(entry => entry.Action), Does.Contain("agent.control.cleanup"));
    }

    [Test]
    public void AgentControlCenterBuildsAttentionSummaryForOwnerAndAutonomousWork()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentWorkspaceService workspace = new(new AgentWorkspacePolicy([root]), auditLog: audit);
        string file = Path.Combine(root, "src", "AgentNote.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "class AgentNote {}\n");
        workspace.ProposeReplace("src/AgentNote.cs", "AgentNote", "GeneratedAgentNote");
        AgentMaintenanceService maintenance = new(auditLog: audit, proposalStorePath: Path.Combine(root, "maintenance.json"));
        maintenance.ProposeFromIssueReport(
            new AgentIssueReportSnapshot(
                DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
                "Browser runtime failed",
                [new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-15T09:59:00Z"), "Error", "Browser runtime failed")],
                [],
                [new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]),
            "agent");
        AgentProactiveBehaviorService proactive = new();
        proactive.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneReply,
            "reply to qzone comment",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "reply target=1001 post=post-a"));
        AgentControlCenterService service = new(workspace: workspace, auditLog: audit, maintenance: maintenance)
        {
            ProactiveBehavior = proactive
        };
        service.ProposeConfigurationChange("OwnerUserIds", "10001", "agent", "attempt to claim owner access");
        service.ApplyConfigurationChange(
            "MaintenanceDuplicateCooldownMinutes",
            "90",
            "agent",
            "autonomously reduce repeated maintenance proposal noise");

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "Kira");

        Assert.That(snapshot.AttentionSummary.OwnerConfirmationRequiredCount, Is.EqualTo(4));
        Assert.That(snapshot.AttentionSummary.OwnerConfirmationItems, Has.Some.Contains("Configuration: OwnerUserIds"));
        Assert.That(snapshot.AttentionSummary.OwnerConfirmationItems, Has.Some.Contains("Workspace: src/AgentNote.cs"));
        Assert.That(snapshot.AttentionSummary.OwnerConfirmationItems, Has.Some.Contains("Maintenance:"));
        Assert.That(snapshot.AttentionSummary.OwnerConfirmationItems, Has.Some.Contains("Proactive: QZoneReply"));
        Assert.That(snapshot.AttentionSummary.AutonomousLowRiskActivityCount, Is.EqualTo(1));
        Assert.That(snapshot.AttentionSummary.AutonomousLowRiskItems, Has.Some.Contains("agent.config.applied"));
    }

    [Test]
    public void AgentControlCenterBuildsOwnerNotificationSummaryWithoutLowRiskNoise()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        audit.Record("agent.command.test", "agent", "dotnet test", AgentAuditRiskLevel.High, false, "exit 1");
        audit.Record("agent.command.test", "agent", "dotnet test", AgentAuditRiskLevel.High, false, "exit 1");
        audit.Record("agent.command.test", "agent", "dotnet test", AgentAuditRiskLevel.High, false, "exit 1");
        AgentControlCenterService service = new(
            issueReports: new AgentIssueReportService(audit)
            {
                HealthReporterSourceOverride = [new StubHealthReporter("QChat", ModuleHealthStatus.Unavailable, "OneBot disconnected.")]
            },
            auditLog: audit);
        service.ProposeConfigurationChange("OwnerUserIds", "10001", "agent", "attempt to claim owner access");
        service.ApplyConfigurationChange(
            "MaintenanceDuplicateCooldownMinutes",
            "90",
            "agent",
            "autonomously reduce repeated maintenance proposal noise");

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "Kira");

        Assert.That(snapshot.NotificationSummary.ShouldNotifyOwner, Is.True);
        Assert.That(snapshot.NotificationSummary.Items, Has.Count.EqualTo(3));
        Assert.That(snapshot.NotificationSummary.Items.Select(item => item.Kind), Does.Contain("owner-confirmation"));
        Assert.That(snapshot.NotificationSummary.Items.Select(item => item.Kind), Does.Contain("repeated-failure"));
        Assert.That(snapshot.NotificationSummary.Items.Select(item => item.Kind), Does.Contain("qq-environment"));
        Assert.That(snapshot.NotificationSummary.Items.Select(item => item.Message), Has.Some.Contains("OwnerUserIds"));
        Assert.That(snapshot.NotificationSummary.Items.Select(item => item.Message), Has.Some.Contains("agent.command.test failed 3 times"));
        Assert.That(snapshot.NotificationSummary.Items.Select(item => item.Message), Has.Some.Contains("QChat"));
        Assert.That(snapshot.NotificationSummary.Items.Select(item => item.Message), Has.None.Contains("agent.config.applied"));
    }

    [Test]
    public void AgentControlCenterExposesSecurityGatewayPreview()
    {
        AgentControlCenterService service = new();

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "Kira");

        Assert.That(snapshot.SecurityGatewayPreview.Select(item => item.Action), Does.Contain("maintenance.inspect"));
        Assert.That(snapshot.SecurityGatewayPreview.Single(item => item.Action == "maintenance.inspect").Status,
            Is.EqualTo(AgentExecutionDecisionStatus.AllowedAutomatically));
        Assert.That(snapshot.SecurityGatewayPreview.Single(item => item.Action == "workspace.apply").Status,
            Is.EqualTo(AgentExecutionDecisionStatus.OwnerConfirmationRequired));
        Assert.That(snapshot.SecurityGatewayPreview.Single(item => item.Action == "qzone.reply").Status,
            Is.EqualTo(AgentExecutionDecisionStatus.OwnerConfirmationRequired));
        Assert.That(snapshot.SecurityGatewayPreview.Single(item => item.Action == "github.upload").Status,
            Is.EqualTo(AgentExecutionDecisionStatus.OwnerConfirmationRequired));
    }

    [Test]
    public void AgentControlCenterExposesRuntimeVisibilityForStreamingLatencyEventsAndBackgroundWork()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        audit.Record("qzone.like", "group:20002", "target=1001 post=post-a", AgentAuditRiskLevel.High, false, "Blocked: owner authority required");
        audit.Record("qzone.reply", "owner:10001", "target=1001 post=post-b", AgentAuditRiskLevel.High, true);
        AgentEventPipeline eventPipeline = new();
        eventPipeline.Register(new AgentEventMatcher(
            Name: "owner-command",
            Priority: 100,
            Rule: _ => true,
            Permission: _ => true,
            Handler: _ => Task.CompletedTask,
            Block: true));
        AgentControlCenterService service = new(auditLog: audit, eventPipeline: eventPipeline);
        service.RecordBackgroundTaskResult(AgentBackgroundTaskResult.Completed(
            "task-1",
            "browser-scan",
            "qq:group:1000",
            "found 2 pages",
            DateTimeOffset.Parse("2026-06-15T12:00:00Z")));
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 3,
            LastError: null,
            RecentEvents: [new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-15T12:00:01Z"), "ChatEnd", "Chat streaming ended.")])
        {
            Latency = new ChatLatencySnapshot(
                DateTimeOffset.Parse("2026-06-15T12:00:00Z"),
                DateTimeOffset.Parse("2026-06-15T12:00:00.420Z"),
                DateTimeOffset.Parse("2026-06-15T12:00:02Z"),
                TimeSpan.FromMilliseconds(420),
                TimeSpan.FromSeconds(2))
        };

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(runtime, "Kira");

        Assert.That(snapshot.RuntimeVisibility.StreamingPolicies.Select(policy => policy.Name),
            Is.SupersetOf(new[] { "QQ group", "QQ private", "DeskPet/UI" }));
        Assert.That(snapshot.RuntimeVisibility.ChatLatency.LastFirstContentLatencyMs, Is.EqualTo(420));
        Assert.That(snapshot.RuntimeVisibility.ChatLatency.LastChatDurationMs, Is.EqualTo(2000));
        Assert.That(snapshot.RuntimeVisibility.EventPipeline.MatcherCount, Is.EqualTo(1));
        Assert.That(snapshot.RuntimeVisibility.EventPipeline.Matchers[0].Name, Is.EqualTo("owner-command"));
        Assert.That(snapshot.RuntimeVisibility.BackgroundTasks[0].TaskName, Is.EqualTo("browser-scan"));
        Assert.That(snapshot.RuntimeVisibility.ActionGatewayAudit.BlockedCount, Is.EqualTo(1));
        Assert.That(snapshot.RuntimeVisibility.ActionGatewayAudit.SucceededCount, Is.EqualTo(1));
    }

    [Test]
    public void AgentControlCenterExposesQChatRuntimeVisibility()
    {
        string root = CreateTempWorkspace();
        string diagnosticsPath = Path.Combine(root, "qchat-diagnostics.jsonl");
        File.WriteAllLines(diagnosticsPath, [
            """{"timestamp":"2026-06-16T10:00:00+08:00","eventName":"start","detail":"QChat service starting.","data":{"BotId":3340947887},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:01+08:00","eventName":"connect-succeeded","detail":"OneBot connected.","data":{"BotId":3340947887,"IsConnected":true},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:02+08:00","eventName":"message-dispatching","detail":"Dispatching message event to QChat.","data":{"MessageType":"Private","UserId":3045846738,"GroupId":0,"isMentionedOrWoken":true},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:03+08:00","eventName":"qchat-sent","detail":"QChat XML tool sent a QQ message.","data":{"type":"Private","targetId":3045846738,"message":"ok"},"exception":null}"""
        ]);
        AgentControlCenterService service = new(qchatDiagnosticsPath: diagnosticsPath);

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "Kira");

        Assert.That(snapshot.RuntimeVisibility.QChat.RecentConnectSucceededCount, Is.EqualTo(1));
        Assert.That(snapshot.RuntimeVisibility.QChat.RecentInboundMessageCount, Is.EqualTo(1));
        Assert.That(snapshot.RuntimeVisibility.QChat.RecentOutboundMessageCount, Is.EqualTo(1));
        Assert.That(snapshot.RuntimeVisibility.QChat.RecentFailureCount, Is.Zero);
        Assert.That(snapshot.RuntimeVisibility.QChat.LastConnectSucceededAt,
            Is.EqualTo(DateTimeOffset.Parse("2026-06-16T10:00:01+08:00")));
    }

    [Test]
    public void AgentControlCenterReportsQChatFailuresInSelfCheck()
    {
        string root = CreateTempWorkspace();
        string diagnosticsPath = Path.Combine(root, "qchat-diagnostics.jsonl");
        File.WriteAllLines(diagnosticsPath, [
            """{"timestamp":"2026-06-16T10:00:01+08:00","eventName":"connect-succeeded","detail":"OneBot connected.","data":{"BotId":3340947887,"IsConnected":true},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:02+08:00","eventName":"qchat-send-failed","detail":"network unavailable","data":{"type":"Group","targetId":867165927},"exception":"System.InvalidOperationException: network unavailable"}""",
            """{"timestamp":"2026-06-16T10:00:03+08:00","eventName":"model-dispatch-failed","detail":"model timeout","data":{"MessageType":"Private","TargetId":3045846738},"exception":"System.TimeoutException: model timeout"}"""
        ]);
        AgentControlCenterService service = new(qchatDiagnosticsPath: diagnosticsPath);

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "Kira");

        AgentControlCenterSelfCheckItem failure = snapshot.SelfCheck.Items
            .Single(item => item.Category == "qq-runtime-failure");
        Assert.That(snapshot.RuntimeVisibility.QChat.RecentFailureCount, Is.EqualTo(2));
        Assert.That(snapshot.RuntimeVisibility.QChat.RecentFailures, Has.Some.Contains("network unavailable"));
        Assert.That(snapshot.RuntimeVisibility.QChat.RecentFailures, Has.Some.Contains("model timeout"));
        Assert.That(failure.CanAgentHandleAutonomously, Is.False);
        Assert.That(failure.RecommendedAction, Does.Contain("Do not retry noisy QQ output automatically"));
    }

    [Test]
    public void AgentControlCenterFlagsQChatOutputVolumeAsLowRiskSelfCheck()
    {
        string root = CreateTempWorkspace();
        string diagnosticsPath = Path.Combine(root, "qchat-diagnostics.jsonl");
        List<string> lines = [
            """{"timestamp":"2026-06-16T10:00:01+08:00","eventName":"connect-succeeded","detail":"OneBot connected.","data":{"BotId":3340947887,"IsConnected":true},"exception":null}"""
        ];
        for (int i = 0; i < 6; i++)
        {
            lines.Add($$"""{"timestamp":"2026-06-16T10:00:0{{i + 2}}+08:00","eventName":"qchat-sent","detail":"QChat XML tool sent a QQ message.","data":{"type":"Group","targetId":867165927,"message":"reply {{i}}"},"exception":null}""");
        }
        File.WriteAllLines(diagnosticsPath, lines);
        AgentControlCenterService service = new(qchatDiagnosticsPath: diagnosticsPath)
        {
            Configuration = new AgentControlCenterConfig
            {
                ProactiveChatIntensity = 5
            }
        };

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "Kira");

        AgentControlCenterSelfCheckItem item = snapshot.SelfCheck.Items
            .Single(item => item.Category == "qq-output-volume");
        Assert.That(snapshot.RuntimeVisibility.QChat.RecentOutboundMessageCount, Is.EqualTo(6));
        Assert.That(item.ActionId, Is.EqualTo("reduce-proactive-intensity"));
        Assert.That(item.CanAgentHandleAutonomously, Is.True);
        Assert.That(item.RiskLevel, Is.EqualTo(AgentAuditRiskLevel.Low));
    }

    [Test]
    public void AgentControlCenterTreatsQChatQuietSuppressionAsInformational()
    {
        string root = CreateTempWorkspace();
        string diagnosticsPath = Path.Combine(root, "qchat-diagnostics.jsonl");
        File.WriteAllLines(diagnosticsPath, [
            """{"timestamp":"2026-06-16T10:00:01+08:00","eventName":"qchat-quiet-message-suppressed","detail":"QQ inbound message suppressed because owner quiet mode is enabled.","data":{"MessageType":"Group","UserId":2001,"GroupId":867165927,"senderRole":1,"isMentionedOrWoken":false},"exception":null}"""
        ]);
        AgentControlCenterService service = new(qchatDiagnosticsPath: diagnosticsPath);

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "Kira");

        Assert.That(snapshot.RuntimeVisibility.QChat.RecentQuietSuppressionCount, Is.EqualTo(1));
        Assert.That(snapshot.RuntimeVisibility.QChat.RecentFailureCount, Is.Zero);
        Assert.That(snapshot.SelfCheck.Items.Select(item => item.Category), Does.Not.Contain("qq-runtime-failure"));
        Assert.That(snapshot.SelfCheck.Items.Select(item => item.Category), Does.Not.Contain("qq-output-volume"));
    }

    [Test]
    public void AgentControlCenterBuildsQChatAntiSpamVisibility()
    {
        string root = CreateTempWorkspace();
        string diagnosticsPath = Path.Combine(root, "qchat-diagnostics.jsonl");
        File.WriteAllLines(diagnosticsPath, [
            """{"timestamp":"2026-06-16T10:00:00+08:00","eventName":"start","detail":"QChat service starting.","data":{"BotId":3340947887},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:01+08:00","eventName":"message-dispatching","detail":"Dispatching message event to QChat.","data":{"MessageType":"Group","UserId":2001,"GroupId":867165927,"isMentionedOrWoken":false},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:02+08:00","eventName":"group-buffered","detail":"Group message buffered for model dispatch.","data":{"GroupId":867165927,"IsEnabled":true,"bufferCount":1,"isAwakening":false,"senderRole":1},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:03+08:00","eventName":"group-passive-low-information-skipped","detail":"Passive group message skipped because it has too little conversational content.","data":{"GroupId":867165927,"UserId":2002,"senderRole":1,"isMentionedOrWoken":false,"RawMessage":"ok"},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:04+08:00","eventName":"group-passive-throttled","detail":"Passive group message skipped because the bot replied recently.","data":{"GroupId":867165927,"UserId":2003,"senderRole":1,"isMentionedOrWoken":false,"elapsedSeconds":12.5,"cooldownSeconds":90},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:05+08:00","eventName":"group-buffered-proactive","detail":"Group message buffered by proactive probability.","data":{"GroupId":867165927,"bufferCount":1,"ProactiveChatProbability":0.15,"EffectiveProactiveChatProbability":0.075},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:06+08:00","eventName":"qchat-quiet-mode-enabled","detail":"Owner enabled QQ quiet mode.","data":{"MessageType":"Group","UserId":3045846738,"GroupId":867165927,"reason":"owner-sleep-command"},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:07+08:00","eventName":"qchat-quiet-message-suppressed","detail":"QQ inbound message suppressed because owner quiet mode is enabled.","data":{"MessageType":"Group","UserId":2004,"GroupId":867165927,"senderRole":1,"isMentionedOrWoken":false},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:08+08:00","eventName":"group-passive-media-chance-allowed","detail":"Passive media-only group message allowed by media reply chance.","data":{"GroupId":867165927,"UserId":2005,"senderRole":1,"isMentionedOrWoken":false,"MediaOnlyPassiveGroupReplyProbability":0.2,"RawMessage":"[CQ:image,file=sticker.jpg]"},"exception":null}"""
        ]);
        AgentControlCenterService service = new(qchatDiagnosticsPath: diagnosticsPath);

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "Kira");

        AgentQChatAntiSpamVisibility antiSpam = snapshot.RuntimeVisibility.QChat.AntiSpam;
        Assert.That(antiSpam.RecentGroupMessageCount, Is.EqualTo(1));
        Assert.That(antiSpam.RecentGroupBufferedCount, Is.EqualTo(2));
        Assert.That(antiSpam.RecentSuppressedCount, Is.EqualTo(3));
        Assert.That(antiSpam.RecentLowInformationSuppressionCount, Is.EqualTo(1));
        Assert.That(antiSpam.RecentCooldownSuppressionCount, Is.EqualTo(1));
        Assert.That(antiSpam.RecentQuietSuppressionCount, Is.EqualTo(1));
        Assert.That(antiSpam.RecentMediaChanceAllowedCount, Is.EqualTo(1));
        Assert.That(antiSpam.PassiveCooldownSeconds, Is.EqualTo(90));
        Assert.That(antiSpam.LastPassiveElapsedSeconds, Is.EqualTo(12.5).Within(0.01));
        Assert.That(antiSpam.ObservedProactiveProbability, Is.EqualTo(0.075).Within(0.001));
        Assert.That(antiSpam.ObservedMediaOnlyReplyProbability, Is.EqualTo(0.2).Within(0.001));
        Assert.That(antiSpam.QuietModeEnabled, Is.True);
        Assert.That(antiSpam.QuietModeReason, Is.EqualTo("owner-sleep-command"));
        Assert.That(antiSpam.LastSuppressionReason, Is.EqualTo("quiet-mode"));
        Assert.That(antiSpam.RecentSuppressionReasons, Does.Contain("low-information"));
        Assert.That(antiSpam.RecentSuppressionReasons, Does.Contain("cooldown"));
        Assert.That(antiSpam.RecentSuppressionReasons, Does.Contain("quiet-mode"));
    }

    [Test]
    public void AgentControlCenterBuildsQChatGroupScopeVisibility()
    {
        string root = CreateTempWorkspace();
        string diagnosticsPath = Path.Combine(root, "qchat-diagnostics.jsonl");
        File.WriteAllLines(diagnosticsPath, [
            """{"timestamp":"2026-06-16T10:00:00+08:00","eventName":"start","detail":"QChat service starting.","data":{"BotId":3340947887,"AllowedGroupIds":"867165927"},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:01+08:00","eventName":"group-passive-scope-skipped","detail":"Passive group message skipped because the group is outside the QQ allowlist.","data":{"GroupId":768420784,"UserId":2001,"AllowedGroupIds":"867165927","RawMessage":"outside scope"},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:02+08:00","eventName":"group-decision","detail":"Recorded group reply decision.","data":{"GroupId":768420784,"UserId":2001,"Decision":"suppressed","Reason":"scope","SenderRole":"Member","IsMentionedOrWoken":false,"IsGroupEnabled":false,"SocialAttentionProbability":0,"CooldownRemainingSeconds":0,"ActiveSoftAttentionRemainingSeconds":0,"RawMessage":"outside scope"},"exception":null}"""
        ]);
        AgentControlCenterService service = new(qchatDiagnosticsPath: diagnosticsPath);

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "Kira");

        AgentQChatAntiSpamVisibility antiSpam = snapshot.RuntimeVisibility.QChat.AntiSpam;
        Assert.That(antiSpam.AllowedGroupIds, Is.EqualTo("867165927"));
        Assert.That(antiSpam.RecentScopeSuppressionCount, Is.EqualTo(1));
        Assert.That(antiSpam.LastSuppressionReason, Is.EqualTo("scope"));
        Assert.That(antiSpam.RecentSuppressionReasons, Does.Contain("scope"));
        Assert.That(antiSpam.RecentGroupDecisions[0].Reason, Is.EqualTo("scope"));
    }

    [Test]
    public void AgentControlCenterAppliesQChatPolicyToCharacterConfiguration()
    {
        string root = CreateTempWorkspace();
        string previousStorage = AlifePath.StorageFolderPath;
        try
        {
            AlifePath.SetStorageFolderPath(root, persist: false);
            ConfigurationSystem configurationSystem = new(new StorageSystem());
            AgentControlCenterService service = new(configurationSystem: configurationSystem);
            const string characterRoot = "Character\\真央";
            configurationSystem.SetConfiguration(
                typeof(QChatService),
                new QChatConfig
                {
                    BotId = 3340947887,
                    OwnerId = 3045846738,
                    Token = "do-not-touch",
                    AllowedGroupIds = "",
                    ProactiveChatProbability = 0.5f,
                    PassiveGroupReplyCooldownSeconds = 30,
                    MediaOnlyPassiveGroupReplyProbability = 0.5f
                },
                characterRoot);

            AgentQChatPolicyChangeResult result = service.ApplyQChatPolicyFromControlCenter(
                characterRoot,
                allowedGroupIds: "867165927, 867165927, abc, 1072509877",
                mode: "balanced",
                passiveCooldownSeconds: 120,
                mediaOnlyReplyProbability: 0.12f,
                actor: "agent-control-ui");

            Assert.That(result.Applied, Is.True);
            Assert.That(result.Message, Does.Contain("QChat policy applied"));
            QChatConfig updated =
                (QChatConfig)configurationSystem.GetConfiguration(typeof(QChatService), characterRoot)!;
            Assert.That(updated.Token, Is.EqualTo("do-not-touch"));
            Assert.That(updated.OwnerId, Is.EqualTo(3045846738));
            Assert.That(updated.AllowedGroupIds, Is.EqualTo("867165927,1072509877"));
            Assert.That(updated.AllowMentionOutsideAllowedGroups, Is.True);
            Assert.That(updated.AllowGroupMemberChat, Is.True);
            Assert.That(updated.AllowGroupMemberMentions, Is.True);
            Assert.That(updated.AllowProactiveGroupChat, Is.True);
            Assert.That(updated.ProactiveChatProbability, Is.EqualTo(0.15f).Within(0.001f));
            Assert.That(updated.PassiveGroupReplyCooldownSeconds, Is.EqualTo(120));
            Assert.That(updated.MediaOnlyPassiveGroupReplyProbability, Is.EqualTo(0.12f).Within(0.001f));
        }
        finally
        {
            AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [TestCase("silent", false, true, false, 0f)]
    [TestCase("mention-only", true, true, false, 0f)]
    [TestCase("balanced", true, true, true, 0.15f)]
    [TestCase("active", true, true, true, 0.3f)]
    public void AgentControlCenterQChatPolicyModesMapToConservativeValues(
        string mode,
        bool allowGroupMemberChat,
        bool allowGroupMemberMentions,
        bool allowProactiveGroupChat,
        float proactiveProbability)
    {
        string root = CreateTempWorkspace();
        string previousStorage = AlifePath.StorageFolderPath;
        try
        {
            AlifePath.SetStorageFolderPath(root, persist: false);
            ConfigurationSystem configurationSystem = new(new StorageSystem());
            AgentControlCenterService service = new(configurationSystem: configurationSystem);
            const string characterRoot = "Character\\真央";
            configurationSystem.SetConfiguration(typeof(QChatService), new QChatConfig(), characterRoot);

            AgentQChatPolicyChangeResult result = service.ApplyQChatPolicyFromControlCenter(
                characterRoot,
                allowedGroupIds: "867165927",
                mode: mode,
                passiveCooldownSeconds: 999,
                mediaOnlyReplyProbability: 9f,
                actor: "agent-control-ui");

            Assert.That(result.Applied, Is.True);
            QChatConfig updated =
                (QChatConfig)configurationSystem.GetConfiguration(typeof(QChatService), characterRoot)!;
            Assert.That(updated.AllowGroupMemberChat, Is.EqualTo(allowGroupMemberChat));
            Assert.That(updated.AllowGroupMemberMentions, Is.EqualTo(allowGroupMemberMentions));
            Assert.That(updated.AllowProactiveGroupChat, Is.EqualTo(allowProactiveGroupChat));
            Assert.That(updated.ProactiveChatProbability, Is.EqualTo(proactiveProbability).Within(0.001f));
            Assert.That(updated.PassiveGroupReplyCooldownSeconds, Is.EqualTo(600));
            Assert.That(updated.MediaOnlyPassiveGroupReplyProbability, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(result.Snapshot?.Mode, Is.EqualTo(mode));
        }
        finally
        {
            AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public void AgentControlCenterQChatPolicyRejectsUnknownMode()
    {
        string root = CreateTempWorkspace();
        string previousStorage = AlifePath.StorageFolderPath;
        try
        {
            AlifePath.SetStorageFolderPath(root, persist: false);
            ConfigurationSystem configurationSystem = new(new StorageSystem());
            AgentControlCenterService service = new(configurationSystem: configurationSystem);
            const string characterRoot = "Character\\真央";
            configurationSystem.SetConfiguration(
                typeof(QChatService),
                new QChatConfig { ProactiveChatProbability = 0.22f },
                characterRoot);

            AgentQChatPolicyChangeResult result = service.ApplyQChatPolicyFromControlCenter(
                characterRoot,
                allowedGroupIds: "867165927",
                mode: "loud",
                passiveCooldownSeconds: 120,
                mediaOnlyReplyProbability: 0.1f,
                actor: "agent-control-ui");

            Assert.That(result.Applied, Is.False);
            Assert.That(result.Message, Does.Contain("Unknown QChat policy mode"));
            QChatConfig updated =
                (QChatConfig)configurationSystem.GetConfiguration(typeof(QChatService), characterRoot)!;
            Assert.That(updated.ProactiveChatProbability, Is.EqualTo(0.22f).Within(0.001f));
        }
        finally
        {
            AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public void AgentControlCenterQChatPolicyFailsWhenConfigurationSystemIsUnavailable()
    {
        AgentControlCenterService service = new();

        AgentQChatPolicyChangeResult result = service.ApplyQChatPolicyFromControlCenter(
            "Character\\真央",
            allowedGroupIds: "867165927",
            mode: "balanced",
            passiveCooldownSeconds: 120,
            mediaOnlyReplyProbability: 0.1f,
            actor: "agent-control-ui");

        Assert.That(result.Applied, Is.False);
        Assert.That(result.Message, Does.Contain("Configuration system is unavailable"));
    }

    [Test]
    public void AgentControlCenterQChatPolicyCanDisallowMentionOutsideAllowedGroups()
    {
        string root = CreateTempWorkspace();
        string previousStorage = AlifePath.StorageFolderPath;
        try
        {
            AlifePath.SetStorageFolderPath(root, persist: false);
            ConfigurationSystem configurationSystem = new(new StorageSystem());
            AgentControlCenterService service = new(configurationSystem: configurationSystem);
            const string characterRoot = "Character\\真央";
            configurationSystem.SetConfiguration(
                typeof(QChatService),
                new QChatConfig { AllowMentionOutsideAllowedGroups = true },
                characterRoot);

            AgentQChatPolicyChangeResult result = service.ApplyQChatPolicyFromControlCenter(
                characterRoot,
                allowedGroupIds: "867165927",
                mode: "balanced",
                passiveCooldownSeconds: 120,
                mediaOnlyReplyProbability: 0.12f,
                actor: "agent-control-ui",
                allowMentionOutsideAllowedGroups: false);

            Assert.That(result.Applied, Is.True);
            Assert.That(result.Snapshot?.AllowMentionOutsideAllowedGroups, Is.False);
            QChatConfig updated =
                (QChatConfig)configurationSystem.GetConfiguration(typeof(QChatService), characterRoot)!;
            Assert.That(updated.AllowMentionOutsideAllowedGroups, Is.False);
        }
        finally
        {
            AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public void AgentControlCenterQChatPolicySnapshotReadsCharacterConfiguration()
    {
        string root = CreateTempWorkspace();
        string previousStorage = AlifePath.StorageFolderPath;
        try
        {
            AlifePath.SetStorageFolderPath(root, persist: false);
            ConfigurationSystem configurationSystem = new(new StorageSystem());
            AgentControlCenterService service = new(configurationSystem: configurationSystem);
            const string characterRoot = "Character\\真央";
            configurationSystem.SetConfiguration(
                typeof(QChatService),
                new QChatConfig
                {
                    AllowedGroupIds = "867165927",
                    AllowGroupMemberChat = true,
                    AllowGroupMemberMentions = true,
                    AllowMentionOutsideAllowedGroups = false,
                    AllowProactiveGroupChat = true,
                    ProactiveChatProbability = 0.15f,
                    PassiveGroupReplyCooldownSeconds = 120,
                    MediaOnlyPassiveGroupReplyProbability = 0.12f
                },
                characterRoot);

            AgentQChatPolicySnapshot? snapshot =
                service.GetQChatPolicySnapshotFromControlCenter(characterRoot);

            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot!.AllowedGroupIds, Is.EqualTo("867165927"));
            Assert.That(snapshot.Mode, Is.EqualTo("balanced"));
            Assert.That(snapshot.AllowMentionOutsideAllowedGroups, Is.False);
            Assert.That(snapshot.PassiveCooldownSeconds, Is.EqualTo(120));
            Assert.That(snapshot.MediaOnlyReplyProbability, Is.EqualTo(0.12f).Within(0.001f));
        }
        finally
        {
            AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public void AgentControlCenterShowsJoinedQChatGroupsWithAllowedScopeState()
    {
        string root = CreateTempWorkspace();
        string previousStorage = AlifePath.StorageFolderPath;
        try
        {
            AlifePath.SetStorageFolderPath(root, persist: false);
            ConfigurationSystem configurationSystem = new(new StorageSystem());
            FakeJoinedQChatGroupProvider groupProvider = new();
            groupProvider.CachedSnapshot = new AgentQChatJoinedGroupSourceSnapshot(
                DateTimeOffset.Parse("2026-06-17T12:00:00+08:00"),
                [
                    new AgentQChatJoinedGroupSourceItem(867165927, "test group", 3, 200),
                    new AgentQChatJoinedGroupSourceItem(1072509877, "music group", 106, 200)
                ]);
            AgentControlCenterService service = new(configurationSystem: configurationSystem)
            {
                QChatJoinedGroupProviderOverride = groupProvider
            };
            const string characterRoot = "Character\\鐪熷ぎ";
            configurationSystem.SetConfiguration(
                typeof(QChatService),
                new QChatConfig { AllowedGroupIds = "867165927" },
                characterRoot);

            AgentQChatJoinedGroupSnapshot snapshot =
                service.GetJoinedQChatGroupsFromControlCenter(characterRoot);

            Assert.That(snapshot.Available, Is.True);
            Assert.That(snapshot.Groups, Has.Count.EqualTo(2));
            Assert.That(snapshot.Groups.Single(group => group.GroupId == 867165927).IsAllowed, Is.True);
            Assert.That(snapshot.Groups.Single(group => group.GroupId == 1072509877).IsAllowed, Is.False);
            Assert.That(snapshot.Groups[0].GroupName, Is.EqualTo("test group"));
            Assert.That(snapshot.Message, Does.Contain("cached"));
        }
        finally
        {
            AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public void AgentControlCenterAddsAndRemovesJoinedQChatGroupWithoutTouchingSensitiveFields()
    {
        string root = CreateTempWorkspace();
        string previousStorage = AlifePath.StorageFolderPath;
        try
        {
            AlifePath.SetStorageFolderPath(root, persist: false);
            ConfigurationSystem configurationSystem = new(new StorageSystem());
            AgentControlCenterService service = new(configurationSystem: configurationSystem);
            const string characterRoot = "Character\\鐪熷ぎ";
            configurationSystem.SetConfiguration(
                typeof(QChatService),
                new QChatConfig
                {
                    BotId = 3340947887,
                    OwnerId = 3045846738,
                    Token = "do-not-touch",
                    AllowedGroupIds = "867165927",
                    AllowGroupMemberChat = true,
                    AllowGroupMemberMentions = true,
                    AllowMentionOutsideAllowedGroups = false,
                    AllowProactiveGroupChat = true,
                    ProactiveChatProbability = 0.15f,
                    PassiveGroupReplyCooldownSeconds = 90,
                    MediaOnlyPassiveGroupReplyProbability = 0.15f
                },
                characterRoot);

            AgentQChatPolicyChangeResult addResult = service.AddAllowedQChatGroupFromControlCenter(
                characterRoot,
                1072509877,
                "agent-control-ui");
            AgentQChatPolicyChangeResult removeResult = service.RemoveAllowedQChatGroupFromControlCenter(
                characterRoot,
                867165927,
                "agent-control-ui");

            Assert.That(addResult.Applied, Is.True);
            Assert.That(removeResult.Applied, Is.True);
            QChatConfig updated =
                (QChatConfig)configurationSystem.GetConfiguration(typeof(QChatService), characterRoot)!;
            Assert.That(updated.Token, Is.EqualTo("do-not-touch"));
            Assert.That(updated.OwnerId, Is.EqualTo(3045846738));
            Assert.That(updated.AllowedGroupIds, Is.EqualTo("1072509877"));
            Assert.That(updated.AllowMentionOutsideAllowedGroups, Is.False);
            Assert.That(updated.ProactiveChatProbability, Is.EqualTo(0.15f).Within(0.001f));
            Assert.That(updated.PassiveGroupReplyCooldownSeconds, Is.EqualTo(90));
            Assert.That(updated.MediaOnlyPassiveGroupReplyProbability, Is.EqualTo(0.15f).Within(0.001f));
        }
        finally
        {
            AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public void AgentControlCenterBuildsQChatRecentGroupDecisionVisibility()
    {
        string root = CreateTempWorkspace();
        string diagnosticsPath = Path.Combine(root, "qchat-diagnostics.jsonl");
        File.WriteAllLines(diagnosticsPath, [
            """{"timestamp":"2026-06-16T10:00:00+08:00","eventName":"start","detail":"QChat service starting.","data":{"BotId":3340947887},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:01+08:00","eventName":"group-decision","detail":"Recorded group reply decision.","data":{"GroupId":867165927,"UserId":2001,"Decision":"accepted","Reason":"mention-or-wake","SenderRole":"Member","IsMentionedOrWoken":true,"IsGroupEnabled":true,"SocialAttentionProbability":1,"CooldownRemainingSeconds":0,"ActiveSoftAttentionRemainingSeconds":120,"RawMessage":"[CQ:at,qq=3340947887] 你在吗"},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:02+08:00","eventName":"group-decision","detail":"Recorded group reply decision.","data":{"GroupId":867165927,"UserId":2002,"Decision":"suppressed","Reason":"social-attention","SenderRole":"Member","IsMentionedOrWoken":false,"IsGroupEnabled":false,"SocialAttentionProbability":0.05,"CooldownRemainingSeconds":44,"ActiveSoftAttentionRemainingSeconds":0,"RawMessage":"路过说一句"},"exception":null}"""
        ]);
        AgentControlCenterService service = new(qchatDiagnosticsPath: diagnosticsPath);

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "Kira");

        AgentQChatAntiSpamVisibility antiSpam = snapshot.RuntimeVisibility.QChat.AntiSpam;
        Assert.That(antiSpam.RecentGroupDecisions, Has.Count.EqualTo(2));
        Assert.That(antiSpam.RecentGroupDecisions[0].Decision, Is.EqualTo("suppressed"));
        Assert.That(antiSpam.RecentGroupDecisions[0].Reason, Is.EqualTo("social-attention"));
        Assert.That(antiSpam.RecentGroupDecisions[0].GroupId, Is.EqualTo(867165927));
        Assert.That(antiSpam.RecentGroupDecisions[0].UserId, Is.EqualTo(2002));
        Assert.That(antiSpam.RecentGroupDecisions[0].IsMentionedOrWoken, Is.False);
        Assert.That(antiSpam.RecentGroupDecisions[0].IsGroupEnabled, Is.False);
        Assert.That(antiSpam.RecentGroupDecisions[0].SocialAttentionProbability, Is.EqualTo(0.05).Within(0.001));
        Assert.That(antiSpam.RecentGroupDecisions[0].CooldownRemainingSeconds, Is.EqualTo(44));
        Assert.That(antiSpam.RecentGroupDecisions[0].ActiveSoftAttentionRemainingSeconds, Is.Zero);
        Assert.That(antiSpam.RecentGroupDecisions[0].RawMessage, Is.EqualTo("路过说一句"));
        Assert.That(antiSpam.RecentGroupDecisions[1].Decision, Is.EqualTo("accepted"));
        Assert.That(antiSpam.RecentGroupDecisions[1].Reason, Is.EqualTo("mention-or-wake"));
        Assert.That(antiSpam.RecentGroupDecisions[1].ActiveSoftAttentionRemainingSeconds, Is.EqualTo(120));
    }

    [Test]
    public void AgentControlCenterFormatsQChatRecentDecisionSummaryForAgent()
    {
        AgentQChatGroupDecisionVisibility accepted = new(
            DateTimeOffset.Parse("2026-06-16T10:00:01+08:00"),
            867165927,
            2001,
            "accepted",
            "mention-or-wake",
            true,
            true,
            1,
            0,
            120,
            "[CQ:at,qq=3340947887] 你在吗");
        AgentQChatGroupDecisionVisibility suppressed = new(
            DateTimeOffset.Parse("2026-06-16T10:00:02+08:00"),
            867165927,
            2002,
            "suppressed",
            "social-attention",
            false,
            false,
            0.05,
            44,
            0,
            "路过说一句");

        string summary = AgentControlCenterService.FormatQChatRecentDecisionSummaryForAgent([suppressed, accepted]);

        Assert.That(summary, Does.Contain("Internal QQ group decision diagnostic"));
        Assert.That(summary, Does.Contain("group=867165927"));
        Assert.That(summary, Does.Contain("user=2002"));
        Assert.That(summary, Does.Contain("decision=suppressed"));
        Assert.That(summary, Does.Contain("reason=social-attention"));
        Assert.That(summary, Does.Contain("cooldown=44s"));
        Assert.That(summary, Does.Contain("activeWindow=0s"));
        Assert.That(summary, Does.Contain("probability=5%"));
        Assert.That(summary, Does.Contain("user=2001"));
        Assert.That(summary, Does.Contain("decision=accepted"));
        Assert.That(summary, Does.Contain("reason=mention-or-wake"));
        Assert.That(summary, Does.Contain("wake=True"));
        Assert.That(summary, Does.Contain("active=True"));
        Assert.That(summary, Does.Contain("Keep this out of user-facing chat"));
        Assert.That(summary, Does.Not.Contain("<qchat"));
    }

    [Test]
    public void AgentControlCenterScopesQChatVisibilityToLatestRuntimeStart()
    {
        string root = CreateTempWorkspace();
        string diagnosticsPath = Path.Combine(root, "qchat-diagnostics.jsonl");
        File.WriteAllLines(diagnosticsPath, [
            """{"timestamp":"2026-06-16T09:59:58+08:00","eventName":"start","detail":"QChat service starting.","data":{"BotId":999},"exception":null}""",
            """{"timestamp":"2026-06-16T09:59:59+08:00","eventName":"qchat-send-failed","detail":"old test failure","data":{"type":"Group","targetId":123},"exception":"System.InvalidOperationException: old test failure"}""",
            """{"timestamp":"2026-06-16T10:00:00+08:00","eventName":"start","detail":"QChat service starting.","data":{"BotId":3340947887},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:01+08:00","eventName":"connect-succeeded","detail":"OneBot connected.","data":{"BotId":3340947887,"IsConnected":true},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:02+08:00","eventName":"message-dispatching","detail":"Dispatching message event to QChat.","data":{"MessageType":"Group","UserId":3045846738,"GroupId":867165927},"exception":null}"""
        ]);
        AgentControlCenterService service = new(qchatDiagnosticsPath: diagnosticsPath);

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "Kira");

        Assert.That(snapshot.RuntimeVisibility.QChat.RecentConnectSucceededCount, Is.EqualTo(1));
        Assert.That(snapshot.RuntimeVisibility.QChat.RecentInboundMessageCount, Is.EqualTo(1));
        Assert.That(snapshot.RuntimeVisibility.QChat.RecentFailureCount, Is.Zero);
        Assert.That(snapshot.SelfCheck.Items.Select(item => item.Category), Does.Not.Contain("qq-runtime-failure"));
    }

    [Test]
    public void AgentControlCenterBuildsAgentReadableSelfCheckRecommendations()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        audit.Record("qzone.like", "group:20002", "target=1001 post=post-a", AgentAuditRiskLevel.High, false, "Blocked: owner authority required");
        AgentControlCenterService service = new(auditLog: audit)
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowAutomaticMaintenanceInspection = true,
                MaintenanceInspectionIntervalMinutes = 1
            }
        };
        service.ProposeConfigurationChange("OwnerUserIds", "10001", "agent", "owner identity is protected");
        service.RecordBackgroundTaskResult(AgentBackgroundTaskResult.Failed(
            "task-1",
            "browser-scan",
            "qq:group:1000",
            "Browser page timed out",
            DateTimeOffset.Parse("2026-06-15T12:00:00Z")));
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 3,
            LastError: "Browser runtime failed",
            RecentEvents: [new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-15T12:00:01Z"), "Error", "Browser runtime failed")])
        {
            Latency = new ChatLatencySnapshot(
                DateTimeOffset.Parse("2026-06-15T12:00:00Z"),
                DateTimeOffset.Parse("2026-06-15T12:00:04.250Z"),
                DateTimeOffset.Parse("2026-06-15T12:00:06Z"),
                TimeSpan.FromMilliseconds(4250),
                TimeSpan.FromSeconds(6))
        };

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(runtime, "Kira");
        string report = AgentControlCenterService.FormatSelfCheckForAgent(snapshot.SelfCheck);

        Assert.That(snapshot.SelfCheck.Items.Select(item => item.Category), Does.Contain("owner-confirmation"));
        Assert.That(snapshot.SelfCheck.Items.Select(item => item.Category), Does.Contain("runtime-error"));
        Assert.That(snapshot.SelfCheck.Items.Select(item => item.Category), Does.Contain("background-task"));
        Assert.That(snapshot.SelfCheck.Items.Select(item => item.Category), Does.Contain("streaming-latency"));
        Assert.That(snapshot.SelfCheck.Items.Select(item => item.Category), Does.Contain("security-gateway"));
        Assert.That(snapshot.SelfCheck.OwnerReviewCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(snapshot.SelfCheck.AutonomousRecommendationCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(snapshot.SelfCheck.Items
            .Where(item => item.Category == "owner-confirmation")
            .All(item => item.CanAgentHandleAutonomously == false), Is.True);
        Assert.That(snapshot.SelfCheck.Items.Single(item => item.Category == "runtime-error").CanAgentHandleAutonomously, Is.True);
        Assert.That(report, Does.Contain("Agent self-check"));
        Assert.That(report, Does.Contain("OwnerUserIds"));
        Assert.That(report, Does.Contain("Browser runtime failed"));
        Assert.That(report, Does.Contain("Browser page timed out"));
    }

    [Test]
    public void AgentControlCenterAppliesAllowlistedLowRiskSelfCheckActionAndAudits()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService service = new(auditLog: audit)
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowAutomaticMaintenanceInspection = false
            }
        };
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 3,
            LastError: "Browser runtime failed",
            RecentEvents: [new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-15T12:00:01Z"), "Error", "Browser runtime failed")]);
        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(runtime, "Kira");
        string actionId = snapshot.SelfCheck.Items.Single(item => item.Category == "maintenance-disabled").ActionId!;

        AgentControlCenterSelfCheckActionResult result = service.ApplySelfCheckAction(actionId, runtime, "agent");

        Assert.That(result.Applied, Is.True);
        Assert.That(result.RequiresOwnerConfirmation, Is.False);
        Assert.That(result.Key, Is.EqualTo("AllowAutomaticMaintenanceInspection"));
        Assert.That(service.Configuration!.AllowAutomaticMaintenanceInspection, Is.True);
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.config.applied"));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.self_check.action"));
    }

    [Test]
    public void AgentControlCenterExposesMemoryConsistencyIssuesToSelfCheck()
    {
        FakeMemoryConsistencyReporter memoryConsistency = new(new MemoryConsistencySnapshot(
            MissingArchiveFiles: 1,
            MissingIndexRecords: 2,
            ContentMismatches: 3,
            RepairedArchiveFiles: 0,
            RepairedIndexRecords: 0,
            RepairedContentMismatches: 0));
        AgentControlCenterService service = new(memoryConsistencyReporter: memoryConsistency);
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 3,
            LastError: null,
            RecentEvents: []);

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(runtime, "Kira");
        AgentControlCenterSelfCheckItem item = snapshot.SelfCheck.Items.Single(item => item.Category == "memory-consistency");
        string report = AgentControlCenterService.FormatSelfCheckForAgent(snapshot.SelfCheck);

        Assert.That(snapshot.MemoryConsistency.TotalIssues, Is.EqualTo(6));
        Assert.That(item.ActionId, Is.EqualTo("repair-memory-storage-consistency"));
        Assert.That(item.CanAgentHandleAutonomously, Is.True);
        Assert.That(item.Summary, Does.Contain("missing archives=1"));
        Assert.That(item.Summary, Does.Contain("missing indexes=2"));
        Assert.That(item.Summary, Does.Contain("content mismatches=3"));
        Assert.That(report, Does.Contain("memory-consistency"));
        Assert.That(report, Does.Contain("repair-memory-storage-consistency"));
    }

    [Test]
    public void AgentControlCenterRepairsMemoryConsistencyFromSelfCheckAndAudits()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        FakeMemoryConsistencyReporter memoryConsistency = new(new MemoryConsistencySnapshot(
            MissingArchiveFiles: 1,
            MissingIndexRecords: 0,
            ContentMismatches: 1,
            RepairedArchiveFiles: 0,
            RepairedIndexRecords: 0,
            RepairedContentMismatches: 0))
        {
            RepairResult = new MemoryConsistencySnapshot(
                MissingArchiveFiles: 1,
                MissingIndexRecords: 0,
                ContentMismatches: 1,
                RepairedArchiveFiles: 1,
                RepairedIndexRecords: 0,
                RepairedContentMismatches: 1)
        };
        AgentControlCenterService service = new(
            auditLog: audit,
            memoryConsistencyReporter: memoryConsistency);
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 3,
            LastError: null,
            RecentEvents: []);

        AgentControlCenterSelfCheckActionResult result = service.ApplySelfCheckAction(
            "repair-memory-storage-consistency",
            runtime,
            "agent");

        Assert.That(result.Applied, Is.True);
        Assert.That(result.RequiresOwnerConfirmation, Is.False);
        Assert.That(result.Key, Is.EqualTo("MemoryStorageConsistency"));
        Assert.That(result.Message, Does.Contain("repaired_archives=1"));
        Assert.That(result.Message, Does.Contain("repaired_content_mismatches=1"));
        Assert.That(memoryConsistency.RepairCallCount, Is.EqualTo(1));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.memory.consistency.repair"));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.self_check.action"));
    }

    [Test]
    public void AgentControlCenterSelfCheckActionKeepsProtectedConfigurationBehindOwnerConfirmation()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService service = new(auditLog: audit);
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 0,
            LastError: null,
            RecentEvents: []);

        AgentControlCenterSelfCheckActionResult result = service.ApplySelfCheckAction(
            "set-owner-user-ids",
            runtime,
            "agent");

        Assert.That(result.Applied, Is.False);
        Assert.That(result.RequiresOwnerConfirmation, Is.True);
        Assert.That(result.Key, Is.EqualTo("OwnerUserIds"));
        Assert.That(service.GetPendingConfigurationProposals().Select(item => item.Key), Does.Contain("OwnerUserIds"));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.config.proposed"));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.self_check.action"));
    }

    [Test]
    public void AgentControlCenterAutomaticSelfCheckSkipsWhileChatting()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T12:00:00Z");
        AgentControlCenterService service = new(clock: () => now)
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowAutomaticMaintenanceInspection = true,
                MaintenanceInspectionIntervalMinutes = 1
            }
        };
        ChatRuntimeState runtime = new(
            IsChatting: true,
            PendingPokeCount: 0,
            ChatHistoryCount: 3,
            LastError: "Browser runtime failed",
            RecentEvents: [new ChatRuntimeEvent(now, "Error", "Browser runtime failed")]);

        AgentControlCenterSelfCheckLoopResult? result = service.TryAutomaticSelfCheck(
            runtime,
            "Kira",
            "qq:group:1000");

        Assert.That(result, Is.Null);
        Assert.That(service.BuildSnapshot(runtime with { IsChatting = false }, "Kira").RuntimeVisibility.BackgroundTasks, Is.Empty);
    }

    [Test]
    public void AgentControlCenterAutomaticSelfCheckWakesOnlyForMeaningfulChangesAfterIntervalAndCooldown()
    {
        string root = CreateTempWorkspace();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T12:00:00Z");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentTaskService tasks = new(audit, taskStorePath: Path.Combine(root, "tasks.json"));
        AgentMaintenanceService maintenance = new(
            auditLog: audit,
            proposalStorePath: Path.Combine(root, "maintenance.json"),
            clock: () => now);
        AgentWorkspaceService workspace = new(new AgentWorkspacePolicy([root]), auditLog: audit);
        AgentControlCenterService service = new(
            tasks: tasks,
            workspace: workspace,
            auditLog: audit,
            maintenance: maintenance,
            qchatDiagnosticsPath: Path.Combine(root, "qchat-diagnostics.jsonl"),
            clock: () => now)
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowAutomaticMaintenanceInspection = true,
                MaintenanceInspectionIntervalMinutes = 1,
                MaintenanceDuplicateCooldownMinutes = 120
            }
        };
        service.ProposeConfigurationChange("OwnerUserIds", "10001", "agent", "owner identity is protected");
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 3,
            LastError: null,
            RecentEvents: []);

        AgentControlCenterSelfCheckLoopResult? first = service.TryAutomaticSelfCheck(runtime, "Kira", "qq:private:10001");
        AgentControlCenterSelfCheckLoopResult? immediate = service.TryAutomaticSelfCheck(runtime, "Kira", "qq:private:10001");
        now = now.AddMinutes(2);
        AgentControlCenterSelfCheckLoopResult? duplicate = service.TryAutomaticSelfCheck(runtime, "Kira", "qq:private:10001");
        now = now.AddMinutes(121);
        AgentControlCenterSelfCheckLoopResult? afterCooldown = service.TryAutomaticSelfCheck(runtime, "Kira", "qq:private:10001");
        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(runtime, "Kira");

        Assert.That(first, Is.Not.Null);
        Assert.That(first!.WakeRecommended, Is.True);
        Assert.That(first.WakeEvent, Is.Not.Null);
        Assert.That(first.WakeEvent!.Type, Is.EqualTo("agent.background.completed"));
        Assert.That(first.BackgroundResult.TaskName, Is.EqualTo("agent-self-check"));
        Assert.That(first.BackgroundResult.ResultText, Does.Contain("OwnerUserIds"));
        Assert.That(immediate, Is.Null);
        Assert.That(duplicate, Is.Not.Null);
        Assert.That(duplicate!.WakeRecommended, Is.False);
        Assert.That(duplicate.WakeEvent, Is.Null);
        Assert.That(afterCooldown, Is.Not.Null);
        Assert.That(afterCooldown!.WakeRecommended, Is.True);
        Assert.That(snapshot.RuntimeVisibility.BackgroundTasks.Count(task => task.TaskName == "agent-self-check"), Is.EqualTo(3));
    }

    [Test]
    public void AgentControlCenterAutomaticSelfCheckRunsMaintenanceInspectionWithoutDuplicateNoise()
    {
        string root = CreateTempWorkspace();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T12:00:00Z");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentMaintenanceService maintenance = new(
            auditLog: audit,
            proposalStorePath: Path.Combine(root, "maintenance.json"),
            clock: () => now);
        AgentTaskService tasks = new(audit, taskStorePath: Path.Combine(root, "tasks.json"));
        AgentControlCenterService service = new(
            auditLog: audit,
            tasks: tasks,
            maintenance: maintenance,
            clock: () => now)
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowAutomaticMaintenanceInspection = true,
                MaintenanceInspectionIntervalMinutes = 1,
                MaintenanceDuplicateCooldownMinutes = 120
            }
        };
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 3,
            LastError: "Browser runtime failed",
            RecentEvents: [new ChatRuntimeEvent(now.AddSeconds(-10), "Error", "Browser runtime failed")]);

        AgentControlCenterSelfCheckLoopResult? first = service.TryAutomaticSelfCheck(runtime, "Kira", "qq:group:1000");
        now = now.AddMinutes(2);
        AgentControlCenterSelfCheckLoopResult? duplicate = service.TryAutomaticSelfCheck(runtime, "Kira", "qq:group:1000");
        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(runtime, "Kira");

        Assert.That(first, Is.Not.Null);
        Assert.That(first!.MaintenanceInspection?.Created, Is.True);
        Assert.That(first.WakeRecommended, Is.True);
        Assert.That(snapshot.PendingMaintenanceProposals, Has.Count.EqualTo(1));
        Assert.That(snapshot.ActiveTasks, Has.Count.EqualTo(1));
        Assert.That(duplicate, Is.Not.Null);
        Assert.That(duplicate!.MaintenanceInspection?.Created, Is.False);
        Assert.That(duplicate.WakeRecommended, Is.False);
        Assert.That(service.BuildSnapshot(runtime, "Kira").PendingMaintenanceProposals, Has.Count.EqualTo(1));
    }

    [Test]
    public void AgentControlCenterAutomaticSelfCheckReducesQChatOutputVolume()
    {
        string root = CreateTempWorkspace();
        string diagnosticsPath = Path.Combine(root, "qchat-diagnostics.jsonl");
        List<string> lines = [
            """{"timestamp":"2026-06-16T10:00:00+08:00","eventName":"start","detail":"QChat service starting.","data":{"BotId":3340947887},"exception":null}""",
            """{"timestamp":"2026-06-16T10:00:01+08:00","eventName":"connect-succeeded","detail":"OneBot connected.","data":{"BotId":3340947887,"IsConnected":true},"exception":null}"""
        ];
        for (int i = 0; i < 6; i++)
        {
            lines.Add($$"""{"timestamp":"2026-06-16T10:00:0{{i + 2}}+08:00","eventName":"qchat-sent","detail":"QChat XML tool sent a QQ message.","data":{"type":"Group","targetId":867165927,"message":"reply {{i}}"},"exception":null}""");
        }
        File.WriteAllLines(diagnosticsPath, lines);
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-16T10:01:00+08:00");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService service = new(
            auditLog: audit,
            qchatDiagnosticsPath: diagnosticsPath,
            clock: () => now)
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowAutomaticMaintenanceInspection = true,
                MaintenanceInspectionIntervalMinutes = 1,
                ProactiveChatIntensity = 5
            }
        };
        ChatRuntimeState runtime = new(false, 0, 0, null, []);

        AgentControlCenterSelfCheckLoopResult? result = service.TryAutomaticSelfCheck(
            runtime,
            "Kira",
            "qq:group:867165927");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.SelfCheck.Items.Select(item => item.Category), Does.Contain("qq-output-volume"));
        Assert.That(service.Configuration!.ProactiveChatIntensity, Is.EqualTo(4));
        Assert.That(audit.GetRecentEntries(20).Select(entry => entry.Action), Does.Contain("agent.self_check.action"));
        Assert.That(audit.GetRecentEntries(20).Select(entry => entry.Detail), Has.Some.Contains("reduce-proactive-intensity"));
    }

    [Test]
    public void AgentControlCenterReportsSelfCheckSchedulerStatus()
    {
        string root = CreateTempWorkspace();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T12:00:00Z");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService service = new(auditLog: audit, clock: () => now)
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowAutomaticMaintenanceInspection = true,
                MaintenanceInspectionIntervalMinutes = 5,
                MaintenanceDuplicateCooldownMinutes = 120
            }
        };
        ChatRuntimeState chatting = new(
            IsChatting: true,
            PendingPokeCount: 0,
            ChatHistoryCount: 3,
            LastError: null,
            RecentEvents: []);
        ChatRuntimeState idle = chatting with { IsChatting = false };

        AgentControlCenterSelfCheckLoopResult? skipped = service.TryAutomaticSelfCheck(chatting, "Kira", "qq:group:1000");
        AgentControlCenterSelfCheckLoopResult? first = service.TryAutomaticSelfCheck(idle, "Kira", "qq:group:1000");
        AgentControlCenterSelfCheckLoopResult? intervalSkipped = service.TryAutomaticSelfCheck(idle, "Kira", "qq:group:1000");
        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(idle, "Kira");

        Assert.That(skipped, Is.Null);
        Assert.That(first, Is.Not.Null);
        Assert.That(intervalSkipped, Is.Null);
        Assert.That(snapshot.SelfCheckScheduler.LastSkipReason, Is.EqualTo("interval"));
        Assert.That(snapshot.SelfCheckScheduler.LastCheckedAt, Is.EqualTo(now));
        Assert.That(snapshot.SelfCheckScheduler.NextCheckAt, Is.EqualTo(now.AddMinutes(5)));
        Assert.That(snapshot.SelfCheckScheduler.LastWakeAt, Is.EqualTo(now));
    }

    [Test]
    public void AgentControlCenterBuildsOwnerNotificationPlanForPrivateOwnerReminder()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService service = new(auditLog: audit);
        service.ProposeConfigurationChange("OwnerUserIds", "10001", "agent", "owner identity is protected");
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 0,
            LastError: null,
            RecentEvents: []);
        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(runtime, "Kira");

        AgentOwnerNotificationPlan plan = AgentControlCenterService.BuildOwnerNotificationPlan(
            snapshot,
            ownerPrivateSessionId: "qq:private:3045846738",
            sourceGroupSessionId: "qq:group:867165927");

        Assert.That(plan.ShouldNotifyOwner, Is.True);
        Assert.That(plan.TargetSessionId, Is.EqualTo("qq:private:3045846738"));
        Assert.That(plan.PrivateMessages, Has.Some.Contains("OwnerUserIds"));
        Assert.That(plan.PublicGroupSummary, Does.Not.Contain("OwnerUserIds"));
        Assert.That(plan.PublicGroupSummary, Does.Contain("owner attention"));
    }

    [Test]
    public void AgentControlCenterLinksAutomaticSelfCheckWakeToRunSession()
    {
        string root = CreateTempWorkspace();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T12:00:00Z");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService service = new(auditLog: audit, clock: () => now)
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowAutomaticMaintenanceInspection = true,
                MaintenanceInspectionIntervalMinutes = 1,
                MaintenanceDuplicateCooldownMinutes = 120
            }
        };
        service.ProposeConfigurationChange("OwnerUserIds", "10001", "agent", "owner identity is protected");
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 0,
            LastError: null,
            RecentEvents: []);

        AgentControlCenterSelfCheckLoopResult? result = service.TryAutomaticSelfCheck(
            runtime,
            "Kira",
            "qq:private:3045846738");
        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(runtime, "Kira");

        Assert.That(result?.WakeRecommended, Is.True);
        Assert.That(snapshot.RuntimeVisibility.RecentRunSessions, Has.Count.EqualTo(1));
        Assert.That(snapshot.RuntimeVisibility.RecentRunSessions[0].SourceEventType, Is.EqualTo("agent.background.completed"));
        Assert.That(snapshot.RuntimeVisibility.RecentRunSessions[0].SourceSessionId, Is.EqualTo("qq:private:3045846738"));
        Assert.That(snapshot.RuntimeVisibility.RecentRunSessions[0].ToolSteps.Select(step => step.ToolName), Does.Contain("agent-self-check"));
        Assert.That(snapshot.RuntimeVisibility.RecentRunSessions[0].ToolSteps.Select(step => step.ToolName), Does.Contain("owner-notification-plan"));
    }

    [Test]
    public void AgentControlCenterAppliesExpandedLowRiskSelfCheckActions()
    {
        string root = CreateTempWorkspace();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T12:00:00Z");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentProactiveBehaviorService proactive = new(
            auditLog: audit,
            clock: () => now,
            persistencePath: Path.Combine(root, "proactive.json"));
        proactive.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneReply,
            "old reply suggestion",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001));
        now = now.AddDays(2);
        AgentControlCenterService service = new(auditLog: audit, clock: () => now)
        {
            ProactiveBehavior = proactive,
            Configuration = new AgentControlCenterConfig
            {
                ProactiveChatIntensity = 5,
                MaintenanceDuplicateCooldownMinutes = 15
            }
        };
        ChatRuntimeState runtime = new(false, 0, 0, null, []);

        AgentControlCenterSelfCheckActionResult reduce = service.ApplySelfCheckAction(
            "reduce-proactive-intensity",
            runtime,
            "agent");
        AgentControlCenterSelfCheckActionResult cooldown = service.ApplySelfCheckAction(
            "extend-maintenance-cooldown",
            runtime,
            "agent");
        AgentControlCenterSelfCheckActionResult cleanup = service.ApplySelfCheckAction(
            "cleanup-proactive-suggestions",
            runtime,
            "agent");

        Assert.That(reduce.Applied, Is.True);
        Assert.That(service.Configuration!.ProactiveChatIntensity, Is.EqualTo(4));
        Assert.That(cooldown.Applied, Is.True);
        Assert.That(service.Configuration.MaintenanceDuplicateCooldownMinutes, Is.EqualTo(30));
        Assert.That(cleanup.Applied, Is.True);
        Assert.That(cleanup.Message, Does.Contain("expired_pending=1"));
        Assert.That(proactive.GetPendingSuggestions(), Is.Empty);
        Assert.That(audit.GetRecentEntries(20).Select(entry => entry.Action), Does.Contain("agent.proactive.cleanup"));
        Assert.That(audit.GetRecentEntries(20).Count(entry => entry.Action == "agent.self_check.action"), Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void AgentControlCenterTaskActionsUpdateStateAndAudit()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentTaskService tasks = new(audit, taskStorePath: Path.Combine(root, "tasks.json"));
        AgentTaskState task = tasks.CreateTask("owner", "Operate from control center", ["start", "complete"]);
        AgentControlCenterService service = new(tasks: tasks, auditLog: audit);

        AgentTaskState running = service.StartTaskFromControlCenter(task.Id);
        AgentTaskState completed = service.CompleteTaskFromControlCenter(task.Id, "verified from UI");

        Assert.That(running.Status, Is.EqualTo(AgentTaskStatus.Running));
        Assert.That(completed.Status, Is.EqualTo(AgentTaskStatus.Completed));
        Assert.That(tasks.GetLatestTask()?.Status, Is.EqualTo(AgentTaskStatus.Completed));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.task.completed"));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Actor), Does.Contain("agent-control-ui"));
    }

    [Test]
    public void AgentControlCenterArchivesMaintenanceProposal()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentMaintenanceService maintenance = new(auditLog: audit, proposalStorePath: Path.Combine(root, "maintenance.json"));
        AgentMaintenanceProposal proposal = maintenance.ProposeFromIssueReport(
            new AgentIssueReportSnapshot(
                DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
                "Browser runtime failed",
                [new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-15T09:59:00Z"), "Error", "Browser runtime failed")],
                [],
                [new ModuleHealth("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]),
            "agent");
        AgentControlCenterService service = new(auditLog: audit, maintenance: maintenance);

        AgentMaintenanceArchiveResult result = service.ArchiveMaintenanceProposalFromControlCenter(
            proposal.Id,
            "fixed after tests passed");

        Assert.That(result.Archived, Is.True);
        Assert.That(service.BuildSnapshot(new ChatRuntimeState(false, 0, 0, null, []), "Kira").PendingMaintenanceProposals, Is.Empty);
        Assert.That(maintenance.GetArchivedProposals().Single().Resolution, Does.Contain("fixed after tests"));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Actor), Does.Contain("agent-control-ui"));
    }

    [Test]
    public void AgentControlCenterInspectsIssueReportForMaintenanceProposal()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentMaintenanceService maintenance = new(auditLog: audit, proposalStorePath: Path.Combine(root, "maintenance.json"));
        AgentControlCenterService service = new(
            issueReports: new AgentIssueReportService(audit)
            {
                HealthReporterSourceOverride = [new StubHealthReporter("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]
            },
            auditLog: audit,
            maintenance: maintenance);
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 0,
            LastError: "Browser runtime failed",
            RecentEvents: [new ChatRuntimeEvent(DateTimeOffset.Parse("2026-06-15T09:59:00Z"), "Error", "Browser runtime failed")]);

        AgentMaintenanceInspectionResult first = service.InspectMaintenanceFromControlCenter(
            runtime,
            TimeSpan.FromHours(2));
        AgentMaintenanceInspectionResult duplicate = service.InspectMaintenanceFromControlCenter(
            runtime,
            TimeSpan.FromHours(2));

        Assert.That(first.Created, Is.True);
        Assert.That(duplicate.Created, Is.False);
        Assert.That(duplicate.Proposal?.Id, Is.EqualTo(first.Proposal?.Id));
        Assert.That(service.BuildSnapshot(runtime, "Kira").PendingMaintenanceProposals, Has.Count.EqualTo(1));
    }

    [Test]
    public void AgentControlCenterAutomaticMaintenanceInspectionUsesConfigurationAndInterval()
    {
        string root = CreateTempWorkspace();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T10:00:00Z");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentMaintenanceService maintenance = new(
            auditLog: audit,
            proposalStorePath: Path.Combine(root, "maintenance.json"),
            clock: () => now);
        AgentControlCenterService service = new(
            issueReports: new AgentIssueReportService(audit)
            {
                HealthReporterSourceOverride = [new StubHealthReporter("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]
            },
            auditLog: audit,
            maintenance: maintenance,
            qchatDiagnosticsPath: Path.Combine(root, "qchat-diagnostics.jsonl"),
            clock: () => now)
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowAutomaticMaintenanceInspection = true,
                MaintenanceInspectionIntervalMinutes = 15,
                MaintenanceDuplicateCooldownMinutes = 120
            }
        };
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 0,
            LastError: "Browser runtime failed",
            RecentEvents: [new ChatRuntimeEvent(now.AddMinutes(-1), "Error", "Browser runtime failed")]);

        AgentMaintenanceInspectionResult? first = service.TryAutomaticMaintenanceInspection(runtime);
        AgentMaintenanceInspectionResult? skipped = service.TryAutomaticMaintenanceInspection(runtime);
        now = now.AddMinutes(16);
        AgentMaintenanceInspectionResult? duplicate = service.TryAutomaticMaintenanceInspection(runtime);

        Assert.That(first?.Created, Is.True);
        Assert.That(skipped, Is.Null);
        Assert.That(duplicate?.Created, Is.False);
        Assert.That(duplicate?.Proposal?.Id, Is.EqualTo(first?.Proposal?.Id));
        Assert.That(service.BuildSnapshot(runtime, "Kira").PendingMaintenanceProposals, Has.Count.EqualTo(1));
    }

    [Test]
    public void AgentControlCenterAutomaticMaintenanceInspectionCanBeDisabled()
    {
        string root = CreateTempWorkspace();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T10:00:00Z");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentMaintenanceService maintenance = new(auditLog: audit, proposalStorePath: Path.Combine(root, "maintenance.json"));
        AgentControlCenterService service = new(
            issueReports: new AgentIssueReportService(audit)
            {
                HealthReporterSourceOverride = [new StubHealthReporter("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]
            },
            auditLog: audit,
            maintenance: maintenance,
            qchatDiagnosticsPath: Path.Combine(root, "qchat-diagnostics.jsonl"),
            clock: () => now)
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowAutomaticMaintenanceInspection = false
            }
        };
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 0,
            LastError: "Browser runtime failed",
            RecentEvents: [new ChatRuntimeEvent(now.AddMinutes(-1), "Error", "Browser runtime failed")]);

        AgentMaintenanceInspectionResult? result = service.TryAutomaticMaintenanceInspection(runtime);

        Assert.That(result, Is.Null);
        Assert.That(service.BuildSnapshot(runtime, "Kira").PendingMaintenanceProposals, Is.Empty);
    }

    [Test]
    public void AgentControlCenterShowsWaitingHealthWithoutCreatingMaintenance()
    {
        string root = CreateTempWorkspace();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T10:00:00Z");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentMaintenanceService maintenance = new(
            auditLog: audit,
            proposalStorePath: Path.Combine(root, "maintenance.json"),
            clock: () => now);
        AgentControlCenterService service = new(
            issueReports: new AgentIssueReportService(audit)
            {
                HealthReporterSourceOverride =
                [
                    new StubHealthReporter("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized yet."),
                    new StubHealthReporter("QChat", ModuleHealthStatus.Degraded, "OneBot is configured but disconnected.")
                ]
            },
            auditLog: audit,
            maintenance: maintenance,
            qchatDiagnosticsPath: Path.Combine(root, "qchat-diagnostics.jsonl"),
            clock: () => now)
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowAutomaticMaintenanceInspection = true,
                MaintenanceInspectionIntervalMinutes = 1,
                MaintenanceDuplicateCooldownMinutes = 120
            }
        };
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 0,
            LastError: null,
            RecentEvents: []);

        AgentMaintenanceInspectionResult? result = service.TryAutomaticMaintenanceInspection(runtime);
        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(runtime, "Kira");

        Assert.That(result?.Created, Is.False);
        Assert.That(snapshot.PendingMaintenanceProposals, Is.Empty);
        Assert.That(snapshot.SelfCheck.OwnerReviewCount, Is.Zero);
        Assert.That(snapshot.SelfCheck.Items.Select(item => item.Category), Does.Contain("module-waiting"));
        Assert.That(snapshot.SelfCheck.Items.Select(item => item.Category), Does.Contain("external-environment"));
    }

    [Test]
    public void AgentControlCenterCreatesMaintenanceTaskFromAutomaticInspectionAndDeduplicates()
    {
        string root = CreateTempWorkspace();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-15T10:00:00Z");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentTaskService tasks = new(audit, taskStorePath: Path.Combine(root, "tasks.json"));
        AgentMaintenanceService maintenance = new(
            auditLog: audit,
            proposalStorePath: Path.Combine(root, "maintenance.json"),
            clock: () => now);
        AgentControlCenterService service = new(
            issueReports: new AgentIssueReportService(audit)
            {
                HealthReporterSourceOverride = [new StubHealthReporter("Browser", ModuleHealthStatus.Degraded, "Browser runtime is not initialized.")]
            },
            tasks: tasks,
            auditLog: audit,
            maintenance: maintenance,
            clock: () => now)
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowAutomaticMaintenanceInspection = true,
                MaintenanceInspectionIntervalMinutes = 1,
                MaintenanceDuplicateCooldownMinutes = 120
            }
        };
        ChatRuntimeState runtime = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 0,
            LastError: "Browser runtime failed",
            RecentEvents: [new ChatRuntimeEvent(now.AddMinutes(-1), "Error", "Browser runtime failed")]);

        AgentMaintenanceInspectionResult? first = service.TryAutomaticMaintenanceInspection(runtime);
        now = now.AddMinutes(2);
        AgentMaintenanceInspectionResult? duplicate = service.TryAutomaticMaintenanceInspection(runtime);
        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(runtime, "Kira");

        Assert.That(first?.Created, Is.True);
        Assert.That(duplicate?.Created, Is.False);
        Assert.That(snapshot.ActiveTasks, Has.Count.EqualTo(1));
        Assert.That(snapshot.ActiveTasks[0].Goal, Does.Contain("Resolve maintenance proposal"));
        Assert.That(snapshot.ActiveTasks[0].Steps, Has.Some.Contains("Record repair evidence"));
        Assert.That(snapshot.ActiveTasks[0].Events.Select(taskEvent => taskEvent.Detail), Has.Some.Contains(first!.Proposal!.Id));
        Assert.That(audit.GetRecentEntries(20).Select(entry => entry.Action), Does.Contain("agent.maintenance.task.created"));
    }

    [Test]
    public void AgentControlCenterProactiveActionsConfirmDismissAndCleanup()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T12:00:00Z");
        AgentProactiveBehaviorService proactive = new(clock: () => now);
        AgentControlCenterService service = new()
        {
            ProactiveBehavior = proactive
        };
        AgentProactivePendingSuggestion first = proactive.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneLike,
            "like qzone post",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "like target=1001 post=post-a"));
        AgentProactivePendingSuggestion second = proactive.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneReply,
            "reply to qzone comment",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "reply target=1001 post=post-b comment=comment-b"));

        AgentProactivePendingSuggestion confirmed = service.ConfirmProactiveSuggestionFromControlCenter(first.Id);
        AgentProactivePendingSuggestion dismissed = service.DismissProactiveSuggestionFromControlCenter(second.Id);
        now = now.AddDays(31);
        AgentProactiveCleanupResult cleanup = service.CleanupProactiveSuggestionsFromControlCenter(
            TimeSpan.FromHours(24),
            TimeSpan.FromDays(30));

        Assert.That(confirmed.Status, Is.EqualTo(AgentProactivePendingStatus.Confirmed));
        Assert.That(dismissed.Status, Is.EqualTo(AgentProactivePendingStatus.Dismissed));
        Assert.That(cleanup.RemovedCompletedCount, Is.EqualTo(2));
        Assert.That(proactive.GetCompletedSuggestions(), Is.Empty);
    }

    [Test]
    public async Task AgentControlCenterExecutesConfirmedProactiveSuggestionThroughExecutor()
    {
        AgentProactiveBehaviorService proactive = new();
        StubProactiveExecutor executor = new();
        AgentControlCenterService service = new()
        {
            ProactiveBehavior = proactive,
            ProactiveExecutors = [executor]
        };
        AgentProactivePendingSuggestion pending = proactive.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneLike,
            "like qzone post",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "like target=1001 post=post-a"));
        proactive.ConfirmPendingSuggestion(pending.Id, "owner");

        AgentProactiveExternalExecutionResult result = await service.ExecuteProactiveSuggestionFromControlCenter(pending.Id);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(executor.ExecutedIds, Is.EqualTo(new[] { pending.Id }));
        Assert.That(proactive.GetCompletedSuggestion(pending.Id)?.Status, Is.EqualTo(AgentProactivePendingStatus.Executed));
    }

    [Test]
    public async Task AgentControlCenterProactiveExecutionUsesSecurityGatewayForExternalRequests()
    {
        AgentProactiveBehaviorService proactive = new();
        StubProactiveExecutor executor = new();
        AgentControlCenterService service = new()
        {
            ProactiveBehavior = proactive,
            ProactiveExecutors = [executor]
        };
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };
        AgentProactivePendingSuggestion blockedPending = proactive.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneReply,
            "reply to qzone comment",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "reply target=1001 post=post-a comment=comment-a"));
        proactive.ConfirmPendingSuggestion(blockedPending.Id, "owner");
        AgentProactivePendingSuggestion ownerPending = proactive.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneLike,
            "like qzone post",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "like target=1001 post=post-a"));
        proactive.ConfirmPendingSuggestion(ownerPending.Id, "owner");

        AgentProactiveExternalExecutionResult blocked = await service.ExecuteProactiveSuggestionFromControlCenter(
            blockedPending.Id,
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qzone.reply"),
            config);
        AgentProactiveExternalExecutionResult executed = await service.ExecuteProactiveSuggestionFromControlCenter(
            ownerPending.Id,
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qzone.like"),
            config);

        Assert.That(blocked.Succeeded, Is.False);
        Assert.That(blocked.Message, Does.Contain("Owner confirmation required"));
        Assert.That(executed.Succeeded, Is.True);
        Assert.That(executor.ExecutedIds, Is.EqualTo(new[] { ownerPending.Id }));
        Assert.That(proactive.GetCompletedSuggestion(blockedPending.Id)?.Status, Is.EqualTo(AgentProactivePendingStatus.Confirmed));
        Assert.That(proactive.GetCompletedSuggestion(ownerPending.Id)?.Status, Is.EqualTo(AgentProactivePendingStatus.Executed));
    }

    [Test]
    public void AgentControlCenterBuildsOwnerConfirmationTextForWorkspaceProposal()
    {
        AgentWorkspacePatchProposal proposal = new(
            "abc123",
            "D:/workspace/src/AgentNote.cs",
            "src/AgentNote.cs",
            "AgentNote",
            "GeneratedAgentNote",
            "- AgentNote\n+ GeneratedAgentNote",
            DateTimeOffset.Now);

        string confirmation = AgentControlCenterService.BuildWorkspaceProposalConfirmationText(proposal);

        Assert.That(confirmation, Does.Contain("confirm execute"));
        Assert.That(confirmation, Does.Contain("workspace_apply_proposal"));
        Assert.That(confirmation, Does.Contain("abc123"));
    }

    [Test]
    public void AgentControlCenterExposesConfigurationSnapshot()
    {
        AgentControlCenterService service = new();

        AgentControlCenterSnapshot snapshot = service.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            "Kira");

        Assert.That(snapshot.Configuration.AllowAgentLowRiskSelfConfiguration, Is.True);
        Assert.That(snapshot.Configuration.AllowMentionWakeup, Is.True);
        Assert.That(snapshot.Configuration.AllowAutomaticMaintenanceInspection, Is.True);
        Assert.That(snapshot.Configuration.MaintenanceInspectionIntervalMinutes, Is.EqualTo(15));
        Assert.That(snapshot.Configuration.MaintenanceDuplicateCooldownMinutes, Is.EqualTo(120));
        Assert.That(snapshot.EnvironmentCheck.Items.Select(item => item.Name), Does.Contain("Storage folder"));
        Assert.That(snapshot.EnvironmentCheck.Items.Select(item => item.Name), Does.Contain(".NET runtime"));
        Assert.That(snapshot.EnvironmentCheck.HasBlockingIssues, Is.False);
        Assert.That(snapshot.PendingConfigurationProposals, Is.Empty);
    }

    [Test]
    public void AgentControlCenterMapsProactiveChatIntensityToHumanMode()
    {
        Assert.That(AgentControlCenterService.GetProactiveChatModeName(0), Is.EqualTo("安静"));
        Assert.That(AgentControlCenterService.GetProactiveChatModeName(1), Is.EqualTo("低调"));
        Assert.That(AgentControlCenterService.GetProactiveChatModeName(2), Is.EqualTo("平衡"));
        Assert.That(AgentControlCenterService.GetProactiveChatModeName(4), Is.EqualTo("活跃"));
        Assert.That(AgentControlCenterService.GetProactiveChatModeName(5), Is.EqualTo("高活跃"));
    }

    [Test]
    public void AgentControlCenterFormatsProactiveChatStatusWithHumanMode()
    {
        AgentControlCenterConfig config = new()
        {
            AllowProactiveChat = true,
            ProactiveChatIntensity = 2
        };

        string status = AgentControlCenterService.FormatProactiveChatStatus(config);

        Assert.That(status, Does.Contain("enabled"));
        Assert.That(status, Does.Contain("mode=平衡"));
        Assert.That(status, Does.Contain("intensity=2"));
    }

    [Test]
    public void AgentControlCenterAppliesAllowedLowRiskConfigurationAndAudits()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService service = new(auditLog: audit);

        AgentConfigurationChangeResult result = service.ApplyConfigurationChange(
            "ProactiveChatIntensity",
            "4",
            "agent",
            "owner requested more proactive conversation");

        Assert.That(result.Applied, Is.True);
        Assert.That(service.Configuration!.ProactiveChatIntensity, Is.EqualTo(4));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.config.applied"));
    }

    [Test]
    public void AgentControlCenterAppliesAllowedMaintenanceInspectionConfigurationAndAudits()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService service = new(auditLog: audit);

        AgentConfigurationChangeResult result = service.ApplyConfigurationChange(
            "MaintenanceDuplicateCooldownMinutes",
            "90",
            "agent",
            "reduce repeated maintenance proposal noise");

        Assert.That(result.Applied, Is.True);
        Assert.That(result.RequiresOwnerConfirmation, Is.False);
        Assert.That(result.Proposal, Is.Null);
        Assert.That(service.Configuration!.MaintenanceDuplicateCooldownMinutes, Is.EqualTo(90));
        Assert.That(service.GetPendingConfigurationProposals(), Is.Empty);
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.config.applied"));
    }

    [Test]
    public void AgentControlCenterPersistsLowRiskConfigurationWhenConfigurationSystemIsAvailable()
    {
        string previousStorage = AlifePath.StorageFolderPath;
        string storageRoot = CreateTempWorkspace();
        Directory.CreateDirectory(storageRoot);
        try
        {
            AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            ConfigurationSystem configurationSystem = new(new StorageSystem());
            AgentControlCenterService service = new(configurationSystem: configurationSystem);

            AgentConfigurationChangeResult result = service.ApplyConfigurationChange(
                "ProactiveChatIntensity",
                "5",
                "agent",
                "persist self-configuration");

            AgentControlCenterConfig persisted = (AgentControlCenterConfig)configurationSystem
                .GetConfiguration(typeof(AgentControlCenterService))!;

            Assert.That(result.Applied, Is.True);
            Assert.That(persisted.ProactiveChatIntensity, Is.EqualTo(5));
        }
        finally
        {
            AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public void AgentControlCenterBlocksProtectedConfigurationWithoutOwnerConfirmation()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService service = new(auditLog: audit);

        AgentConfigurationChangeResult result = service.ApplyConfigurationChange(
            "OwnerUserIds",
            "10001",
            "agent",
            "attempt to claim owner access");

        Assert.That(result.Applied, Is.False);
        Assert.That(result.RequiresOwnerConfirmation, Is.True);
        Assert.That(service.GetPendingConfigurationProposals(), Has.Count.EqualTo(1));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.config.proposed"));
    }

    [Test]
    public void AgentControlCenterCreatesHighRiskConfigurationProposal()
    {
        AgentControlCenterService service = new();

        AgentConfigurationChangeProposal proposal = service.ProposeConfigurationChange(
            "AllowedWorkspaceRoots",
            "D:/Alife",
            "agent",
            "needed for code work");

        Assert.That(proposal.Key, Is.EqualTo("AllowedWorkspaceRoots"));
        Assert.That(proposal.RiskLevel, Is.EqualTo(AgentAuditRiskLevel.High));
        Assert.That(service.GetPendingConfigurationProposals().Select(item => item.Id), Does.Contain(proposal.Id));
    }

    [Test]
    public void AgentControlCenterBuildsOwnerConfirmationTextForConfigProposal()
    {
        AgentConfigurationChangeProposal proposal = new(
            "config-1",
            "AllowedWorkspaceRoots",
            "D:/Alife",
            "",
            "needed for code work",
            AgentAuditRiskLevel.High,
            DateTimeOffset.Now,
            "agent");

        string confirmation = AgentControlCenterService.BuildConfigurationProposalConfirmationText(proposal);

        Assert.That(confirmation, Does.Contain("confirm execute"));
        Assert.That(confirmation, Does.Contain("agent_config_apply_proposal"));
        Assert.That(confirmation, Does.Contain("config-1"));
    }

    [Test]
    public void AgentControlCenterAppliesConfirmedConfigProposal()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService service = new(auditLog: audit);
        AgentConfigurationChangeProposal proposal = service.ProposeConfigurationChange(
            "AllowPassiveGroupListening",
            "false",
            "agent",
            "owner wants mention-only mode");

        AgentConfigurationChangeResult result = service.ApplyConfigurationProposal(proposal.Id, "owner");

        Assert.That(result.Applied, Is.True);
        Assert.That(service.Configuration!.AllowPassiveGroupListening, Is.False);
        Assert.That(service.GetPendingConfigurationProposals(), Is.Empty);
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.config.confirmed"));
    }

    [Test]
    public async Task AgentControlCenterXmlToolAppliesAllowedLowRiskConfiguration()
    {
        AgentControlCenterService service = new();
        Character character = new() { Name = "AgentConfigXmlToolTest" };
        ChatHistoryAgentThread thread = new();
        await service.AwakeAsync(new AwakeContext
        {
            Character = character,
            Services = new EmptyServiceProvider(),
            ContextBuilder = thread,
            KernelBuilder = Kernel.CreateBuilder(),
        });
        ChatBot chatBot = new(null!, thread);
        await service.StartAsync(Kernel.CreateBuilder().Build(), new ChatActivity(
            character,
            Kernel.CreateBuilder().Build(),
            null!,
            chatBot,
            []));
        XmlHandlerTable table = new();
        table.Register(new XmlHandler(service));

        await table.Handle("agent_config_apply", OneShotContext(new Dictionary<string, string>
        {
            ["key"] = "AllowProactiveChat",
            ["value"] = "false",
            ["reason"] = "owner wants quieter group behavior",
        }));

        Assert.That(service.Configuration!.AllowProactiveChat, Is.False);
    }

    [Test]
    public void AgentControlCenterExposesSelfConfigurationXmlTools()
    {
        string[] xmlFunctionNames = typeof(AgentControlCenterService)
            .GetMethods()
            .Select(method => method.GetCustomAttributes(typeof(XmlFunctionAttribute), inherit: false)
                .OfType<XmlFunctionAttribute>()
                .FirstOrDefault())
            .OfType<XmlFunctionAttribute>()
            .Select(attribute => attribute.Name ?? string.Empty)
            .ToArray();

        Assert.That(xmlFunctionNames, Does.Contain("agent_config_status"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_self_check"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_self_check_apply"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_config_apply"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_config_propose"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_config_confirmation_text"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_config_apply_proposal"));
    }

    [Test]
    public async Task AgentCoreModificationToolsRegisterWithXmlFunctionCaller()
    {
        string root = CreateTempWorkspace();
        XmlFunctionCaller functionCaller = new(NullLogger<XmlFunctionCaller>.Instance);
        AwakeContext context = new()
        {
            Character = new Character { Name = "AgentToolExposureTest" },
            Services = new EmptyServiceProvider(),
            ContextBuilder = new ChatHistoryAgentThread(),
            KernelBuilder = Kernel.CreateBuilder(),
        };
        InteractiveModule[] modules =
        [
            new AgentControlCenterService(functionCaller: functionCaller),
            new AgentWorkspaceService(new AgentWorkspacePolicy([root]), functionCaller: functionCaller),
            new AgentCommandService(functionCaller: functionCaller),
            new AgentMaintenanceService(functionCaller: functionCaller, proposalStorePath: Path.Combine(root, "maintenance.json")),
            new AgentProactiveBehaviorService(functionCaller: functionCaller, persistencePath: Path.Combine(root, "proactive.json")),
            new QZoneService(functionService: functionCaller),
        ];

        foreach (InteractiveModule module in modules)
            await module.AwakeAsync(context);

        string[] expectedTools =
        [
            "agent_config_status",
            "agent_config_apply",
            "agent_config_apply_proposal",
            "workspace_read",
            "workspace_propose_replace",
            "workspace_apply_proposal",
            "agent_commands",
            "agent_run",
            "agent_maintenance_propose",
            "agent_maintenance_archive",
            "agent_proactive_status",
            "agent_proactive_confirm",
            "qzone_proactive_execute",
        ];
        foreach (string tool in expectedTools)
            Assert.That(functionCaller.CanHandleFunction(tool), Is.True, $"XML tool should be registered: {tool}");
    }

    [Test]
    public async Task AgentTaskXmlToolsMutateTaskLifecycleState()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentTaskService service = new(audit, taskStorePath: Path.Combine(root, "tasks.json"));
        await using ChatBot chatBot = await StartModuleForXmlAsync(service, "AgentTaskXmlLoopTest");
        XmlHandlerTable table = new();
        table.Register(new XmlHandler(service));

        await table.Handle("agent_task_create", OneShotContext(new Dictionary<string, string>
        {
            ["goal"] = "verify xml task loop",
            ["steps"] = "create;start;complete",
            ["actor"] = "xml-test",
        }));
        AgentTaskState created = service.GetLatestTask()!;
        await table.Handle("agent_task_start", OneShotContext(new Dictionary<string, string>
        {
            ["id"] = created.Id,
            ["actor"] = "xml-test",
        }));
        await table.Handle("agent_task_progress", OneShotContext(new Dictionary<string, string>
        {
            ["id"] = created.Id,
            ["detail"] = "xml call reached task service",
            ["actor"] = "xml-test",
        }));
        await table.Handle("agent_task_complete", OneShotContext(new Dictionary<string, string>
        {
            ["id"] = created.Id,
            ["detail"] = "verified",
            ["actor"] = "xml-test",
        }));

        AgentTaskState completed = service.GetTask(created.Id)!;
        Assert.That(completed.Goal, Is.EqualTo("verify xml task loop"));
        Assert.That(completed.Status, Is.EqualTo(AgentTaskStatus.Completed));
        Assert.That(completed.Steps, Is.EqualTo(new[] { "create", "start", "complete" }));
        Assert.That(completed.Events.Select(taskEvent => taskEvent.Kind),
            Is.EqualTo(new[] { "created", "started", "progress", "completed" }));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.task.completed"));
    }

    [Test]
    public async Task AgentWorkspaceXmlToolsProposeAndApplyReplacementInAllowedRoot()
    {
        string root = CreateTempWorkspace();
        string file = Path.Combine(root, "src", "AgentNote.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "namespace Demo;\nclass AgentNote {}\n");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentWorkspaceService service = new(new AgentWorkspacePolicy([root]), auditLog: audit);
        await using ChatBot chatBot = await StartModuleForXmlAsync(service, "AgentWorkspaceXmlLoopTest");
        XmlHandlerTable table = new();
        table.ExecutionPolicy.AllowHighRisk = true;
        table.Register(new XmlHandler(service));

        await table.Handle("workspace_propose_replace", OneShotContext(new Dictionary<string, string>
        {
            ["path"] = "src/AgentNote.cs",
            ["oldtext"] = "class AgentNote {}",
            ["newtext"] = "class GeneratedAgentNote {}",
        }));
        AgentWorkspacePatchProposal proposal = service.GetPendingProposals().Single();
        Assert.That(File.ReadAllText(file), Does.Contain("class AgentNote {}"));

        await table.Handle("workspace_apply_proposal", OneShotContext(new Dictionary<string, string>
        {
            ["id"] = proposal.Id,
        }));

        Assert.That(File.ReadAllText(file), Does.Contain("class GeneratedAgentNote {}"));
        Assert.That(service.GetPendingProposals(), Is.Empty);
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("workspace.replace"));
    }

    [Test]
    public async Task AgentRunXmlToolIsBlockedByDefaultBeforeRunnerExecutes()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        FakeCommandRunner runner = new();
        AgentCommandService service = new(
            new AgentCommandPolicy([
                new AgentCommandDefinition("fake-test", "Fake test command.", "fake", "test", root, TimeSpan.FromSeconds(5))
            ]),
            runner,
            audit);
        await using ChatBot chatBot = await StartModuleForXmlAsync(service, "AgentRunXmlBlockedTest");
        XmlHandlerTable table = new();
        table.Register(new XmlHandler(service));

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await table.Handle("agent_run", OneShotContext(new Dictionary<string, string>
            {
                ["commandid"] = "fake-test",
            })));

        Assert.That(exception!.Message, Does.Contain("high-risk"));
        Assert.That(runner.LastRequest, Is.Null);
        Assert.That(audit.GetRecentEntries(10), Is.Empty);
        Assert.That(chatBot.GetRuntimeState().PendingPokeCount, Is.Zero);
    }

    [Test]
    public async Task AgentRunXmlToolExecutesAllowedCommandWhenHighRiskPolicyAllows()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        FakeCommandRunner runner = new();
        AgentCommandService service = new(
            new AgentCommandPolicy([
                new AgentCommandDefinition("fake-test", "Fake test command.", "fake", "test", root, TimeSpan.FromSeconds(5))
            ]),
            runner,
            audit);
        await using ChatBot chatBot = await StartModuleForXmlAsync(service, "AgentRunXmlAllowedTest");
        XmlHandlerTable table = new();
        table.ExecutionPolicy.AllowHighRisk = true;
        table.Register(new XmlHandler(service));

        await table.Handle("agent_run", OneShotContext(new Dictionary<string, string>
        {
            ["commandid"] = "fake-test",
        }));

        Assert.That(runner.LastRequest, Is.Not.Null);
        Assert.That(runner.LastRequest!.CommandId, Is.EqualTo("fake-test"));
        Assert.That(runner.LastRequest.FileName, Is.EqualTo("fake"));
        Assert.That(runner.LastRequest.Arguments, Is.EqualTo("test"));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("agent.command.fake-test"));
        Assert.That(chatBot.GetRuntimeState().PendingPokeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task WorkspaceWriteXmlToolIsBlockedByDefaultBeforeFileIsCreated()
    {
        string root = CreateTempWorkspace();
        string target = Path.Combine(root, "src", "Generated.cs");
        AgentWorkspaceService service = new(new AgentWorkspacePolicy([root]));
        await using ChatBot chatBot = await StartModuleForXmlAsync(service, "WorkspaceWriteXmlBlockedTest");
        XmlHandlerTable table = new();
        table.Register(new XmlHandler(service));

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await table.Handle("workspace_write", ContentClosingContext(
                "class Generated {}",
                new Dictionary<string, string>
                {
                    ["path"] = "src/Generated.cs",
                    ["overwrite"] = "false",
                })));

        Assert.That(exception!.Message, Does.Contain("high-risk"));
        Assert.That(File.Exists(target), Is.False);
        Assert.That(chatBot.GetRuntimeState().PendingPokeCount, Is.Zero);
    }

    [Test]
    public async Task WorkspaceWriteXmlToolCreatesFileWhenHighRiskPolicyAllows()
    {
        string root = CreateTempWorkspace();
        string target = Path.Combine(root, "src", "Generated.cs");
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentWorkspaceService service = new(new AgentWorkspacePolicy([root]), auditLog: audit);
        await using ChatBot chatBot = await StartModuleForXmlAsync(service, "WorkspaceWriteXmlAllowedTest");
        XmlHandlerTable table = new();
        table.ExecutionPolicy.AllowHighRisk = true;
        table.Register(new XmlHandler(service));

        await table.Handle("workspace_write", ContentClosingContext(
            "class Generated {}",
            new Dictionary<string, string>
            {
                ["path"] = "src/Generated.cs",
                ["overwrite"] = "false",
            }));

        Assert.That(File.ReadAllText(target), Is.EqualTo("class Generated {}"));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Action), Does.Contain("workspace.write"));
        Assert.That(chatBot.GetRuntimeState().PendingPokeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task QZoneLatestPostXmlToolQueriesRuntimeAndQueuesFeedback()
    {
        FakeQZoneRuntime runtime = new()
        {
            LatestPost = new QZonePostSnapshot("post-1", 10001, "latest post"),
            LatestComments =
            [
                new QZoneCommentSnapshot("comment-1", 20001, "first comment"),
                new QZoneCommentSnapshot("comment-2", 20002, "second comment"),
            ],
        };
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                AllowedQZoneTargetIds = "10001",
                DryRunExternalActions = true,
            }
        };
        await using ChatBot chatBot = await StartModuleForXmlAsync(service, "QZoneXmlQueryLoopTest");
        XmlHandlerTable table = new();
        table.Register(new XmlHandler(service));

        await table.Handle("qzonelatestpostandcomments", OneShotContext(new Dictionary<string, string>
        {
            ["targetid"] = "10001",
            ["commentcount"] = "2",
        }));

        Assert.That(runtime.LatestPostRequestTargets, Is.EqualTo(new[] { 10001L }));
        Assert.That(runtime.LatestCommentRequests, Is.EqualTo(new[] { (10001L, "post-1", 2) }));
        Assert.That(chatBot.GetRuntimeState().PendingPokeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task QZoneCommentXmlToolStaysDryRunWithoutCallingRuntime()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                AllowedQZoneTargetIds = "10001",
                DryRunExternalActions = true,
            }
        };
        await using ChatBot chatBot = await StartModuleForXmlAsync(service, "QZoneXmlDryRunLoopTest");
        XmlHandlerTable table = new();
        table.ExecutionPolicy.AllowHighRisk = true;
        table.Register(new XmlHandler(service));

        await table.Handle("qzonecomment", OneShotContext(new Dictionary<string, string>
        {
            ["targetid"] = "10001",
            ["postid"] = "post-1",
            ["content"] = "dry run comment",
        }));

        Assert.That(runtime.CommentRequests, Is.Empty);
        Assert.That(chatBot.GetRuntimeState().PendingPokeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task AgentCoreToolTypesRegisterEveryDeclaredXmlFunctionWithXmlFunctionCaller()
    {
        string root = CreateTempWorkspace();
        XmlFunctionCaller functionCaller = new(NullLogger<XmlFunctionCaller>.Instance);
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentDiagnosticsService diagnostics = new(functionCaller);
        AgentTaskService tasks = new(audit, functionCaller, taskStorePath: Path.Combine(root, "tasks.json"));
        AgentControlCenterService controlCenter = new(auditLog: audit, functionCaller: functionCaller);
        AgentIssueReportService issues = new(auditLog: audit, functionCaller: functionCaller);
        LifeEventStreamService lifeEvents = new(storagePath: Path.Combine(root, "life-events"));
        AwakeContext context = new()
        {
            Character = new Character { Name = "AgentToolInventoryTest" },
            Services = new EmptyServiceProvider(),
            ContextBuilder = new ChatHistoryAgentThread(),
            KernelBuilder = Kernel.CreateBuilder(),
        };
        InteractiveModule[] modules =
        [
            diagnostics,
            new SystemHealthService(functionCaller),
            issues,
            new AgentCapabilityInventoryService(functionCaller),
            new AgentProjectStatusService(
                new AgentWorkspacePolicy([root]),
                new AgentCommandPolicy([]),
                audit,
                functionCaller),
            tasks,
            new AgentSelfModelService(diagnostics, tasks, controlCenter, lifeEvents, functionCaller),
            controlCenter,
            new AgentWorkspaceService(new AgentWorkspacePolicy([root]), auditLog: audit, functionCaller: functionCaller),
            new AgentCommandService(new AgentCommandPolicy([]), new FakeCommandRunner(), audit, functionCaller),
            new AgentMaintenanceService(issues, audit, functionCaller, Path.Combine(root, "maintenance.json")),
            new AgentProactiveBehaviorService(functionCaller: functionCaller, persistencePath: Path.Combine(root, "proactive.json")),
            new EmbodiedActionService([], [], [], functionCaller),
            new QChatRelationCacheService(functionCaller: functionCaller),
            new QZoneService(functionService: functionCaller),
        ];

        foreach (InteractiveModule module in modules)
            await module.AwakeAsync(context);

        string[] expectedTools = modules
            .SelectMany(module => DeclaredXmlFunctionNames(module.GetType()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.That(expectedTools, Does.Contain("agent_config_apply"));
        Assert.That(expectedTools, Does.Contain("agent_capability_inventory"));
        Assert.That(expectedTools, Does.Contain("qchat_joined_groups_refresh"));
        Assert.That(expectedTools, Does.Contain("qzone_proactive_execute"));

        foreach (string tool in expectedTools)
            Assert.That(functionCaller.CanHandleFunction(tool), Is.True, $"Declared XML tool should be registered: {tool}");
    }

    static string CreateTempWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-agent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    static async Task<ChatBot> StartModuleForXmlAsync(InteractiveModule module, string characterName)
    {
        Character character = new() { Name = characterName };
        ChatHistoryAgentThread thread = new();
        await module.AwakeAsync(new AwakeContext
        {
            Character = character,
            Services = new EmptyServiceProvider(),
            ContextBuilder = thread,
            KernelBuilder = Kernel.CreateBuilder(),
        });
        ChatBot chatBot = new(null!, thread);
        await module.StartAsync(Kernel.CreateBuilder().Build(), new ChatActivity(
            character,
            Kernel.CreateBuilder().Build(),
            null!,
            chatBot,
            []));
        return chatBot;
    }

    static IEnumerable<string> DeclaredXmlFunctionNames(Type type)
    {
        return type.GetMethods(System.Reflection.BindingFlags.Public |
                               System.Reflection.BindingFlags.NonPublic |
                               System.Reflection.BindingFlags.Instance)
            .Select(method => new
            {
                MethodName = method.Name,
                Attribute = method.GetCustomAttributes(typeof(XmlFunctionAttribute), inherit: false)
                    .OfType<XmlFunctionAttribute>()
                    .FirstOrDefault(),
            })
            .Where(item => item.Attribute != null)
            .Select(item => item.Attribute!.Name ?? item.MethodName.ToLowerInvariant());
    }

    static XmlContext OneShotContext(IReadOnlyDictionary<string, string> parameters) => new()
    {
        CallMode = CallMode.OneShot,
        Parameters = parameters,
    };

    static XmlExecutorContext ContentClosingContext(
        string content,
        IReadOnlyDictionary<string, string> parameters) => new()
    {
        CallMode = CallMode.Closing,
        CallChain = ["workspace_write"],
        Content = content,
        Parameters = parameters,
    };

    sealed record StubHealthReporter(
        string Name,
        ModuleHealthStatus Status,
        string Summary) : IModuleHealthReporter
    {
        public ModuleHealth GetHealth() => new(Name, Status, Summary);
    }

    sealed record StubCapability(
        string Name,
        EmbodiedCapabilityKind Kind,
        string SelfDescription,
        string? CurrentState) : IEmbodiedCapability
    {
        public string? GetCurrentState() => CurrentState;
    }

    sealed class FakeCommandRunner : IAgentCommandRunner
    {
        public AgentCommandRequest? LastRequest { get; private set; }

        public Task<AgentCommandResult> RunAsync(AgentCommandRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new AgentCommandResult(request.CommandId, 0, "ok", "", TimeSpan.FromMilliseconds(5)));
        }
    }

    sealed class FakeMemoryConsistencyReporter : IMemoryConsistencyReporter
    {
        readonly MemoryConsistencySnapshot snapshot;

        public FakeMemoryConsistencyReporter(MemoryConsistencySnapshot snapshot)
        {
            this.snapshot = snapshot;
            RepairResult = snapshot;
        }

        public int RepairCallCount { get; private set; }
        public MemoryConsistencySnapshot RepairResult { get; set; }

        public MemoryConsistencySnapshot GetMemoryConsistencySnapshot() => snapshot;

        public Task<MemoryConsistencySnapshot> RepairMemoryConsistencyAsync(CancellationToken cancellationToken = default)
        {
            RepairCallCount++;
            return Task.FromResult(RepairResult);
        }
    }

    sealed class StubProactiveExecutor : IAgentProactiveSuggestionExecutor
    {
        public List<string> ExecutedIds { get; } = [];

        public bool CanExecute(AgentProactivePendingSuggestion pending)
        {
            return pending.Suggestion.TargetType == "qzone";
        }

        public Task<AgentProactiveExternalExecutionResult> ExecuteAsync(AgentProactivePendingSuggestion pending)
        {
            ExecutedIds.Add(pending.Id);
            return Task.FromResult(new AgentProactiveExternalExecutionResult(true, "executed by stub"));
        }
    }

    sealed class FakeJoinedQChatGroupProvider : IAgentQChatJoinedGroupProvider
    {
        public AgentQChatJoinedGroupSourceSnapshot CachedSnapshot { get; set; } =
            new(DateTimeOffset.MinValue, []);

        public Task<AgentQChatJoinedGroupSourceSnapshot> RefreshAgentJoinedGroupsAsync()
        {
            return Task.FromResult(CachedSnapshot);
        }

        public AgentQChatJoinedGroupSourceSnapshot GetCachedAgentJoinedGroups()
        {
            return CachedSnapshot;
        }
    }

    sealed class FakeQZoneRuntime : IQZoneRuntime
    {
        public QZonePostSnapshot? LatestPost { get; set; }
        public IReadOnlyList<QZoneCommentSnapshot> LatestComments { get; set; } = [];
        public List<string> PublishedPosts { get; } = [];
        public List<(long TargetId, string PostId, string Content)> CommentRequests { get; } = [];
        public List<(long TargetId, string PostId, string CommentId, string Content)> ReplyRequests { get; } = [];
        public List<(long TargetId, string PostId)> LikeRequests { get; } = [];
        public List<long> LatestPostRequestTargets { get; } = [];
        public List<(long TargetId, string PostId, int Count)> LatestCommentRequests { get; } = [];

        public Task PublishPost(string content)
        {
            PublishedPosts.Add(content);
            return Task.CompletedTask;
        }

        public Task Comment(long targetId, string postId, string content)
        {
            CommentRequests.Add((targetId, postId, content));
            return Task.CompletedTask;
        }

        public Task ReplyComment(long targetId, string postId, string commentId, string content)
        {
            ReplyRequests.Add((targetId, postId, commentId, content));
            return Task.CompletedTask;
        }

        public Task LikePost(long targetId, string postId)
        {
            LikeRequests.Add((targetId, postId));
            return Task.CompletedTask;
        }

        public Task<QZonePostSnapshot?> GetLatestPost(long targetId)
        {
            LatestPostRequestTargets.Add(targetId);
            return Task.FromResult(LatestPost);
        }

        public Task<IReadOnlyList<QZoneCommentSnapshot>> GetLatestComments(long targetId, string postId, int count)
        {
            LatestCommentRequests.Add((targetId, postId, count));
            return Task.FromResult(LatestComments);
        }
    }

    sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
