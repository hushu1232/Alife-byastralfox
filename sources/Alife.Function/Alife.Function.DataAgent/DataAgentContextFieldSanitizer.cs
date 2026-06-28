namespace Alife.Function.DataAgent;

public static class DataAgentContextFieldSanitizer
{
    public const int MaxResultExplanationLength = 480;

    const string TruncationSuffix = "...";

    public static string Sanitize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SanitizeCore(value, null);
    }

    public static string Sanitize(string value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, TruncationSuffix.Length);
        return SanitizeCore(value, maxLength);
    }

    static string SanitizeCore(string value, int? maxLength)
    {
        List<char> buffer = new(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (current == '\r' && i + 1 < value.Length && value[i + 1] == '\n')
            {
                buffer.Add(' ');
                i++;
                continue;
            }

            buffer.Add(current switch
            {
                '[' => '(',
                ']' => ')',
                _ when char.IsControl(current) => ' ',
                _ => current
            });
        }

        string sanitized = new string(buffer.ToArray()).Trim();
        if (maxLength is not int limit || sanitized.Length <= limit)
            return sanitized;

        return sanitized[..(limit - TruncationSuffix.Length)].TrimEnd() + TruncationSuffix;
    }
}
