using System;

namespace Alife.Function.MessageFilter;

public sealed class AgentPlanningService(AgentPlanningConfig config)
{
    public bool ShouldUsePlanner(string request)
    {
        if (config.EnablePlanner == false)
            return false;

        string text = request ?? "";
        return text.Contains("代码", StringComparison.Ordinal)
               || text.Contains("源码", StringComparison.Ordinal)
               || text.Contains("日志", StringComparison.Ordinal)
               || text.Contains("构建", StringComparison.Ordinal)
               || text.Contains("测试", StringComparison.Ordinal)
               || text.Contains("修复", StringComparison.Ordinal)
               || text.Contains(".cs", StringComparison.OrdinalIgnoreCase)
               || text.Contains("GitHub", StringComparison.OrdinalIgnoreCase)
               || text.Contains("build", StringComparison.OrdinalIgnoreCase)
               || text.Contains("test", StringComparison.OrdinalIgnoreCase)
               || text.Contains("log", StringComparison.OrdinalIgnoreCase);
    }

    public string BuildPlannerInstruction(string request)
    {
        return $"""
                [planner session - read-only]
                You are the planner model. Work read-only.
                do not execute tools that mutate files, delete files, push git, restart processes, or change configuration.
                Analyze the request, inspect only evidence needed for planning, and return an execution plan for the executor.
                Request:
                {(request ?? "").Trim()}
                [/planner session]
                """;
    }
}
