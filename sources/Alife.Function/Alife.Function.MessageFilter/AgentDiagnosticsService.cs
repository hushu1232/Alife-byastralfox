using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Autofac;

namespace Alife.Function.Agent;

public sealed record AgentCapabilityInfo(
    string Name,
    EmbodiedCapabilityKind Kind,
    string Description,
    string? CurrentState);

public sealed record AgentStateSnapshot(
    string CharacterName,
    bool IsChatting,
    int PendingPokeCount,
    int ChatHistoryCount,
    string? LastError,
    IReadOnlyList<ChatRuntimeEvent> RecentEvents,
    IReadOnlyList<ModuleHealth> ModuleHealth,
    IReadOnlyList<AgentCapabilityInfo> Capabilities);

[Module(
    "Agent Diagnostics",
    "Exposes self-state, module health, recent runtime events, and current embodied capabilities.",
    defaultCategory: "Alife Official/Agent",
    LaunchOrder = -65)]
public class AgentDiagnosticsService(XmlFunctionCaller? functionCaller = null) : InteractiveModule<AgentDiagnosticsService>
{
    public IEnumerable<IModuleHealthReporter>? HealthReporterSourceOverride { get; set; }
    public IEnumerable<IEmbodiedCapability>? CapabilitySourceOverride { get; set; }

    [XmlFunction(FunctionMode.OneShot, name: "agent_state")]
    [Description("Show the bot's self-state, recent runtime events, module health, and current embodied capabilities.")]
    public void AgentState()
    {
        Poke(FormatSnapshot(BuildSnapshot(ChatBot.GetRuntimeState(), Character.Name)));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_errors")]
    [Description("Show the bot's most recent error and recent error runtime events.")]
    public void AgentErrors()
    {
        ChatRuntimeState state = ChatBot.GetRuntimeState();
        ChatRuntimeEvent[] errors = state.RecentEvents
            .Where(runtimeEvent => runtimeEvent.Kind.Equals("Error", StringComparison.OrdinalIgnoreCase))
            .TakeLast(8)
            .ToArray();

        if (state.LastError == null && errors.Length == 0)
        {
            Poke("No recent agent errors.");
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine("Agent errors:");
        if (state.LastError != null)
            builder.AppendLine($"Last error: {state.LastError}");
        foreach (ChatRuntimeEvent runtimeEvent in errors)
            builder.AppendLine($"- {runtimeEvent.Timestamp:HH:mm:ss} {runtimeEvent.Detail}");
        Poke(builder.ToString().TrimEnd());
    }

    public AgentStateSnapshot BuildSnapshot(ChatRuntimeState runtimeState, string characterName)
    {
        return new AgentStateSnapshot(
            characterName,
            runtimeState.IsChatting,
            runtimeState.PendingPokeCount,
            runtimeState.ChatHistoryCount,
            runtimeState.LastError,
            runtimeState.RecentEvents,
            GetHealthSnapshot(),
            GetCapabilitySnapshot());
    }

    public IReadOnlyList<ModuleHealth> GetHealthSnapshot()
    {
        return ResolveHealthReporters()
            .Where(reporter => ReferenceEquals(reporter, this) == false)
            .Select(GetHealthSafely)
            .OrderBy(health => health.Status)
            .ThenBy(health => health.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<AgentCapabilityInfo> GetCapabilitySnapshot()
    {
        return ResolveCapabilities()
            .Where(capability => ReferenceEquals(capability, this) == false)
            .Select(GetCapabilitySafely)
            .OrderBy(capability => capability.Kind)
            .ThenBy(capability => capability.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string FormatSnapshot(AgentStateSnapshot snapshot)
    {
        StringBuilder builder = new();
        builder.AppendLine($"Agent state: {snapshot.CharacterName}");
        builder.AppendLine($"- Chatting: {snapshot.IsChatting}");
        builder.AppendLine($"- Pending poke messages: {snapshot.PendingPokeCount}");
        builder.AppendLine($"- Chat history messages: {snapshot.ChatHistoryCount}");
        builder.AppendLine($"- Last error: {snapshot.LastError ?? "none"}");

        builder.AppendLine("Module health:");
        if (snapshot.ModuleHealth.Count == 0)
            builder.AppendLine("- none");
        foreach (ModuleHealth health in snapshot.ModuleHealth)
            builder.AppendLine($"- [{health.Status}] {health.Name}: {health.Summary}");

        builder.AppendLine("Capabilities:");
        if (snapshot.Capabilities.Count == 0)
            builder.AppendLine("- none");
        foreach (AgentCapabilityInfo capability in snapshot.Capabilities)
        {
            builder.Append($"- [{capability.Kind}] {capability.Name}: {capability.Description}");
            if (string.IsNullOrWhiteSpace(capability.CurrentState) == false)
                builder.Append($" State: {capability.CurrentState}");
            builder.AppendLine();
        }

        builder.AppendLine("Recent events:");
        if (snapshot.RecentEvents.Count == 0)
            builder.AppendLine("- none");
        foreach (ChatRuntimeEvent runtimeEvent in snapshot.RecentEvents.TakeLast(8))
            builder.AppendLine($"- {runtimeEvent.Timestamp:HH:mm:ss} [{runtimeEvent.Kind}] {runtimeEvent.Detail}");

        return builder.ToString().TrimEnd();
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionCaller?.RegisterHandler(this);
    }

    IEnumerable<IModuleHealthReporter> ResolveHealthReporters()
    {
        if (HealthReporterSourceOverride != null)
            return HealthReporterSourceOverride;

        try
        {
            return ChatActivity.ModuleService.Resolve<IEnumerable<IModuleHealthReporter>>();
        }
        catch
        {
            return [];
        }
    }

    IEnumerable<IEmbodiedCapability> ResolveCapabilities()
    {
        if (CapabilitySourceOverride != null)
            return CapabilitySourceOverride;

        try
        {
            return ChatActivity.ModuleService.Resolve<IEnumerable<IEmbodiedCapability>>();
        }
        catch
        {
            return [];
        }
    }

    static ModuleHealth GetHealthSafely(IModuleHealthReporter reporter)
    {
        try
        {
            return reporter.GetHealth();
        }
        catch (Exception ex)
        {
            return new ModuleHealth(
                reporter.GetType().Name,
                ModuleHealthStatus.Unavailable,
                $"health check failed: {ex.Message}");
        }
    }

    static AgentCapabilityInfo GetCapabilitySafely(IEmbodiedCapability capability)
    {
        try
        {
            return new AgentCapabilityInfo(
                capability.Name,
                capability.Kind,
                capability.SelfDescription,
                capability.GetCurrentState());
        }
        catch (Exception ex)
        {
            return new AgentCapabilityInfo(
                capability.GetType().Name,
                EmbodiedCapabilityKind.Tool,
                $"capability check failed: {ex.Message}",
                null);
        }
    }
}
