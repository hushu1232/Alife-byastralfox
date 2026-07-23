using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public sealed class QChatReplyLayoutNormalizer
{
    public string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        List<string> blocks = [];
        List<string> ordinary = [];
        List<string> structured = [];
        bool inCode = false;

        void FlushOrdinary()
        {
            if (ordinary.Count > 0)
            {
                blocks.Add(string.Join(" ", ordinary));
                ordinary.Clear();
            }
        }

        void FlushStructured()
        {
            if (structured.Count > 0)
            {
                blocks.Add(string.Join("\n", structured));
                structured.Clear();
            }
        }

        foreach (string sourceLine in lines)
        {
            string line = sourceLine.TrimEnd();
            string trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushOrdinary();
                structured.Add(line);
                inCode = !inCode;
                if (inCode == false)
                    FlushStructured();
                continue;
            }

            if (inCode)
            {
                structured.Add(line);
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushOrdinary();
                FlushStructured();
                continue;
            }

            if (IsStructured(trimmed))
            {
                FlushOrdinary();
                structured.Add(line.Trim());
                continue;
            }

            FlushStructured();
            ordinary.Add(trimmed);
        }

        FlushOrdinary();
        FlushStructured();
        return string.Join("\n\n", blocks.Where(block => string.IsNullOrWhiteSpace(block) == false));
    }

    static bool IsStructured(string line) =>
        line.StartsWith(">", StringComparison.Ordinal) ||
        line.StartsWith("- ", StringComparison.Ordinal) ||
        line.StartsWith("* ", StringComparison.Ordinal) ||
        line.StartsWith("+ ", StringComparison.Ordinal) ||
        (line.Length > 2 && char.IsDigit(line[0]) && line[1] == '.');
}
