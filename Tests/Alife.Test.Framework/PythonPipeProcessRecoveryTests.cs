using System.Diagnostics;
using System.Reflection;
using Alife.Function.PythonPipe;

namespace Alife.Test.Framework;

public class PythonPipeProcessRecoveryTests
{
    [Test]
    public async Task InvokeRestartsExitedProcessAndReplaysInitialization()
    {
        string code = """
prefix = ""

def init(value):
    global prefix
    prefix = value
    return prefix

def add_suffix(value):
    return prefix + value
""";
        await using PythonPipeProcess pipe = new(
            $"recovery_test_{Guid.NewGuid():N}",
            code);
        await pipe.StartAsync();
        string initializedPrefix = await pipe.InvokeAsync<string>("init", "hello ");
        Process process = GetProcess(pipe);

        process.Kill();
        await process.WaitForExitAsync();

        string result = await pipe.InvokeAsync<string>("add_suffix", "world");

        Assert.That(initializedPrefix, Is.EqualTo("hello "));
        Assert.That(result, Is.EqualTo("hello world"));
    }

    static Process GetProcess(PythonPipeProcess pipe)
    {
        FieldInfo? field = typeof(PythonPipeProcess).GetField(
            "process",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "PythonPipeProcess should keep its child process in a private field.");
        Process? process = field!.GetValue(pipe) as Process;
        Assert.That(process, Is.Not.Null);
        return process!;
    }
}
