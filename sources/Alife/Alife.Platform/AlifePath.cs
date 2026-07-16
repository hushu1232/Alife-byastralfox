using System.IO;

namespace Alife.Platform;

public sealed record AlifeLocalPaths(
    string RuntimeFolderPath,
    string StorageFolderPath,
    string TempFolderPath);

public static class AlifePath
{
    public static string RootFolderPath { get; private set; }
    public static string OutputsFolderPath { get; }
    public static string StorageFolderPath { get; private set; }
    public static string RuntimeFolderPath { get; private set; }
    public static string TempFolderPath { get; }

    public static AlifeLocalPaths ResolveLocalProductionPaths(string? runtime, string? storage, string? temp)
    {
        return new AlifeLocalPaths(
            ResolvePathOverride(runtime, Path.Combine(RootFolderPath, "Runtime"), nameof(runtime)),
            ResolvePathOverride(storage, Path.Combine(RootFolderPath, "Storage"), nameof(storage)),
            ResolvePathOverride(temp, Path.Combine(RootFolderPath, ".tmp", "Alife.Client"), nameof(temp)));
    }

    public static void SetStorageFolderPath(string path, bool persist = true)
    {
        string oldPath = StorageFolderPath;
        string newPath = path;

        // 如果路径没变，直接返回
        if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            // 场景：目标地址不存在，且旧地址存在 -> 执行搬家
            if (!Directory.Exists(newPath) && Directory.Exists(oldPath))
            {
                // 确保父目录存在
                string? parent = Path.GetDirectoryName(newPath);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                    Directory.CreateDirectory(parent);

                Directory.Move(oldPath, newPath);
                AlifeTerminal.LogHint($"检测到存储位置变更，数据已从 {oldPath} 迁移至 {newPath}");
            }
            else if (!Directory.Exists(newPath))
            {
                // 目标地址不存在且旧地址也不存在，直接创建
                Directory.CreateDirectory(newPath);
            }
        }
        catch (Exception ex)
        {
            AlifeTerminal.LogError($"存储位置迁移失败: {ex.Message}");
            // 如果迁移失败，至少保证新目录存在，避免后续业务崩溃
            if (!Directory.Exists(newPath)) Directory.CreateDirectory(newPath);
        }

        // 更新配置
        StorageFolderPath = newPath;
        if (persist)
            File.WriteAllText(Path.Combine(RuntimeFolderPath, "storage_path.txt"), newPath);
    }

    private static string ResolvePathOverride(string? value, string fallback, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!Path.IsPathFullyQualified(value))
            throw new ArgumentException("Local production path overrides must be absolute.", parameterName);
        return Path.GetFullPath(value);
    }

    static AlifePath()
    {
        //默认地址
        OutputsFolderPath = Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!;
        RootFolderPath = Path.GetDirectoryName(OutputsFolderPath)!;
        string? storageOverride = Environment.GetEnvironmentVariable("ALIFE_STORAGE_PATH");
        AlifeLocalPaths paths = ResolveLocalProductionPaths(
            Environment.GetEnvironmentVariable("ALIFE_RUNTIME_PATH"),
            storageOverride,
            Environment.GetEnvironmentVariable("ALIFE_TEMP_PATH"));
        StorageFolderPath = paths.StorageFolderPath;
        RuntimeFolderPath = paths.RuntimeFolderPath;
        TempFolderPath = paths.TempFolderPath;

        //后处理
        string configPath = Path.Combine(RuntimeFolderPath, "storage_path.txt");
        if (string.IsNullOrWhiteSpace(storageOverride) && File.Exists(configPath))
            StorageFolderPath = File.ReadAllText(configPath).Trim();
        //保障
        Directory.CreateDirectory(StorageFolderPath);
        Directory.CreateDirectory(RuntimeFolderPath);
        Directory.CreateDirectory(TempFolderPath);
    }
}
