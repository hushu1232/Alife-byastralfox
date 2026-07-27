using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

sealed class DataAgentImageAssetSqliteStore
{
    const int MaxUnderstandingCharacters = 6000;

    static readonly Regex AssetIdPattern = new("^img_[0-9a-f]{24}$", RegexOptions.CultureInvariant);
    static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    static readonly Regex PerceptualHashPattern = new("^[0-9a-f]{16}$", RegexOptions.CultureInvariant);
    static readonly Regex UrlPattern = new(@"https?://[^\s\]]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex AccountNumberPattern = new(@"\b\d{5,12}\b", RegexOptions.CultureInvariant);
    static readonly Regex AbsolutePathPattern = new(@"(?:[A-Za-z]:\\|/)[^\s\]]+", RegexOptions.CultureInvariant);
    static readonly Regex SensitiveValuePattern = new(
        "api[_-]?key|access[_-]?token|client[_-]?secret|authorization|bearer|cookie|password|sk-[A-Za-z0-9_-]{8,}|nb_[A-Za-z0-9_-]{8,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    readonly string databasePath;

    public DataAgentImageAssetSqliteStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        this.databasePath = databasePath;
    }

    public DataAgentImageAssetRecord? FindById(string assetId)
    {
        return FindOne("asset_id = $value", NormalizeAssetId(assetId));
    }

    public DataAgentImageAssetRecord? FindBySha256(string sha256)
    {
        return FindOne("sha256 = $value", NormalizeSha256(sha256));
    }

    public IReadOnlyList<DataAgentImageAssetMatch> FindSimilar(
        string perceptualHash,
        int maxDistance,
        int maxResults)
    {
        string normalizedHash = NormalizePerceptualHash(perceptualHash, allowEmpty: false);
        ulong expected = ulong.Parse(normalizedHash, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int distanceLimit = Math.Clamp(maxDistance, 0, 64);
        int resultLimit = Math.Clamp(maxResults, 1, 20);

        DataAgentSchemaInitializer.Initialize(databasePath);
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_id, sha256, perceptual_hash, managed_file_id, relative_path, media_type,
                   byte_length, pixel_width, pixel_height, visual_summary, ocr_text,
                   first_seen_at_utc, last_seen_at_utc, seen_count
            FROM image_asset
            WHERE length(perceptual_hash) = 16;
            """;

        // ponytail: linear scan is enough for a personal archive; add a dedicated index only beyond ~10k images.
        using SqliteDataReader reader = command.ExecuteReader();
        List<DataAgentImageAssetMatch> matches = [];
        while (reader.Read())
        {
            DataAgentImageAssetRecord record = ReadRecord(reader);
            if (ulong.TryParse(record.PerceptualHash, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong actual) == false)
                continue;
            int distance = BitOperations.PopCount(expected ^ actual);
            if (distance <= distanceLimit)
                matches.Add(new DataAgentImageAssetMatch(record, distance));
        }

        return matches
            .OrderBy(match => match.HammingDistance)
            .ThenByDescending(match => match.Asset.LastSeenAt)
            .Take(resultLimit)
            .ToArray();
    }

    public void Upsert(DataAgentImageAssetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        DataAgentImageAssetRecord safe = SanitizeRecord(record);

        DataAgentSchemaInitializer.Initialize(databasePath);
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO image_asset (
                asset_id, sha256, perceptual_hash, managed_file_id, relative_path, media_type,
                byte_length, pixel_width, pixel_height, visual_summary, ocr_text,
                first_seen_at_utc, last_seen_at_utc, seen_count)
            VALUES (
                $asset_id, $sha256, $perceptual_hash, $managed_file_id, $relative_path, $media_type,
                $byte_length, $pixel_width, $pixel_height, $visual_summary, $ocr_text,
                $first_seen_at_utc, $last_seen_at_utc, $seen_count)
            ON CONFLICT(sha256) DO UPDATE SET
                perceptual_hash = excluded.perceptual_hash,
                managed_file_id = excluded.managed_file_id,
                relative_path = excluded.relative_path,
                media_type = excluded.media_type,
                byte_length = excluded.byte_length,
                pixel_width = excluded.pixel_width,
                pixel_height = excluded.pixel_height,
                visual_summary = CASE WHEN excluded.visual_summary = '' THEN image_asset.visual_summary ELSE excluded.visual_summary END,
                ocr_text = CASE WHEN excluded.ocr_text = '' THEN image_asset.ocr_text ELSE excluded.ocr_text END,
                last_seen_at_utc = CASE
                    WHEN excluded.last_seen_at_utc > image_asset.last_seen_at_utc
                    THEN excluded.last_seen_at_utc
                    ELSE image_asset.last_seen_at_utc
                END,
                seen_count = image_asset.seen_count + 1;
            """;
        AddRecordParameters(command, safe);
        command.ExecuteNonQuery();
    }

    public void UpdateUnderstanding(
        string assetId,
        string visualSummary,
        string ocrText,
        DateTimeOffset updatedAt)
    {
        string normalizedAssetId = NormalizeAssetId(assetId);
        string safeVisualSummary = SanitizeUnderstanding(visualSummary);
        string safeOcrText = SanitizeUnderstanding(ocrText);
        if (safeVisualSummary.Length == 0 && safeOcrText.Length == 0)
            return;

        DataAgentSchemaInitializer.Initialize(databasePath);
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE image_asset
            SET visual_summary = CASE WHEN $visual_summary = '' THEN visual_summary ELSE $visual_summary END,
                ocr_text = CASE WHEN $ocr_text = '' THEN ocr_text ELSE $ocr_text END,
                last_seen_at_utc = CASE
                    WHEN $last_seen_at_utc > last_seen_at_utc
                    THEN $last_seen_at_utc
                    ELSE last_seen_at_utc
                END
            WHERE asset_id = $asset_id;
            """;
        command.Parameters.AddWithValue("$asset_id", normalizedAssetId);
        command.Parameters.AddWithValue("$visual_summary", safeVisualSummary);
        command.Parameters.AddWithValue("$ocr_text", safeOcrText);
        command.Parameters.AddWithValue("$last_seen_at_utc", FormatTimestamp(updatedAt));
        command.ExecuteNonQuery();
    }

    DataAgentImageAssetRecord? FindOne(string predicate, string value)
    {
        DataAgentSchemaInitializer.Initialize(databasePath);
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_id, sha256, perceptual_hash, managed_file_id, relative_path, media_type,
                   byte_length, pixel_width, pixel_height, visual_summary, ocr_text,
                   first_seen_at_utc, last_seen_at_utc, seen_count
            FROM image_asset
            WHERE __PREDICATE__
            LIMIT 1;
            """.Replace("__PREDICATE__", predicate, StringComparison.Ordinal);
        command.Parameters.AddWithValue("$value", value);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadRecord(reader) : null;
    }

    static DataAgentImageAssetRecord SanitizeRecord(DataAgentImageAssetRecord record)
    {
        if (record.ByteLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(record), "Image byte length must be positive.");
        if (record.PixelWidth < 0 || record.PixelHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(record), "Image dimensions cannot be negative.");

        return record with
        {
            AssetId = NormalizeAssetId(record.AssetId),
            Sha256 = NormalizeSha256(record.Sha256),
            PerceptualHash = NormalizePerceptualHash(record.PerceptualHash, allowEmpty: true),
            ManagedFileId = NormalizeAssetId(record.ManagedFileId),
            RelativePath = NormalizeRelativePath(record.RelativePath),
            MediaType = NormalizeMediaType(record.MediaType),
            VisualSummary = SanitizeUnderstanding(record.VisualSummary),
            OcrText = SanitizeUnderstanding(record.OcrText),
            FirstSeenAt = record.FirstSeenAt.ToUniversalTime(),
            LastSeenAt = record.LastSeenAt.ToUniversalTime(),
            SeenCount = Math.Max(1, record.SeenCount)
        };
    }

    static void AddRecordParameters(SqliteCommand command, DataAgentImageAssetRecord record)
    {
        command.Parameters.AddWithValue("$asset_id", record.AssetId);
        command.Parameters.AddWithValue("$sha256", record.Sha256);
        command.Parameters.AddWithValue("$perceptual_hash", record.PerceptualHash);
        command.Parameters.AddWithValue("$managed_file_id", record.ManagedFileId);
        command.Parameters.AddWithValue("$relative_path", record.RelativePath);
        command.Parameters.AddWithValue("$media_type", record.MediaType);
        command.Parameters.AddWithValue("$byte_length", record.ByteLength);
        command.Parameters.AddWithValue("$pixel_width", record.PixelWidth);
        command.Parameters.AddWithValue("$pixel_height", record.PixelHeight);
        command.Parameters.AddWithValue("$visual_summary", record.VisualSummary);
        command.Parameters.AddWithValue("$ocr_text", record.OcrText);
        command.Parameters.AddWithValue("$first_seen_at_utc", FormatTimestamp(record.FirstSeenAt));
        command.Parameters.AddWithValue("$last_seen_at_utc", FormatTimestamp(record.LastSeenAt));
        command.Parameters.AddWithValue("$seen_count", record.SeenCount);
    }

    static DataAgentImageAssetRecord ReadRecord(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetInt64(6),
        reader.GetInt32(7),
        reader.GetInt32(8),
        reader.GetString(9),
        reader.GetString(10),
        ParseTimestamp(reader.GetString(11)),
        ParseTimestamp(reader.GetString(12)),
        reader.GetInt32(13));

    static string NormalizeAssetId(string value)
    {
        string normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (AssetIdPattern.IsMatch(normalized) == false)
            throw new ArgumentException("Image asset id is invalid.", nameof(value));
        return normalized;
    }

    static string NormalizeSha256(string value)
    {
        string normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (Sha256Pattern.IsMatch(normalized) == false)
            throw new ArgumentException("Image SHA-256 is invalid.", nameof(value));
        return normalized;
    }

    static string NormalizePerceptualHash(string value, bool allowEmpty)
    {
        string normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (allowEmpty && normalized.Length == 0)
            return string.Empty;
        if (PerceptualHashPattern.IsMatch(normalized) == false)
            throw new ArgumentException("Image perceptual hash is invalid.", nameof(value));
        return normalized;
    }

    static string NormalizeRelativePath(string value)
    {
        string normalized = (value ?? string.Empty).Replace('\\', '/').Trim();
        bool hasWindowsDrivePrefix = normalized.Length >= 3 &&
                                     char.IsAsciiLetter(normalized[0]) &&
                                     normalized[1] == ':' &&
                                     normalized[2] == '/';
        if (normalized.Length == 0 ||
            normalized.Length > 260 ||
            Path.IsPathRooted(normalized) ||
            hasWindowsDrivePrefix ||
            normalized.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("Image relative path is invalid.", nameof(value));
        }
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
            throw new ArgumentException("Image relative path escapes the managed workspace.", nameof(value));
        return normalized;
    }

    static string NormalizeMediaType(string value)
    {
        string normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized is "image/jpeg" or "image/png" or "image/gif" or "image/webp" or "image/bmp"
            ? normalized
            : throw new ArgumentException("Image media type is unsupported.", nameof(value));
    }

    static string SanitizeUnderstanding(string value)
    {
        string text = (value ?? string.Empty).ReplaceLineEndings(" ").Trim();
        if (SensitiveValuePattern.IsMatch(text))
            return "[redacted]";
        text = UrlPattern.Replace(text, "[url-hidden]");
        text = AccountNumberPattern.Replace(text, "[number-hidden]");
        text = AbsolutePathPattern.Replace(text, "[path-hidden]");
        return text.Length <= MaxUnderstandingCharacters ? text : text[..MaxUnderstandingCharacters];
    }

    static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
