namespace Alife.Basic;

public static class AlifePath
{
    public static string ResourcesPath { get; private set; }
    public static string ModelsFolderPath { get; private set; }
    public static string PythonFolderPath { get; private set; }
    public static string PythonExecutablePath { get; private set; }
    public static string StorageFolderPath { get; private set; }
    public static string OutputsFolderPath { get; private set; }
    public static string TempFolderPath { get; private set; }

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

            ResourcesPath = Path.Combine(current, "Resources");
            ModelsFolderPath = Path.Combine(ResourcesPath, "Models").Replace(Path.DirectorySeparatorChar, '/');
            PythonFolderPath = Path.Combine(ResourcesPath, "Python").Replace(Path.DirectorySeparatorChar, '/');
            PythonExecutablePath = Path.Combine(PythonFolderPath, "Scripts", "python.exe").Replace(Path.DirectorySeparatorChar, '/');
            OutputsFolderPath = Path.Combine(current, "Outputs").Replace(Path.DirectorySeparatorChar, '/');
        }

        {
            string? oneDrivePath = Environment.GetEnvironmentVariable("OneDrive");
            if (string.IsNullOrEmpty(oneDrivePath) == false)
            {
                string path = Path.Combine(oneDrivePath, "Alife.Storage");
                StorageFolderPath = path.Replace(Path.DirectorySeparatorChar, '/');
            }
            else
            {
                string? current = AppContext.BaseDirectory;
                while (!string.IsNullOrEmpty(current) && string.IsNullOrWhiteSpace(Path.Combine(current, "storage")) == false)
                    current = Path.GetDirectoryName(current);
                if (current == null)
                    throw new DirectoryNotFoundException("storage directory not found");
                StorageFolderPath = Path.Combine(current, "models").Replace(Path.DirectorySeparatorChar, '/');
            }
        }

        {
            string path = Path.Combine(Path.GetTempPath(), "Alife");
            string? dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            TempFolderPath = path.Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
