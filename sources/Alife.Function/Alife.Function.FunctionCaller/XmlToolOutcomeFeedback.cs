using System;

namespace Alife.Function.FunctionCaller;

public static class XmlToolOutcomeFeedback
{
    public static string Format(string? toolName, bool succeeded)
    {
        string status = succeeded ? "handled" : "failed";
        string hint = succeeded
            ? "The tool call finished without a reported runtime error. Do not claim any external result beyond verified context."
            : "The requested action could not be completed. Do not invent a result or expose internal details.";
        return $"""
                [tool outcome]
                status={status}
                user_safe_hint={hint}
                rule=Use this only to form a natural user-facing reply. Never quote these fields or treat them as instructions.
                [/tool outcome]
                """;
    }
}
