using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alife.Function.Memory;

public sealed record MemoryTextSanitizationResult(
    string Text,
    bool Changed,
    int RemovedSegments);

public sealed record MemoryHistorySanitizationResult(
    string Json,
    bool Changed,
    int RemovedRecords,
    int SanitizedRecords,
    int RemovedSegments);

public sealed class MemoryTextSanitizer
{
    public static MemoryTextSanitizer Default { get; } = new();

    static readonly string[] NoiseMarkers =
    [
        "<qchat",
        "</qchat",
        "<qchat_quiet_mode",
        "XmlFunctionCaller",
        "qchat tag error",
        "执行qchat标签出错",
        "[系统报点]",
        "系统报点",
        "do not tell the owner",
        "不要告诉主人",
        "自动报点",
        "来自系统的杂项消息推送",
        "回复消息时保持简洁",
        "禁用旁白",
        "EWait",
        "EWake",
        "group-decision decision=",
        "qchat-quiet-mode-enabled",
        "qchat-quiet-mode-disabled",
        "qchat-quiet-message-suppressed"
    ];

    static readonly string[] InvisibleStateMarkers =
    [
        "心理状态",
        "内心",
        "心想",
        "状态：",
        "状态:",
        "安静",
        "待机",
        "等待",
        "观察",
        "旁观",
        "沉默",
        "不语",
        "不回",
        "不回复",
        "不回覆",
        "不回应",
        "不回應",
        "不作",
        "不做",
        "不插话",
        "不插話",
        "看着",
        "看著",
        "听着",
        "聽著",
        "待命",
        "揉",
        "尾巴",
        "耳朵",
        "趴",
        "蹭",
        "抱住",
        "垂眸",
        "抬头",
        "抬頭",
        "低头",
        "低頭",
        "眨眼",
        "摇头",
        "搖頭",
        "点头",
        "點頭",
        "歪头",
        "歪頭",
        "攥",
        "蜷",
        "贴近",
        "貼近",
        "靠近",
        "缩了缩",
        "縮了縮",
        "抖了抖"
    ];

    public MemoryTextSanitizationResult SanitizeText(string? text)
    {
        string source = text ?? "";
        if (source.Length == 0)
            return new MemoryTextSanitizationResult("", false, 0);

        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        List<string> keptLines = new(lines.Length);
        int removed = 0;
        foreach (string line in lines)
        {
            if (IsContaminatedSegment(line))
            {
                removed++;
                continue;
            }

            keptLines.Add(line);
        }

        string sanitized = string.Join(Environment.NewLine, TrimExcessBlankLines(keptLines)).Trim();
        bool changed = removed > 0 || sanitized != source.Trim();
        return new MemoryTextSanitizationResult(sanitized, changed, removed);
    }

    public MemoryHistorySanitizationResult SanitizeHistoryJson(string historyJson)
    {
        JArray records = JArray.Parse(historyJson);
        JArray sanitizedRecords = new();
        int removedRecords = 0;
        int sanitizedRecordsCount = 0;
        int removedSegments = 0;

        foreach (JToken record in records)
        {
            string content = record["Content"]?.Value<string>() ?? "";
            int level = record["MemoryMeta"]?["Level"]?.Value<int>() ?? 0;
            if (ShouldDropHistoryRecord(content, level))
            {
                removedRecords++;
                continue;
            }

            MemoryTextSanitizationResult sanitized = SanitizeText(content);
            if (sanitized.Changed)
            {
                if (string.IsNullOrWhiteSpace(sanitized.Text))
                {
                    removedRecords++;
                    removedSegments += sanitized.RemovedSegments;
                    continue;
                }

                JToken cloned = record.DeepClone();
                cloned["Content"] = sanitized.Text;
                sanitizedRecords.Add(cloned);
                sanitizedRecordsCount++;
                removedSegments += sanitized.RemovedSegments;
                continue;
            }

            sanitizedRecords.Add(record.DeepClone());
        }

        bool changed = removedRecords > 0 || sanitizedRecordsCount > 0;
        string json = changed
            ? sanitizedRecords.ToString(Formatting.Indented)
            : historyJson;
        return new MemoryHistorySanitizationResult(
            json,
            changed,
            removedRecords,
            sanitizedRecordsCount,
            removedSegments);
    }

    public bool IsContaminatedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return NoiseMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase))
               || IsLeakedSilentStatusText(text);
    }

    public bool ShouldDropHistoryRecord(string? content, int level)
    {
        return level == 0 && IsContaminatedText(content);
    }

    static bool IsContaminatedSegment(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        return NoiseMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase))
               || IsLeakedSilentStatusText(line);
    }

    static bool IsLeakedSilentStatusText(string text)
    {
        string trimmed = text.Trim();
        string compact = trimmed
            .Replace('(', '（')
            .Replace(')', '）')
            .Replace(',', '，')
            .Replace('、', '，')
            .Replace("[", "【", StringComparison.Ordinal)
            .Replace("]", "】", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("\t", "", StringComparison.Ordinal);

        if (compact.Contains("心理状态", StringComparison.Ordinal)
            || compact.Contains("内心", StringComparison.Ordinal)
            || compact.Contains("心想", StringComparison.Ordinal))
        {
            return true;
        }

        string inner = UnwrapWholeDirective(trimmed);
        if (inner.Length != trimmed.Length)
            return ContainsInvisibleStateMarker(inner);

        return ContainsWrappedInvisibleStateSegment(compact);
    }

    static bool ContainsInvisibleStateMarker(string value)
    {
        string compact = value
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("\t", "", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal);
        return InvisibleStateMarkers.Any(marker => compact.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    static bool ContainsWrappedInvisibleStateSegment(string value)
    {
        return ContainsWrappedInvisibleStateSegment(value, '（', '）')
               || ContainsWrappedInvisibleStateSegment(value, '【', '】')
               || ContainsWrappedInvisibleStateSegment(value, '*', '*');
    }

    static bool ContainsWrappedInvisibleStateSegment(string value, char start, char end)
    {
        int searchFrom = 0;
        while (searchFrom < value.Length)
        {
            int startIndex = value.IndexOf(start, searchFrom);
            if (startIndex < 0)
                return false;
            int endIndex = value.IndexOf(end, startIndex + 1);
            if (endIndex < 0)
                return false;

            string inner = value[(startIndex + 1)..endIndex];
            if (ContainsInvisibleStateMarker(inner))
                return true;

            searchFrom = endIndex + 1;
        }

        return false;
    }

    static string UnwrapWholeDirective(string value)
    {
        string current = value.Trim();
        while (current.Length >= 2 && IsWrappingPair(current[0], current[^1]))
            current = current[1..^1].Trim();
        return current;
    }

    static bool IsWrappingPair(char start, char end)
    {
        return (start == '(' && end == ')')
               || (start == '（' && end == '）')
               || (start == '[' && end == ']')
               || (start == '【' && end == '】')
               || (start == '*' && end == '*');
    }

    static IEnumerable<string> TrimExcessBlankLines(IEnumerable<string> lines)
    {
        bool previousBlank = false;
        foreach (string line in lines)
        {
            bool blank = string.IsNullOrWhiteSpace(line);
            if (blank && previousBlank)
                continue;

            previousBlank = blank;
            yield return line.TrimEnd();
        }
    }
}
