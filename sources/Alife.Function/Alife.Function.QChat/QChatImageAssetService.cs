using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Alife.Function.DataAgent;

namespace Alife.Function.QChat;

public sealed record QChatPreparedImageAsset(
    DataAgentImageAssetRecord Record,
    IReadOnlyList<DataAgentImageAssetMatch> SimilarMatches)
{
    public string CachedUnderstanding(bool isOcrRequest) =>
        isOcrRequest ? Record.OcrText : Record.VisualSummary;
}

public sealed class QChatImageAssetService
{
    const long MaxImageBytes = 10L * 1024 * 1024;
    const int SimilarHashDistance = 12;
    const int SimilarResultLimit = 4;

    readonly IOneBotRuntime runtime;
    readonly IDataAgentStore store;
    readonly string storageRoot;
    readonly Action<string, string, object?, Exception?>? diagnosticWriter;

    public QChatImageAssetService(
        IOneBotRuntime runtime,
        IDataAgentStore store,
        string storageRoot,
        Action<string, string, object?, Exception?>? diagnosticWriter = null)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
        this.storageRoot = Path.GetFullPath(storageRoot);
        this.diagnosticWriter = diagnosticWriter;
    }

    public async Task<QChatPreparedImageAsset?> PrepareAsync(
        QChatImageCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (string.IsNullOrWhiteSpace(candidate.File))
            return null;

        OneBotFile? resolved;
        try
        {
            resolved = await runtime.CallActionAsync<OneBotFile>(
                "get_image",
                new { file = candidate.File.Trim() });
        }
        catch (Exception exception)
        {
            WriteDiagnostic("qchat-image-asset-get-image-failed", exception);
            return null;
        }

        if (resolved == null || string.IsNullOrWhiteSpace(resolved.Path))
            return null;

        string sourcePath;
        try
        {
            string resolvedPath = Uri.TryCreate(resolved.Path, UriKind.Absolute, out Uri? fileUri) && fileUri.IsFile
                ? fileUri.LocalPath
                : resolved.Path;
            sourcePath = Path.GetFullPath(resolvedPath);
        }
        catch (Exception exception)
        {
            WriteDiagnostic("qchat-image-asset-path-invalid", exception);
            return null;
        }

        FileInfo source = new(sourcePath);
        if (source.Exists == false || source.Length is <= 0 or > MaxImageBytes)
            return null;

        byte[] bytes;
        try
        {
            await using FileStream stream = new(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            bytes = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
        }
        catch (Exception exception)
        {
            WriteDiagnostic("qchat-image-asset-read-failed", exception);
            return null;
        }

        if (TryDetectMedia(bytes, out string mediaType, out string extension) == false)
            return null;

        string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string assetId = $"img_{sha256[..24]}";
        (string perceptualHash, int pixelWidth, int pixelHeight) = ComputeDHash(bytes);
        string fileName = assetId + extension;
        Directory.CreateDirectory(storageRoot);
        string managedPath = Path.Combine(storageRoot, fileName);
        try
        {
            await WriteIfMissingAsync(managedPath, bytes, cancellationToken);
        }
        catch (Exception exception)
        {
            WriteDiagnostic("qchat-image-asset-write-failed", exception);
            return null;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DataAgentImageAssetRecord? existing = TryFindBySha256(sha256);
        DataAgentImageAssetRecord proposed = new(
            assetId,
            sha256,
            perceptualHash,
            assetId,
            $"{Path.GetFileName(storageRoot)}/{fileName}",
            mediaType,
            bytes.LongLength,
            pixelWidth,
            pixelHeight,
            existing?.VisualSummary ?? string.Empty,
            existing?.OcrText ?? string.Empty,
            existing?.FirstSeenAt ?? now,
            now,
            Math.Max(1, existing?.SeenCount ?? 1));

        TryUpsert(proposed);
        DataAgentImageAssetRecord record = TryFindBySha256(sha256) ?? proposed;
        IReadOnlyList<DataAgentImageAssetMatch> matches = FindSimilar(record)
            .Where(match => string.Equals(match.Asset.AssetId, record.AssetId, StringComparison.OrdinalIgnoreCase) == false)
            .ToArray();
        return new QChatPreparedImageAsset(record, matches);
    }

    public DataAgentImageAssetRecord? FindById(string assetId)
    {
        try
        {
            return store.FindImageAssetById(assetId);
        }
        catch (Exception exception)
        {
            WriteDiagnostic("qchat-image-asset-find-failed", exception);
            return null;
        }
    }

    public IReadOnlyList<DataAgentImageAssetMatch> FindSimilar(DataAgentImageAssetRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.PerceptualHash))
            return [];

        try
        {
            return store.FindSimilarImageAssets(
                record.PerceptualHash,
                SimilarHashDistance,
                SimilarResultLimit);
        }
        catch (Exception exception)
        {
            WriteDiagnostic("qchat-image-asset-similar-failed", exception);
            return [];
        }
    }

    public void UpdateUnderstanding(string assetId, string content, bool isOcrRequest)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        try
        {
            store.UpdateImageAssetUnderstanding(
                assetId,
                isOcrRequest ? string.Empty : content,
                isOcrRequest ? content : string.Empty,
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            WriteDiagnostic("qchat-image-asset-update-failed", exception);
        }
    }

    public string? ResolveManagedPath(DataAgentImageAssetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        string fileName = Path.GetFileName(record.RelativePath);
        if (string.IsNullOrWhiteSpace(fileName) || string.Equals(fileName, record.RelativePath, StringComparison.Ordinal))
            return null;

        string fullPath = Path.GetFullPath(Path.Combine(storageRoot, fileName));
        string rootPrefix = storageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) == false || File.Exists(fullPath) == false)
            return null;
        return fullPath;
    }

    DataAgentImageAssetRecord? TryFindBySha256(string sha256)
    {
        try
        {
            return store.FindImageAssetBySha256(sha256);
        }
        catch (Exception exception)
        {
            WriteDiagnostic("qchat-image-asset-find-failed", exception);
            return null;
        }
    }

    void TryUpsert(DataAgentImageAssetRecord record)
    {
        try
        {
            store.UpsertImageAsset(record);
        }
        catch (Exception exception)
        {
            WriteDiagnostic("qchat-image-asset-store-failed", exception);
        }
    }

    static async Task WriteIfMissingAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
            return;

        try
        {
            await using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await stream.WriteAsync(bytes, cancellationToken);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another identical image won the CreateNew race.
        }
    }

    static (string Hash, int Width, int Height) ComputeDHash(byte[] bytes)
    {
        try
        {
            using MemoryStream stream = new(bytes, writable: false);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            BitmapSource source = decoder.Frames[0];
            int width = source.PixelWidth;
            int height = source.PixelHeight;
            if (width <= 0 || height <= 0)
                return (string.Empty, 0, 0);

            TransformedBitmap scaled = new(
                source,
                new ScaleTransform(9d / width, 8d / height));
            FormatConvertedBitmap gray = new(scaled, PixelFormats.Gray8, null, 0);
            if (gray.PixelWidth != 9 || gray.PixelHeight != 8)
                return (string.Empty, width, height);

            byte[] pixels = new byte[9 * 8];
            gray.CopyPixels(pixels, 9, 0);
            ulong hash = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    hash <<= 1;
                    if (pixels[(y * 9) + x] > pixels[(y * 9) + x + 1])
                        hash |= 1;
                }
            }

            return (hash.ToString("x16"), width, height);
        }
        catch
        {
            return (string.Empty, 0, 0);
        }
    }

    public static bool TryDetectMedia(byte[] bytes, out string mediaType, out string extension)
    {
        mediaType = string.Empty;
        extension = string.Empty;
        if (bytes.Length >= 8 &&
            bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            mediaType = "image/png";
            extension = ".png";
            return true;
        }
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            mediaType = "image/jpeg";
            extension = ".jpg";
            return true;
        }
        if (bytes.Length >= 6 && (bytes.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || bytes.AsSpan(0, 6).SequenceEqual("GIF89a"u8)))
        {
            mediaType = "image/gif";
            extension = ".gif";
            return true;
        }
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            mediaType = "image/bmp";
            extension = ".bmp";
            return true;
        }
        if (bytes.Length >= 12 &&
            bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
            bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            mediaType = "image/webp";
            extension = ".webp";
            return true;
        }
        return false;
    }

    void WriteDiagnostic(string eventName, Exception exception)
    {
        diagnosticWriter?.Invoke(
            eventName,
            "QChat image asset operation failed safely without recording source paths, URLs, or credentials.",
            new { exception = exception.GetType().Name },
            exception);
    }
}
