using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.WebBridge;

public class WebAssetSync(string targetDirectory)
{
    public async Task SyncAssets(WebAssetManifest manifest, CancellationToken cancellationToken = default)
    {
        string rootPath = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(rootPath);

        foreach (WebAssetFile file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string destinationPath = Path.GetFullPath(Path.Combine(rootPath, file.RelativePath));
            if (destinationPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == false)
                throw new InvalidOperationException($"素材路径越界: {file.RelativePath}");

            string? directory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrEmpty(directory) == false)
                Directory.CreateDirectory(directory);

            byte[] data = Convert.FromBase64String(file.ContentBase64);
            await File.WriteAllBytesAsync(destinationPath, data, cancellationToken);
        }
    }
}
