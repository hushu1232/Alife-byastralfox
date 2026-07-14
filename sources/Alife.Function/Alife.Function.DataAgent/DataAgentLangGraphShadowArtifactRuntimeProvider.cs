namespace Alife.Function.DataAgent;

public static class DataAgentLangGraphShadowArtifactRuntimeProvider
{
    public const string UnavailableAggregate = "state=unavailable\nreason=langgraph_artifact_aggregate_unavailable";

    public static DataAgentLangGraphShadowArtifactWriteResult RecordManualShadowResult(
        DataAgentRealLangGraphManualShadowResult result,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            IDataAgentStore? store = CreateConfiguredSqliteStore();
            return store is null
                ? new DataAgentLangGraphShadowArtifactWriteResult(false, "langgraph_artifact_store_unavailable")
                : store.RecordLangGraphShadowArtifact(CreateArtifact(result, now), now);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return new DataAgentLangGraphShadowArtifactWriteResult(false, "langgraph_artifact_store_unavailable");
        }
    }

    public static string ReadConfiguredAggregate(DateTimeOffset? now = null)
    {
        try
        {
            IDataAgentStore? store = CreateConfiguredSqliteStore();
            if (store is null)
                return UnavailableAggregate;

            return FormatAggregate(store.ReadLangGraphShadowArtifactAggregate(now ?? DateTimeOffset.UtcNow));
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return UnavailableAggregate;
        }
    }

    static IDataAgentStore? CreateConfiguredSqliteStore()
    {
        string databasePath = Path.Combine(AppContext.BaseDirectory, "DataAgent", "dataagent.sqlite");
        DataAgentStoreOptions options = DataAgentStoreFactory.FromEnvironment(databasePath);
        string provider = string.IsNullOrWhiteSpace(options.ProviderName)
            ? "sqlite"
            : options.ProviderName.Trim();

        if (string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase) == false ||
            string.IsNullOrWhiteSpace(options.SqlitePath) ||
            File.Exists(options.SqlitePath) == false)
        {
            return null;
        }

        return DataAgentStoreFactory.Create(options);
    }

    static DataAgentLangGraphShadowArtifact CreateArtifact(
        DataAgentRealLangGraphManualShadowResult result,
        DateTimeOffset now)
    {
        return new DataAgentLangGraphShadowArtifact(
            $"manual-shadow-{Guid.NewGuid():N}",
            "manual-shadow",
            "manual-shadow",
            ClassifyOutcome(result),
            NormalizeReasonCode(result.ReasonCode),
            "manual_shadow_outcome",
            ContextChars: 0,
            DiffGatePassed: result.Accepted,
            FallbackRequired: result.FallbackRequired,
            CreatedAt: now,
            ExpiresAt: now);
    }

    static DataAgentLangGraphShadowArtifactOutcome ClassifyOutcome(
        DataAgentRealLangGraphManualShadowResult result)
    {
        if (result.Accepted)
            return DataAgentLangGraphShadowArtifactOutcome.Accepted;

        string reasonCode = NormalizeReasonCode(result.ReasonCode);
        if (reasonCode.Contains("timeout", StringComparison.Ordinal) ||
            reasonCode.Contains("transport", StringComparison.Ordinal))
        {
            return DataAgentLangGraphShadowArtifactOutcome.Timeout;
        }

        if (reasonCode.Contains("protocol", StringComparison.Ordinal) ||
            reasonCode.Contains("unsafe", StringComparison.Ordinal) ||
            reasonCode.Contains("boundary", StringComparison.Ordinal))
        {
            return DataAgentLangGraphShadowArtifactOutcome.ProtocolRejected;
        }

        if (reasonCode.Contains("gate", StringComparison.Ordinal) ||
            reasonCode.Contains("reject", StringComparison.Ordinal))
        {
            return DataAgentLangGraphShadowArtifactOutcome.GateRejected;
        }

        return DataAgentLangGraphShadowArtifactOutcome.Fallback;
    }

    static string NormalizeReasonCode(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Length > 128)
            return "manual_shadow_reason_redacted";

        foreach (char character in reasonCode)
        {
            if (char.IsAsciiLetterOrDigit(character) == false && character is not '_' and not '-' and not '.')
                return "manual_shadow_reason_redacted";
        }

        return reasonCode;
    }

    static string FormatAggregate(DataAgentLangGraphShadowArtifactAggregate aggregate)
    {
        return string.Join(Environment.NewLine,
            $"total={aggregate.Total}",
            $"accepted={aggregate.Accepted}",
            $"gate_rejected={aggregate.GateRejected}",
            $"protocol_rejected={aggregate.ProtocolRejected}",
            $"timeout={aggregate.Timeout}",
            $"fallback={aggregate.Fallback}",
            $"latest_reason_code={aggregate.LatestReasonCode}",
            $"oldest_created_at={FormatTimestamp(aggregate.OldestCreatedAt)}",
            $"newest_created_at={FormatTimestamp(aggregate.NewestCreatedAt)}",
            $"retention_days={aggregate.RetentionDays}",
            $"per_scope_limit={aggregate.PerScopeLimit}");
    }

    static string FormatTimestamp(DateTimeOffset? value) => value?.ToUniversalTime().ToString("O") ?? "none";
}
