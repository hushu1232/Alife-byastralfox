from __future__ import annotations

import json
from collections.abc import Iterable
from typing import Any

from fastapi import FastAPI
from fastapi.responses import StreamingResponse
from pydantic import BaseModel, Field


app = FastAPI(title="DataAgent Graph Dev Sidecar", version="0.1.0")


class GraphHandshakeProgress(BaseModel):
    NodeName: str
    Status: str
    ReasonCode: str
    Message: str = ""
    Facts: dict[str, str] = Field(default_factory=dict)


class GraphHandshakeRequest(BaseModel):
    RequestId: str
    SessionId: str
    TurnId: str
    CallerId: str
    GoalOrQuestion: str
    ScenarioContextSummary: str
    RouteScope: str
    QueryConstraints: str
    NodeManifests: list[dict[str, Any]] = Field(default_factory=list)
    NoSqlAuthority: bool
    ReadOnly: bool
    FallbackAvailable: bool
    TraceBudgetChars: int
    ProgressBudget: int


class GraphHandshakeResponse(BaseModel):
    RequestId: str
    Accepted: bool
    ReasonCode: str
    SelectedNodes: list[str]
    NodeProgress: list[GraphHandshakeProgress]
    TraceSummary: str
    ContextContribution: str
    FallbackRequired: bool
    NoSqlAuthority: bool
    ReadOnly: bool
    RequestedToolNames: list[str]
    RequestsCheckpointMutation: bool
    RequestsVisibleText: bool


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok", "runtime": "dev_sidecar"}


def build_handshake_response(request: GraphHandshakeRequest) -> GraphHandshakeResponse:
    selected_nodes = ["scenario_knowledge", "query_planner", "diagnostics_router"]
    return GraphHandshakeResponse(
        RequestId=request.RequestId,
        Accepted=True,
        ReasonCode="dev_sidecar_accepted",
        SelectedNodes=selected_nodes,
        NodeProgress=[
            GraphHandshakeProgress(
                NodeName="scenario_knowledge",
                Status="Completed",
                ReasonCode="scenario_context_ready",
                Message="scenario context ready",
                Facts={"stage": "scenario"},
            ),
            GraphHandshakeProgress(
                NodeName="query_planner",
                Status="Completed",
                ReasonCode="planner_suggested",
                Message="planner ready",
                Facts={"stage": "planner"},
            ),
            GraphHandshakeProgress(
                NodeName="diagnostics_router",
                Status="Completed",
                ReasonCode="diagnostics_ready",
                Message="diagnostics ready",
                Facts={"stage": "diagnostics"},
            ),
        ],
        TraceSummary="ScenarioKnowledge:Completed>QueryPlanner:Completed>DiagnosticsRouter:Completed",
        ContextContribution="graph_handshake_dev_sidecar=accepted",
        FallbackRequired=False,
        NoSqlAuthority=True,
        ReadOnly=True,
        RequestedToolNames=["dataagent.query_plan.propose", "dataagent.diagnostics.progress.read"],
        RequestsCheckpointMutation=False,
        RequestsVisibleText=False,
    )


@app.post("/handshake")
def handshake(request: GraphHandshakeRequest) -> GraphHandshakeResponse:
    return build_handshake_response(request)


def stream_handshake_events(response: GraphHandshakeResponse) -> Iterable[str]:
    for progress in response.NodeProgress:
        yield json.dumps({"Kind": "Progress", "Progress": progress.model_dump()}) + "\n"

    yield json.dumps({"Kind": "FinalResponse", "Response": response.model_dump()}) + "\n"


@app.post("/handshake-stream")
def handshake_stream(request: GraphHandshakeRequest) -> StreamingResponse:
    response = build_handshake_response(request)
    return StreamingResponse(
        stream_handshake_events(response),
        media_type="application/x-ndjson",
    )
