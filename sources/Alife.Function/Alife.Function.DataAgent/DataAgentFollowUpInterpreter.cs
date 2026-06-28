namespace Alife.Function.DataAgent;

public sealed class DataAgentFollowUpInterpreter
{
    static readonly string[] ContinuePhrases = ["继续", "接着", "再看", "继续看"];
    static readonly string[] RefinePhrases = ["只看", "筛选", "过滤", "换成", "相关"];
    const string RelatedPhrase = "\u76f8\u5173";

    static readonly string[] ExplicitRelatedRefinePhrases = ["\u53ea\u770b", "\u7b5b\u9009", "\u8fc7\u6ee4", "\u6362\u6210"];
    static readonly string[] SummarizePhrases = ["总结", "结论", "归纳"];
    static readonly string[] EndPhrases = ["结束", "停止", "关闭"];

    public DataAgentAnalysisTurnIntent Interpret(
        string question,
        DataAgentAnalysisSession? session = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        string normalized = question.Trim();

        if (ContainsAny(normalized, SummarizePhrases))
            return DataAgentAnalysisTurnIntent.Summarize;

        if (ContainsAny(normalized, EndPhrases))
            return DataAgentAnalysisTurnIntent.End;

        if (session?.Status == DataAgentAnalysisSessionStatus.AwaitingClarification)
            return DataAgentAnalysisTurnIntent.AnswerClarification;

        if (ContainsAny(normalized, ContinuePhrases))
            return DataAgentAnalysisTurnIntent.Continue;

        if (IsRefinePrevious(normalized))
            return DataAgentAnalysisTurnIntent.RefinePrevious;

        return DataAgentAnalysisTurnIntent.NewQuestion;
    }

    static bool IsRefinePrevious(string value)
    {
        foreach (string phrase in RefinePhrases)
        {
            if (string.Equals(phrase, RelatedPhrase, StringComparison.OrdinalIgnoreCase))
                continue;

            if (value.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return value.Contains(RelatedPhrase, StringComparison.OrdinalIgnoreCase)
            && ContainsAny(value, ExplicitRelatedRefinePhrases);
    }

    static bool ContainsAny(string value, IReadOnlyList<string> phrases)
    {
        foreach (string phrase in phrases)
        {
            if (value.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
