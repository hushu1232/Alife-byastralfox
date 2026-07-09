namespace Alife.Tools.DataAgentReplay;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            ParseResult parsed = ParseArgs(args);
            if (parsed.ExitCode != 0)
            {
                Console.Error.WriteLine(parsed.Error);
                return parsed.ExitCode;
            }

            DataAgentReplayFixture fixture = DataAgentReplayFixture.Load(parsed.FixturePath);
            DataAgentReplayResult result = DataAgentReplayRunner.Run(fixture);
            string report = parsed.Format.Equals("json", StringComparison.OrdinalIgnoreCase)
                ? DataAgentReplayReportFormatter.FormatJson(result)
                : DataAgentReplayReportFormatter.FormatMarkdown(result);

            Console.WriteLine(report);
            return result.Passed ? 0 : 1;
        }
        catch (FileNotFoundException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    static ParseResult ParseArgs(string[] args)
    {
        string fixturePath = string.Empty;
        string format = "markdown";

        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (arg.Equals("--fixture", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    return ParseResult.Failure("Missing value for --fixture.");

                fixturePath = args[++index];
                continue;
            }

            if (arg.Equals("--format", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    return ParseResult.Failure("Missing value for --format. Supported formats: markdown, json.");

                format = args[++index];
                continue;
            }

            return ParseResult.Failure($"Unknown argument: {arg}");
        }

        if (string.IsNullOrWhiteSpace(fixturePath))
            return ParseResult.Failure("Missing required argument: --fixture <path>.");

        if (format.Equals("markdown", StringComparison.OrdinalIgnoreCase) == false &&
            format.Equals("json", StringComparison.OrdinalIgnoreCase) == false)
        {
            return ParseResult.Failure($"Unsupported format: {format}. Supported formats: markdown, json.");
        }

        return new ParseResult(fixturePath, format, string.Empty, 0);
    }

    sealed record ParseResult(string FixturePath, string Format, string Error, int ExitCode)
    {
        public static ParseResult Failure(string error) => new(string.Empty, string.Empty, error, 1);
    }
}
