using System;
using System.Collections.Generic;
using System.Text;

namespace Alife.Framework;

public enum StreamingOutputMode
{
    Disabled,
    Token,
    Sentence,
    ShortSentence,
}

public sealed record StreamingOutputPolicy(
    StreamingOutputMode Mode,
    int MinBufferedCharacters,
    int MaxBufferedCharacters,
    int PreferSingleMessageUntilCharacters,
    string SentenceBoundaries)
{
    public static StreamingOutputPolicy Disabled { get; } = new(
        StreamingOutputMode.Disabled,
        MinBufferedCharacters: 0,
        MaxBufferedCharacters: int.MaxValue,
        PreferSingleMessageUntilCharacters: int.MaxValue,
        SentenceBoundaries: "。！？.!?\n");

    public static StreamingOutputPolicy Token { get; } = new(
        StreamingOutputMode.Token,
        MinBufferedCharacters: 0,
        MaxBufferedCharacters: 1,
        PreferSingleMessageUntilCharacters: 0,
        SentenceBoundaries: "。！？.!?\n");

    public static StreamingOutputPolicy QqPrivateText { get; } = new(
        StreamingOutputMode.Sentence,
        MinBufferedCharacters: 120,
        MaxBufferedCharacters: 260,
        PreferSingleMessageUntilCharacters: 420,
        SentenceBoundaries: "。！？.!?\n");

    public static StreamingOutputPolicy QqGroupText { get; } = new(
        StreamingOutputMode.Sentence,
        MinBufferedCharacters: 120,
        MaxBufferedCharacters: 260,
        PreferSingleMessageUntilCharacters: 360,
        SentenceBoundaries: "。！？.!?\n");
}

public sealed class StreamingOutputSegmenter(StreamingOutputPolicy policy)
{
    readonly StreamingOutputPolicy policy = policy;
    readonly StringBuilder buffer = new();

    public IReadOnlyList<string> Push(string content)
    {
        if (string.IsNullOrEmpty(content))
            return [];

        if (policy.Mode == StreamingOutputMode.Token)
            return [content];

        buffer.Append(content);

        if (policy.Mode == StreamingOutputMode.Disabled)
            return [];

        return Drain(force: false);
    }

    public IReadOnlyList<string> Flush()
    {
        if (buffer.Length == 0)
            return [];

        string content = buffer.ToString();
        buffer.Clear();
        return [content];
    }

    IReadOnlyList<string> Drain(bool force)
    {
        List<string> segments = [];
        while (buffer.Length > 0)
        {
            string current = buffer.ToString();
            int cutLength = force ? current.Length : FindCutLength(current);
            if (cutLength <= 0)
                break;

            string segment = current[..cutLength];
            buffer.Remove(0, cutLength);
            if (string.IsNullOrWhiteSpace(segment) == false)
                segments.Add(segment);
        }

        return segments;
    }

    int FindCutLength(string current)
    {
        if (HasOpenCqCode(current))
            return 0;

        int sentenceCut = FindSentenceCut(current);
        if (sentenceCut > 0)
            return sentenceCut;

        if (policy.Mode != StreamingOutputMode.ShortSentence)
            return 0;

        int max = Math.Max(1, policy.MaxBufferedCharacters);
        if (current.Length < max)
            return 0;

        return FindSafeMaxCut(current, max);
    }

    int FindSentenceCut(string current)
    {
        int min = Math.Max(0, policy.MinBufferedCharacters);
        if (min > 1 && current.Length < policy.PreferSingleMessageUntilCharacters)
            return 0;

        for (int i = 0; i < current.Length; i++)
        {
            if (policy.SentenceBoundaries.Contains(current[i], StringComparison.Ordinal) == false)
                continue;

            int cut = i + 1;
            if (cut < min)
                continue;

            string candidate = current[..cut];
            if (HasOpenCqCode(candidate))
                continue;

            return cut;
        }

        return 0;
    }

    static int FindSafeMaxCut(string current, int max)
    {
        int cut = Math.Min(max, current.Length);
        string candidate = current[..cut];
        if (HasOpenCqCode(candidate) == false)
            return cut;

        int cqStart = candidate.LastIndexOf("[CQ:", StringComparison.OrdinalIgnoreCase);
        return cqStart > 0 ? cqStart : 0;
    }

    static bool HasOpenCqCode(string value)
    {
        int start = value.LastIndexOf("[CQ:", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return false;

        int end = value.IndexOf(']', start);
        return end < 0;
    }
}
