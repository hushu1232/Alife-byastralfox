using Microsoft.Win32;
using System;
using System.Collections.Generic;

public static class EnvironmentRefresher
{
    /// <summary>
    /// 从系统/用户注册表读取最新的环境变量，并更新到当前进程。
    /// 对 PATH 这类变量会进行合并处理。
    /// </summary>
    public static void RefreshEnvironmentVariables()
    {
        // 1. 从注册表读取系统级和用户级的环境变量
        var machineVariables = GetEnvironmentVariablesFromRegistry(Registry.LocalMachine);
        var userVariables = GetEnvironmentVariablesFromRegistry(Registry.CurrentUser);

        // 2. 合并并更新到当前进程
        MergeAndUpdateProcessVariables(machineVariables, userVariables);
    }

    private static Dictionary<string, string> GetEnvironmentVariablesFromRegistry(RegistryKey baseKey)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var key = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))
        {
            if (key == null) return variables;
            foreach (string valueName in key.GetValueNames())
            {
                string value = key.GetValue(valueName) as string;
                if (!string.IsNullOrEmpty(valueName) && value != null)
                {
                    // 在这里可以处理 %VARIABLE% 的展开，但为了保持简洁，此处直接存储原始字符串
                    // 可根据需要调用 Environment.ExpandEnvironmentVariables(value)
                    variables[valueName] = value;
                }
            }
        }

        // 对于用户级变量，路径不同
        if (baseKey == Registry.CurrentUser)
        {
            using (var key = baseKey.OpenSubKey("Environment"))
            {
                if (key == null) return variables;
                foreach (string valueName in key.GetValueNames())
                {
                    string value = key.GetValue(valueName) as string;
                    if (!string.IsNullOrEmpty(valueName) && value != null)
                    {
                        variables[valueName] = value;
                    }
                }
            }
        }

        return variables;
    }

    private static void MergeAndUpdateProcessVariables(Dictionary<string, string> machineVars, Dictionary<string, string> userVars)
    {
        // 先设置系统变量
        foreach (var kvp in machineVars)
        {
            Environment.SetEnvironmentVariable(kvp.Key, kvp.Value, EnvironmentVariableTarget.Process);
        }

        // 再设置用户变量，用户变量优先
        foreach (var kvp in userVars)
        {
            // 对于PATH，需要特殊合并逻辑：用户PATH + ";" + 系统PATH
            if (kvp.Key.Equals("PATH", StringComparison.OrdinalIgnoreCase))
            {
                string machinePath = machineVars.ContainsKey("PATH") ? machineVars["PATH"] : "";
                string userPath = kvp.Value;
                string combinedPath = userPath + ";" + machinePath;
                Environment.SetEnvironmentVariable("PATH", combinedPath, EnvironmentVariableTarget.Process);
                continue;
            }
            Environment.SetEnvironmentVariable(kvp.Key, kvp.Value, EnvironmentVariableTarget.Process);
        }
    }
}
