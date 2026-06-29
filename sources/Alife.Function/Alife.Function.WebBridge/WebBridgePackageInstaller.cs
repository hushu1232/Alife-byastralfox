using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.WebBridge;

public sealed class WebBridgePackageInstaller(
    string rootDirectory,
    Func<WebBridgePackageFile, Task<byte[]>> downloadFile,
    Func<string, CancellationToken, Task>? reportMilestone = null)
{
    public async Task<WebBridgeInstallResult> Install(
        WebBridgePackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifest.PackageId))
            throw new InvalidOperationException("PackageId is required.");

        string packageRoot = Path.Combine(rootDirectory, "Packages", SanitizeSegment(manifest.PackageId));
        string manifestPath = Path.Combine(rootDirectory, "Manifests", $"{SanitizeSegment(manifest.PackageId)}.json");
        string draftPath = Path.Combine(rootDirectory, "ConfigDrafts", $"{SanitizeSegment(manifest.PackageId)}.json");

        Directory.CreateDirectory(packageRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(draftPath)!);

        int installedFiles = 0;
        foreach (WebBridgePackageFile file in manifest.Files)
        {
            string targetPath = ResolveSafePath(packageRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            byte[] content = await downloadFile(file);
            ValidateHash(file, content);
            await File.WriteAllBytesAsync(targetPath, content, cancellationToken);
            installedFiles++;
        }

        await ReportMilestone(WebBridgeSyncMilestones.FilesDownloaded, cancellationToken);
        await ReportMilestone(WebBridgeSyncMilestones.HashValidated, cancellationToken);

        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(draftPath, JsonSerializer.Serialize(manifest.ConfigDraft, JsonOptions), cancellationToken);
        await WriteCatalog(manifest, packageRoot, manifestPath, draftPath, cancellationToken);
        await ReportMilestone(WebBridgeSyncMilestones.PackageStaged, cancellationToken);

        return new WebBridgeInstallResult
        {
            PackageId = manifest.PackageId,
            Status = WebBridgePackageStatus.PendingActivation,
            PackageRootPath = packageRoot,
            ManifestPath = manifestPath,
            ConfigDraftPath = draftPath,
            InstalledFiles = installedFiles
        };
    }

    static string ResolveSafePath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException("RelativePath is required.");

        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == false &&
            string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase) == false)
            throw new InvalidOperationException($"Package file escapes install root: {relativePath}");

        return fullPath;
    }

    static void ValidateHash(WebBridgePackageFile file, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(file.Sha256))
            return;

        string actual = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase) == false)
            throw new InvalidOperationException($"SHA-256 mismatch for package file: {file.RelativePath}");
    }

    async Task WriteCatalog(
        WebBridgePackageManifest manifest,
        string packageRoot,
        string manifestPath,
        string draftPath,
        CancellationToken cancellationToken)
    {
        string catalogPath = Path.Combine(rootDirectory, "catalog.json");
        WebBridgeLocalCatalog catalog = new();
        if (File.Exists(catalogPath))
        {
            string existing = await File.ReadAllTextAsync(catalogPath, cancellationToken);
            catalog = JsonSerializer.Deserialize<WebBridgeLocalCatalog>(existing, JsonOptions) ?? new WebBridgeLocalCatalog();
        }

        catalog.InstalledPackages.RemoveAll(package => package.PackageId == manifest.PackageId);
        catalog.InstalledPackages.Add(new WebBridgeInstalledPackageRecord
        {
            PackageId = manifest.PackageId,
            PackageType = manifest.PackageType,
            Version = manifest.Version,
            Status = WebBridgePackageStatus.PendingActivation,
            PackageRootPath = packageRoot,
            ManifestPath = manifestPath,
            ConfigDraftPath = draftPath
        });

        await File.WriteAllTextAsync(catalogPath, JsonSerializer.Serialize(catalog, JsonOptions), cancellationToken);
    }

    Task ReportMilestone(string milestone, CancellationToken cancellationToken)
    {
        return reportMilestone?.Invoke(milestone, cancellationToken) ?? Task.CompletedTask;
    }

    static string SanitizeSegment(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '-');
        return value.Trim();
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
