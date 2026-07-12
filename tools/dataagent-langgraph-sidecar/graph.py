from typing import Any, NotRequired, TypedDict

from contracts import build_accepted_response


class AdvisoryGraphState(TypedDict):
    request: dict[str, Any]
    response: NotRequired[dict[str, Any]]


def _advisory_node(state: AdvisoryGraphState) -> dict[str, Any]:
    return {"response": build_accepted_response(state["request"])}


def compile_advisory_graph(state_graph_factory=None, end_marker=None):
    if state_graph_factory is None or end_marker is None:
        from langgraph.graph import END, StateGraph
        state_graph_factory = state_graph_factory or StateGraph
        end_marker = END if end_marker is None else end_marker

    workflow = state_graph_factory(AdvisoryGraphState)
    workflow.add_node("advisory", _advisory_node)
    workflow.set_entry_point("advisory")
    workflow.add_edge("advisory", end_marker)
    return workflow.compile()
