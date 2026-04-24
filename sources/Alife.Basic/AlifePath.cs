namespace Alife.Basic;

public static class AlifePath
{
    public static string StorageFolderPath { get; private set; }
    public static string OutputsFolderPath { get; private set; }
    public static string TempFolderPath { get; private set; }

    public static void SetStorageFolderPath(string path)
    {
        StorageFolderPath = path;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "storage_path.txt"), path);
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
    }

    static AlifePath()
    {
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current) && Directory.Exists(Path.Combine(current, "Outputs")) == false)
                current = Path.GetDirectoryName(current);
            if (current == null)
            {
                Terminal.LogError("无法确定项目根目录位置！");
                throw new Exception("无法确定项目根目录位置！");
            }

            OutputsFolderPath = Path.Combine(current, "Outputs").Replace(Path.DirectorySeparatorChar, '/');
        }

        {
            string configPath = Path.Combine(AppContext.BaseDirectory, "storage_path.txt");
            StorageFolderPath = File.Exists(configPath)
                ? File.ReadAllText(configPath).Trim()
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Alife").Replace(Path.DirectorySeparatorChar, '/');

            if (!Directory.Exists(StorageFolderPath))
                Directory.CreateDirectory(StorageFolderPath);
        }

        {
            string path = Path.Combine(Path.GetTempPath(), "Alife");
            TempFolderPath = path.Replace(Path.DirectorySeparatorChar, '/');
            Directory.CreateDirectory(TempFolderPath);
        }
    }
}
