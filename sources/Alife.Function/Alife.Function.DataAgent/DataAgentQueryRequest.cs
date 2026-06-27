namespace Alife.Function.DataAgent;

public sealed record DataAgentQueryRequest(string Question, string Role, string Locale, bool AllowLiveSources);
