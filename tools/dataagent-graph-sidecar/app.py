from __future__ import annotations

from typing import Any

from fastapi import FastAPI
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


@app.post("/handshake")
def handshake(request: GraphHandshakeRequest) -> GraphHandshakeResponse:
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
                Facts={"source": "graph_sidecar", "stage": "scenario"},
            ),
            GraphHandshakeProgress(
                NodeName="query_planner",
                Status="Completed",
                ReasonCode="planner_suggested",
                Message="planner ready",
                Facts={"source": "graph_sidecar", "stage": "planner"},
            ),
            GraphHandshakeProgress(
                NodeName="diagnostics_router",
                Status="Completed",
                ReasonCode="diagnostics_ready",
                Message="diagnostics ready",
                Facts={"source": "graph_sidecar", "stage": "diagnostics"},
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
