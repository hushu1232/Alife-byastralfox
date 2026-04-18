namespace Alife.Basic;

/// <summary>
/// 通用的模型与资源下载引导器。
/// 负责检测文件完整性并调用独立的 WPF 下载器窗口。
/// </summary>
public static class ModelDownloader
{
    public static string ModelScopeCachePath { get; }

    public static string EnsureModel(string modelId, string? targetFile = null)
    {
        string localPath = Path.Combine(ModelScopeCachePath, modelId.Replace(".", "___"));
        string checkFile = Path.Combine(localPath, targetFile ?? "README.md");

        if (!File.Exists(checkFile))
            AlifeCommand.Command("python", $"-c \"from modelscope import snapshot_download; snapshot_download('{modelId}')\"");
        if (!File.Exists(checkFile))
            throw new DirectoryNotFoundException($"模型下载失败，目录不存在：{localPath}");

        return targetFile != null ? checkFile : localPath;
    }

    public static void ConvertSafetensorsToOnnx(string modelDir, string taskType = "feature-extraction")
    {
        AlifeCommand.Command("python", $"-c \"from optimum.exporters.onnx import main_export; main_export(model_name_or_path=r'{modelDir}', output=r'{modelDir}', task='{taskType}')\"");
    }

    static ModelDownloader()
    {
        AlifeCommand.Command("pip", "install optimum[onnxruntime]");
        AlifeCommand.Command("pip", "install modelscope");
        ModelScopeCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "modelscope", "hub", "models").Replace(Path.DirectorySeparatorChar, '/');
    }
}
