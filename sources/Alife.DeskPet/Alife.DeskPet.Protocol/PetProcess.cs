using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.DeskPet;

/// <summary>
/// 负责跨进程管道的透明收发（双向流收发器）
/// </summary>
public class PetProcess : IDisposable
{
    public const string ExternalOutputPrefix = "@alife:";
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public event Action<IpcCommand>? InputReceived;
    public event Action<IpcEvent>? OutputReceived;

    public PetProcess(
        TextWriter writer,
        TextReader reader,
        string? outputPrefix = null,
        Action<string>? diagnosticOutput = null)
    {
        this.writer = writer;
        this.reader = reader;
        this.outputPrefix = outputPrefix;
        this.diagnosticOutput = diagnosticOutput;
    }

    public void SendInput(IpcCommand cmd)
    {
        Send(cmd);
    }

    public void SendOutput(IpcEvent ev)
    {
        Send(ev);
    }

    public void ListenInput()
    {
        StartListening(InputReceived);
    }

    public void ListenOutput()
    {
        StartListening(OutputReceived);
    }

    public void Dispose()
    {
        listeningCancellation?.Cancel();
    }

    void Send(object msg)
    {
        string json = JsonSerializer.Serialize(msg, JsonOptions);
        writer.WriteLine(json);
        writer.Flush();
    }

    void StartListening<T>(Action<T>? callback)
    {
        SynchronizationContext? syncContext = SynchronizationContext.Current;

        listeningCancellation = new CancellationTokenSource();
        CancellationToken token = listeningCancellation.Token;

        Task.Run(async () => {
            while (token.IsCancellationRequested == false)
            {
                try
                {
                    string? line = await reader.ReadLineAsync(token);
                    if (line == null) break;
                    if (line.Length == 0) continue;

                    if (outputPrefix != null)
                    {
                        if (line.StartsWith(outputPrefix, StringComparison.Ordinal) == false)
                        {
                            diagnosticOutput?.Invoke(line);
                            continue;
                        }
                        line = line[outputPrefix.Length..];
                    }

                    T? msg = JsonSerializer.Deserialize<T>(line, JsonOptions);
                    if (msg == null) break;

                    if (syncContext != null)
                        syncContext.Post(_ => callback?.Invoke(msg), null);
                    else
                        _ = Task.Run(() => callback?.Invoke(msg), token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    diagnosticOutput?.Invoke(e.ToString());
                }
            }
        }, token);
    }

    readonly TextWriter writer;
    readonly TextReader reader;
    readonly string? outputPrefix;
    readonly Action<string>? diagnosticOutput;
    CancellationTokenSource? listeningCancellation;
}

// --- IPC Protocol ---
[JsonDerivedType(typeof(WindowMoveCommand), "window-move")]
[JsonDerivedType(typeof(GetPositionCommand), "get-position")]
[JsonDerivedType(typeof(BubbleCommand), "bubble")]
[JsonDerivedType(typeof(PlayExpressionCommand), "expression")]
[JsonDerivedType(typeof(MotionCommand), "motion")]
[JsonDerivedType(typeof(HideBubbleCommand), "hide-bubble")]
[JsonDerivedType(typeof(StatusCommand), "status")]
[JsonDerivedType(typeof(ParamCommand), "param")]
[JsonDerivedType(typeof(ParamsCommand), "params")]
[JsonDerivedType(typeof(LipSyncCommand), "lip-sync")]
[JsonDerivedType(typeof(IdleCycleCommand), "idle-cycle")]
[JsonDerivedType(typeof(GetParamsCommand), "get-params")]
public abstract record IpcCommand;

public record WindowMoveCommand(double X, double Y, int Duration) : IpcCommand;

public record GetPositionCommand : IpcCommand;

public record BubbleCommand(string Text) : IpcCommand;

public record PlayExpressionCommand(string? Id) : IpcCommand;

public record MotionCommand(string Group, int Index) : IpcCommand;

public record HideBubbleCommand : IpcCommand;

public record StatusCommand(bool Working) : IpcCommand;

public record ParamCommand(string Id, float Value) : IpcCommand;

public record ParamsCommand(Dictionary<string, float> Params) : IpcCommand;

public record LipSyncCommand(float Value) : IpcCommand;

public record IdleCycleCommand(bool Enabled, Dictionary<string, float>? Params = null) : IpcCommand;

public record GetParamsCommand : IpcCommand;

[JsonDerivedType(typeof(ReadyEvent), "ready")]
[JsonDerivedType(typeof(InputEvent), "input")]
[JsonDerivedType(typeof(InteractionEvent), "interaction")]
[JsonDerivedType(typeof(PositionEvent), "position")]
[JsonDerivedType(typeof(ParamsListEvent), "params-list")]
public abstract record IpcEvent;

public record ReadyEvent : IpcEvent;

public record InputEvent(string Text) : IpcEvent;

public record InteractionEvent(string Interaction) : IpcEvent;

public record PositionEvent(double X, double Y) : IpcEvent;

public record ParamInfo(float Value, float Min, float Max);

public record ParamsListEvent(Dictionary<string, ParamInfo> Params) : IpcEvent;
