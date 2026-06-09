using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Alife.Platform;

namespace Alife.Function.PythonPipe;

public class PythonException(string message) : Exception(message);

public sealed class PythonPipeProcess(string scriptName, string pythonCode, string? pythonExe = null) : IAsyncDisposable
{
    public event Action<string>? OnStderr;

    public async Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await callLock.WaitAsync(ct);
        try
        {
            await StartProcessWithBackoffAsync(ct);
            hasStarted = true;
        }
        finally
        {
            callLock.Release();
        }
    }

    public async Task<JsonElement> InvokeAsync(string funcName, params object[] args)
    {
        EnsureStartRequested();
        await callLock.WaitAsync();
        try
        {
            await EnsureProcessAvailableAsync(CancellationToken.None);
            try
            {
                JsonElement result = await InvokeCoreAsync(funcName, args);
                RememberInitialization(funcName, args);
                return result;
            }
            catch (Exception exception) when (IsRecoverableProcessFailure(exception))
            {
                await RestartAsync(CancellationToken.None);
                JsonElement result = await InvokeCoreAsync(funcName, args);
                RememberInitialization(funcName, args);
                return result;
            }
        }
        finally
        {
            callLock.Release();
        }
    }
    public async Task<T> InvokeAsync<T>(string funcName, params object[] args)
    {
        JsonElement result = await InvokeAsync(funcName, args);
        return result.Deserialize<T>(jsonOptions)!;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        await StopProcessAsync(sendShutdown: true);
        callLock.Dispose();

        try { File.Delete(scriptPath); }
        catch {}
    }

    const string BoilerplateHeader = """
                                     import sys, json

                                     def __alife_run():
                                         for line in sys.stdin:
                                             line = line.strip()
                                             if not line:
                                                 continue
                                             try:
                                                 msg = json.loads(line)
                                             except:
                                                 continue
                                             func_name = msg.get('func', '')
                                             if func_name == '__shutdown__':
                                                 break
                                             args = msg.get('args', [])
                                             kwargs = msg.get('kwargs', {})
                                             try:
                                                 func = globals()[func_name]
                                                 if isinstance(args, list) and len(args) == 1 and isinstance(args[0], dict) and not kwargs:
                                                     result = func(**args[0])
                                                 else:
                                                     result = func(*args, **kwargs)
                                                 print(json.dumps({"ok": True, "result": result}, ensure_ascii=False), flush=True)
                                             except Exception as e:
                                                 print(json.dumps({"ok": False, "error": f"{type(e).__name__}: {e}"}, ensure_ascii=False), flush=True)
                                     """;
    const string BoilerplateFooter = """

                                     if __name__ == '__main__':
                                         __alife_run()
                                     """;

    readonly string pythonExe = pythonExe ?? "python";
    readonly string scriptPath = Path.Combine(AlifePath.TempFolderPath, "python_pipe", $"{scriptName}.py");
    readonly JsonSerializerOptions jsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    Process? process;
    StreamWriter? stdin;
    StreamReader? stdout;
    readonly SemaphoreSlim callLock = new(1, 1);
    string? initializationFunctionName;
    object[]? initializationArgs;
    bool hasStarted;
    bool disposed;

    async Task StartProcessWithBackoffAsync(CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt < MaxStartAttempts; attempt++)
        {
            try
            {
                await StopProcessAsync(sendShutdown: false);
                await StartProcessAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (attempt + 1 < MaxStartAttempts)
            {
                lastException = exception;
                AlifeTerminal.LogWarning($"Python 进程 '{scriptName}' 启动失败，准备重试: {exception.Message}");
                await Task.Delay(GetBackoffDelay(attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException($"Python 进程 '{scriptName}' 启动失败", lastException);
    }

    async Task StartProcessAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, BoilerplateHeader + "\n" + pythonCode + "\n" + BoilerplateFooter, cancellationToken);

        ProcessStartInfo processStartInfo = new() {
            FileName = pythonExe,
            Arguments = $"\"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            Environment = { { "PYTHONIOENCODING", "utf-8" }, { "PYTHONUTF8", "1" } },
        };

        process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) => {
            if (e.Data is not null)
                OnStderr?.Invoke(e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();

        stdin = process.StandardInput;
        stdout = process.StandardOutput;
    }

    async Task StopProcessAsync(bool sendShutdown)
    {
        Process? currentProcess = process;
        StreamWriter? currentStdin = stdin;
        StreamReader? currentStdout = stdout;

        process = null;
        stdin = null;
        stdout = null;

        if (currentProcess is not null && !currentProcess.HasExited)
        {
            try
            {
                if (sendShutdown && currentStdin is not null)
                {
                    await currentStdin.WriteLineAsync("""{"func":"__shutdown__","args":[]}""");
                    await currentStdin.FlushAsync();
                }

                if (!currentProcess.WaitForExit(3000))
                    currentProcess.Kill();
            }
            catch
            {
                try { currentProcess.Kill(); }
                catch {}
            }
        }

        if (currentStdin != null)
            await currentStdin.DisposeAsync();
        currentStdout?.Dispose();
        currentProcess?.Dispose();
    }

    async Task EnsureProcessAvailableAsync(CancellationToken cancellationToken)
    {
        if (process is null || process.HasExited)
            await RestartAsync(cancellationToken);
    }

    async Task RestartAsync(CancellationToken cancellationToken)
    {
        AlifeTerminal.LogWarning($"Python 进程 '{scriptName}' 已退出，准备重启");
        await StartProcessWithBackoffAsync(cancellationToken);
        await ReplayInitializationAsync();
    }

    async Task ReplayInitializationAsync()
    {
        if (initializationFunctionName == null || initializationArgs == null)
            return;

        await InvokeCoreAsync(initializationFunctionName, initializationArgs);
    }

    async Task<JsonElement> InvokeCoreAsync(string funcName, object[] args)
    {
        await WriteRequestAsync(funcName, args);
        return await ReadResponseAsync();
    }

    async Task WriteRequestAsync(string funcName, object[] args)
    {
        object payload = new { func = funcName, args };
        string json = JsonSerializer.Serialize(payload, jsonOptions);
        await stdin!.WriteLineAsync(json.AsMemory());
        await stdin.FlushAsync();
    }
    async Task<JsonElement> ReadResponseAsync()
    {
        while (true)
        {
            string? line = await stdout!.ReadLineAsync();

            if (line is null)
                throw new PythonProcessUnavailableException("Python 进程已退出，未收到响应");

            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.AsSpan().TrimStart()[0] != '{')
            {
                OnStderr?.Invoke(line);
                continue;
            }

            JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("ok", out JsonElement ok))
            {
                if (ok.GetBoolean())
                {
                    if (root.TryGetProperty("result", out JsonElement result))
                        return result.Clone();
                    return default;
                }
                else
                {
                    string error = root.TryGetProperty("error", out JsonElement err) ? err.GetString() ?? "" : "";
                    throw new PythonException(error);
                }
            }
        }
    }
    void EnsureStartRequested()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!hasStarted)
            throw new InvalidOperationException($"Python 进程 '{scriptName}' 未启动或已退出，请先调用 StartAsync()");
    }

    void RememberInitialization(string funcName, object[] args)
    {
        if (funcName != "init")
            return;

        initializationFunctionName = funcName;
        initializationArgs = args.ToArray();
    }

    static bool IsRecoverableProcessFailure(Exception exception)
    {
        return exception is PythonProcessUnavailableException or IOException or ObjectDisposedException or InvalidOperationException;
    }

    static TimeSpan GetBackoffDelay(int attempt)
    {
        return TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt));
    }

    const int MaxStartAttempts = 3;

    sealed class PythonProcessUnavailableException(string message) : Exception(message);
}
