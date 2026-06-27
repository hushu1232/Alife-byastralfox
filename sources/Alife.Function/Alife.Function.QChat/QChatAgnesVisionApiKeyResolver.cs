using System;

namespace Alife.Function.QChat;

public static class QChatAgnesVisionApiKeyResolver
{
    public const string DefaultEnvironmentVariableName = "ALIFE_AGNES_VISION_API_KEY";

    public static string? Resolve(
        string? configValue,
        string environmentVariableName = DefaultEnvironmentVariableName)
    {
        string? processValue = Environment.GetEnvironmentVariable(environmentVariableName);
        if (string.IsNullOrWhiteSpace(processValue) == false)
            return processValue;

        string? userValue = Environment.GetEnvironmentVariable(
            environmentVariableName,
            EnvironmentVariableTarget.User);
        if (string.IsNullOrWhiteSpace(userValue) == false)
            return userValue;

        string? machineValue = Environment.GetEnvironmentVariable(
            environmentVariableName,
            EnvironmentVariableTarget.Machine);
        if (string.IsNullOrWhiteSpace(machineValue) == false)
            return machineValue;

        return configValue;
    }
}
