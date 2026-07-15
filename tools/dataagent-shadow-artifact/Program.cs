using System.Globalization;
using Alife.Function.DataAgent;

namespace Alife.Tools.DataAgentShadowArtifact;

public static class Program
{
    static readonly string[] RequiredOptions =
    [
        "--outcome",
        "--reason-code",
        "--health-status",
        "--handshake-status",
        "--context-layers"
    ];

    public static int Main(string[] args)
    {
        try
        {
            if (TryParseRequest(args, out DataAgentManualShadowArtifactRequest? request) == false)
                return WriteResult(false);

            DataAgentLangGraphShadowArtifactWriteResult result =
                DataAgentLangGraphShadowArtifactRuntimeProvider.RecordManualShadowArtifact(
                    request!,
                    DateTimeOffset.UtcNow);
            return WriteResult(result.Written);
        }
        catch
        {
            return WriteResult(false);
        }
    }

    static bool TryParseRequest(string[]? args, out DataAgentManualShadowArtifactRequest? request)
    {
        request = null;
        if (args is null || args.Length != RequiredOptions.Length * 2)
            return false;

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            string option = args[index];
            string value = args[index + 1];
            if (string.IsNullOrEmpty(option) ||
                option.StartsWith("--", StringComparison.Ordinal) == false ||
                option.Contains('=') ||
                RequiredOptions.Contains(option, StringComparer.Ordinal) == false ||
                value.StartsWith("--", StringComparison.Ordinal) ||
                values.TryAdd(option, value) == false)
            {
                return false;
            }
        }

        if (values.Count != RequiredOptions.Length || RequiredOptions.All(values.ContainsKey) == false ||
            IsClosedOutcome(values["--outcome"]) == false ||
            TryParseInvariantInteger(values["--health-status"], out int healthStatus) == false ||
            TryParseInvariantInteger(values["--handshake-status"], out int handshakeStatus) == false ||
            TryParseInvariantInteger(values["--context-layers"], out int contextLayers) == false)
        {
            return false;
        }

        request = new DataAgentManualShadowArtifactRequest(
            values["--outcome"],
            values["--reason-code"],
            healthStatus,
            handshakeStatus,
            contextLayers);
        return true;
    }

    static bool IsClosedOutcome(string value) =>
        string.Equals(value, "accepted", StringComparison.Ordinal) ||
        string.Equals(value, "fallback", StringComparison.Ordinal);

    static bool TryParseInvariantInteger(string value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    static int WriteResult(bool persisted)
    {
        Console.Out.Write(persisted ? "artifact_persisted=true" : "artifact_persisted=false");
        return persisted ? 0 : 1;
    }
}
