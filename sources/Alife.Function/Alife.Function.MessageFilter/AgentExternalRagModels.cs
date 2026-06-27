using System;
using System.Collections.Generic;

namespace Alife.Function.Agent;

public sealed record AgentExternalRagSource(
    string Id,
    string Url,
    string Title,
    DateTimeOffset CreatedAtUtc);

public sealed record AgentExternalRagChunk(
    string Id,
    string SourceId,
    string Url,
    string Title,
    string Text,
    int Index);

public sealed record AgentExternalRagQueryResponse(
    bool Success,
    string Reason,
    IReadOnlyList<AgentExternalRagChunk> Chunks,
    string FormattedContext);
