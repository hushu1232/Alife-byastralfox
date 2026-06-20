using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Platform;

namespace Alife.Function.Agent;

public sealed record AgentCommandDefinition(
    string Id,
    string Description,
    string FileName,
    string Arguments,
    string WorkingDirectory,
    TimeSpan Timeout);

public sealed record AgentCommandPolicy(IReadOnlyList<AgentCommandDefinition> AllowedCommands);

public sealed record AgentCommandRequest(
    string CommandId,
    string FileName,
    string Arguments,
    string WorkingDirectory,
    TimeSpan Timeout);

public sealed record AgentCommandResult(
    string CommandId,
    int ExitCode,
    string Output,
    string Error,
    TimeSpan Duration);

public interface IAgentCommandRunner
{
    Task<AgentCommandResult> RunAsync(AgentCommandRequest request, CancellationToken cancellationToken);
}

[Module(
    "Agent Command",
    "Runs only predefined maintenance commands and records audit entries for each execution.",
    defaultCategory: "Alife Official/Agent",
    LaunchOrder = -63)]
public class AgentCommandService(
    AgentCommandPolicy? policy = null,
    IAgentCommandRunner? runner = null,
    AgentAuditLogService? auditLog = null,
    XmlFunctionCaller? functionCaller = null)
    : InteractiveModule<AgentCommandService>
{
    readonly AgentCommandPolicy policy = NormalizePolicy(policy ?? CreateDefaultPolicy());
    readonly IAgentCommandRunner runner = runner ?? new ProcessAgentCommandRunner();
    readonly AgentAuditLogService auditLog = auditLog ?? new AgentAuditLogService(
        System.IO.Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "agent-audit.jsonl"));
    public IReadOnlyList<AgentCommandDefinition> AllowedCommands => policy.AllowedCommands;

    [XmlFunction(FunctionMode.OneShot, name: "agent_commands")]
    [Description("List the predefined maintenance commands this agent is allowed to run.")]
    public void AgentCommands()
    {
        if (policy.AllowedCommands.Count == 0)
        {
            Poke("No agent commands are configured.");
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine("Allowed agent commands:");
        foreach (AgentCommandDefinition command in policy.AllowedCommands)
            builder.AppendLine($"- {command.Id}: {command.Description}");
        Poke(builder.ToString().TrimEnd());
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_run", riskLevel: XmlFunctionRiskLevel.High, budgetCost: 10)]
    [Description("Run one predefined maintenance command by id. Unknown commands are blocked.")]
    public async Task AgentRun(string commandId, CancellationToken cancellationToken = default)
    {
        AgentCommandResult result = await RunAllowedCommandAsync(commandId, Character.Name, cancellationToken);
        Poke($"""
              Agent command: {result.CommandId}
              Exit code: {result.ExitCode}
              Duration: {result.Duration.TotalMilliseconds:0}ms
              Output:
              {TrimForReport(result.Output)}
              Error:
              {TrimForReport(result.Error)}
              """);
    }

    public async Task<AgentCommandResult> RunAllowedCommandAsync(
        string commandId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        AgentCommandDefinition definition = policy.AllowedCommands
            .FirstOrDefault(command => command.Id.Equals(commandId, StringComparison.OrdinalIgnoreCase))
            ?? throw new UnauthorizedAccessException($"Agent command is not allowed: {commandId}");

        AgentCommandRequest request = new(
            definition.Id,
            definition.FileName,
            definition.Arguments,
            definition.WorkingDirectory,
            definition.Timeout);

        try
        {
            AgentCommandResult result = await runner.RunAsync(request, cancellationToken);
            auditLog.Record(
                $"agent.command.{definition.Id}",
                actor,
                $"{definition.FileName} {definition.Arguments}",
                AgentAuditRiskLevel.High,
                result.ExitCode == 0,
                result.ExitCode == 0 ? null : result.Error);
            return result;
        }
        catch (Exception ex)
        {
            auditLog.Record(
                $"agent.command.{definition.Id}",
                actor,
                $"{definition.FileName} {definition.Arguments}",
                AgentAuditRiskLevel.High,
                false,
                ex.Message);
            throw;
        }
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionCaller?.RegisterHandler(this);
    }

    static AgentCommandPolicy NormalizePolicy(AgentCommandPolicy rawPolicy)
    {
        AgentCommandDefinition[] commands = rawPolicy.AllowedCommands
            .Where(command => string.IsNullOrWhiteSpace(command.Id) == false)
            .Select(command => command with
            {
                Id = command.Id.Trim(),
                Description = command.Description.Trim(),
                FileName = command.FileName.Trim(),
                Arguments = command.Arguments.Trim(),
                WorkingDirectory = System.IO.Path.GetFullPath(command.WorkingDirectory),
                Timeout = command.Timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : command.Timeout
            })
            .ToArray();

        foreach (AgentCommandDefinition command in commands)
        {
            if (string.IsNullOrWhiteSpace(command.FileName))
                throw new ArgumentException($"Command '{command.Id}' has no executable.");
            if (System.IO.Directory.Exists(command.WorkingDirectory) == false)
                throw new ArgumentException($"Command '{command.Id}' working directory does not exist: {command.WorkingDirectory}");
        }

        return new AgentCommandPolicy(commands);
    }

    static AgentCommandPolicy CreateDefaultPolicy()
    {
        string cwd = Environment.CurrentDirectory;
        string dotnetExecutable = ResolveDotnetExecutable();
        return new AgentCommandPolicy([
            new AgentCommandDefinition("git-status", "Show repository status.", "git", "status --short", cwd, TimeSpan.FromSeconds(20)),
            new AgentCommandDefinition("git-diff", "Show unstaged repository diff.", "git", "diff --", cwd, TimeSpan.FromSeconds(20)),
            new AgentCommandDefinition("dotnet-build-solution", "Build the Alife solution without restoring packages.", dotnetExecutable, "build Alife.slnx --no-restore", cwd, TimeSpan.FromMinutes(3)),
            new AgentCommandDefinition("dotnet-test-solution", "Run the Alife solution tests without restoring packages.", dotnetExecutable, "test Alife.slnx --no-restore", cwd, TimeSpan.FromMinutes(5))
        ]);
    }

    static string ResolveDotnetExecutable()
    {
        string? configured = Environment.GetEnvironmentVariable("ALIFE_AGENT_DOTNET_PATH");
        if (string.IsNullOrWhiteSpace(configured) == false)
            return configured.Trim();

        string? hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (IsDotnetExecutable(hostPath))
            return hostPath!;

        if (IsDotnetExecutable(Environment.ProcessPath))
            return Environment.ProcessPath!;

        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (string.IsNullOrWhiteSpace(dotnetRoot) == false)
        {
            string candidate = System.IO.Path.Combine(dotnetRoot.Trim(), OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (System.IO.File.Exists(candidate))
                return candidate;
        }

        return "dotnet";
    }

    static bool IsDotnetExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string fileName = System.IO.Path.GetFileName(path);
        return fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);
    }

    static string TrimForReport(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(empty)";

        string trimmed = value.Trim();
        return trimmed.Length <= 4000 ? trimmed : trimmed[..4000] + "...";
    }
}

public sealed class ProcessAgentCommandRunner : IAgentCommandRunner
{
    public async Task<AgentCommandResult> RunAsync(AgentCommandRequest request, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        process.Start();
        using CancellationTokenSource timeout = new(request.Timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && cancellationToken.IsCancellationRequested == false)
        {
            TryKill(process);
            throw new TimeoutException($"Agent command timed out: {request.CommandId}");
        }
        finally
        {
            stopwatch.Stop();
        }

        return new AgentCommandResult(
            request.CommandId,
            process.ExitCode,
            await outputTask,
            await errorTask,
            stopwatch.Elapsed);
    }

    static void TryKill(Process process)
    {
        try
        {
            if (process.HasExited == false)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup after timeout.
        }
    }
}
