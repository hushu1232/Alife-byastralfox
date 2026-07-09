using System.Text.Json;

namespace Alife.Tools.DataAgentReplay;

public sealed record DataAgentReplayFixture(
    string Version,
    string Name,
    string CallerId,
    string Utterance,
    DataAgentReplayRouteStateFixture RouteState,
    DataAgentReplayPlannerFixture Planner,
    IReadOnlyList<string> ExpectedMarkers)
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static DataAgentReplayFixture Load(string path)
    {
        if (File.Exists(path) == false)
            throw new FileNotFoundException($"Fixture not found: {path}", path);

        string json = File.ReadAllText(path);
        DataAgentReplayFixture? fixture = JsonSerializer.Deserialize<DataAgentReplayFixture>(json, JsonOptions);
        if (fixture is null)
            throw new InvalidOperationException($"Fixture could not be parsed: {path}");

        return fixture.Normalize();
    }

    public DataAgentReplayFixture Normalize()
    {
        Require(Version, "version");
        Require(Name, "name");
        Require(CallerId, "callerId");
        Require(Utterance, "utterance");

        if (RouteState is null)
            throw new InvalidOperationException("Fixture routeState is required.");

        if (Planner is null)
            throw new InvalidOperationException("Fixture planner is required.");

        if (ExpectedMarkers is null)
            throw new InvalidOperationException("Fixture expectedMarkers is required.");

        return this with
        {
            Version = Version.Trim(),
            Name = Name.Trim(),
            CallerId = CallerId.Trim(),
            Utterance = Utterance.Trim(),
            RouteState = RouteState.Normalize(),
            Planner = Planner.Normalize(),
            ExpectedMarkers = ExpectedMarkers
                .Where(marker => string.IsNullOrWhiteSpace(marker) == false)
                .Select(marker => marker.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    public static JsonSerializerOptions ReportJsonOptions => JsonOptions;

    static void Require(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Fixture {fieldName} is required.");
    }
}

public sealed record DataAgentReplayRouteStateFixture(
    bool IsOwner,
    bool IsPrivate,
    bool TrustedRuntime,
    string ActiveDataAgentSessionId,
    string ActiveDataAgentSessionStatus)
{
    public DataAgentReplayRouteStateFixture Normalize()
    {
        return this with
        {
            ActiveDataAgentSessionId = ActiveDataAgentSessionId ?? string.Empty,
            ActiveDataAgentSessionStatus = ActiveDataAgentSessionStatus ?? string.Empty
        };
    }
}

public sealed record DataAgentReplayPlannerFixture(
    string Dataset,
    string Intent,
    IReadOnlyList<string> Select,
    IReadOnlyList<DataAgentReplayFilterFixture> Filters,
    int Limit)
{
    public DataAgentReplayPlannerFixture Normalize()
    {
        Require(Dataset, "planner.dataset");
        Require(Intent, "planner.intent");

        if (Select is null)
            throw new InvalidOperationException("Fixture planner.select is required.");

        if (Filters is null)
            throw new InvalidOperationException("Fixture planner.filters is required.");

        if (Limit <= 0)
            throw new InvalidOperationException("Fixture planner.limit must be greater than zero.");

        string[] select = Select
            .Where(field => string.IsNullOrWhiteSpace(field) == false)
            .Select(field => field.Trim())
            .ToArray();
        if (select.Length == 0)
            throw new InvalidOperationException("Fixture planner.select is required.");

        return this with
        {
            Dataset = Dataset.Trim(),
            Intent = Intent.Trim(),
            Select = select,
            Filters = Filters
                .Where(filter => filter is not null)
                .Select(filter => filter.Normalize())
                .ToArray()
        };
    }

    static void Require(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Fixture {fieldName} is required.");
    }
}

public sealed record DataAgentReplayFilterFixture(string Field, string Operator, string Value)
{
    public DataAgentReplayFilterFixture Normalize()
    {
        return this with
        {
            Field = Field ?? string.Empty,
            Operator = Operator ?? string.Empty,
            Value = Value ?? string.Empty
        };
    }
}

public sealed record DataAgentReplayResult(
    DataAgentReplayFixtureSummary Fixture,
    DataAgentReplayRouteReport Route,
    DataAgentReplayXmlPolicyReport XmlPolicy,
    DataAgentReplayRouteContextReport RouteContext,
    DataAgentReplayOrchestrationReport Orchestration,
    DataAgentReplaySessionReport Session,
    DataAgentReplayDiagnosticsReport Diagnostics,
    IReadOnlyList<DataAgentReplayExpectedMarker> ExpectedMarkers,
    DataAgentReplayOfflineBoundary OfflineBoundary,
    bool Passed);

public sealed record DataAgentReplayFixtureSummary(string Version, string Name, string CallerId, string Utterance);

public sealed record DataAgentReplayRouteReport(
    string Domain,
    string Intent,
    string ReasonCode,
    string Reason,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> DeniedTools);

public sealed record DataAgentReplayXmlPolicyReport(bool Allowed, string Reason);

public sealed record DataAgentReplayRouteContextReport(
    bool Present,
    string ToolName,
    bool AllowsTool,
    bool AllowsQuery,
    string RouteId,
    string Intent,
    string ReasonCode,
    string RouteSessionId);

public sealed record DataAgentReplayOrchestrationReport(string Trace, bool Accepted, string RejectedReason, int RowCount);

public sealed record DataAgentReplaySessionReport(string SessionId, string Status, bool HasActiveRouteSession);

public sealed record DataAgentReplayDiagnosticsReport(
    string Evidence,
    string Trace,
    string Progress,
    string Graph,
    string QChatEvidence,
    string QChatTrace,
    string QChatProgress,
    string QChatGraph);

public sealed record DataAgentReplayExpectedMarker(string Marker, bool Passed);

public sealed record DataAgentReplayOfflineBoundary(bool SidecarAuthority, bool DefaultTestsLiveRuntime);
