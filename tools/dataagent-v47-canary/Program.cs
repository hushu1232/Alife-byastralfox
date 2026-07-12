namespace Alife.Tools.DataAgentV47Canary;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        DataAgentV47CanaryArgumentResult parsed = DataAgentV47CanaryArguments.Parse(args);
        if (parsed.Accepted == false)
        {
            Console.Error.WriteLine($"canary_accepted=false{Environment.NewLine}reason_code={parsed.ReasonCode}");
            return 1;
        }

        DataAgentV47CanaryRunResult result =
            await new DataAgentV47CanaryRunner().RunAsync(parsed.Value!);
        Console.WriteLine($"canary_accepted={(result.Accepted ? "true" : "false")}");
        Console.WriteLine($"reason_code={result.ReasonCode}");
        Console.WriteLine($"accepted_count={result.AcceptedCount}");
        Console.WriteLine($"network_attempt_count={result.NetworkAttemptCount}");
        if (result.RuntimeIdentity is not null)
            Console.WriteLine($"runtime_instance_id={result.RuntimeIdentity.RuntimeInstanceId}");
        return result.Accepted ? 0 : 1;
    }
}
