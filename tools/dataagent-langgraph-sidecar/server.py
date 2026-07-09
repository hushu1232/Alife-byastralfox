from __future__ import annotations

import argparse
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any

try:
    from langgraph.graph import END, StateGraph
except ModuleNotFoundError:
    END = "__end__"
    StateGraph = None


def build_advisory_response(request: dict[str, Any]) -> dict[str, Any]:
    request_id = str(request.get("RequestId") or request.get("requestId") or "")
    node_manifests = request.get("NodeManifests") or request.get("nodeManifests") or []
    first_node = "query_planner"
    if isinstance(node_manifests, list) and node_manifests:
        candidate = node_manifests[0]
        if isinstance(candidate, dict):
            first_node = str(candidate.get("NodeName") or candidate.get("nodeName") or first_node)

    return {
        "RequestId": request_id,
        "Accepted": True,
        "ReasonCode": "langgraph_skeleton_advisory",
        "SelectedNodes": [first_node],
        "NodeProgress": [
            {
                "NodeName": first_node,
                "Status": "Completed",
                "ReasonCode": "advisory_only",
                "Message": "LangGraph skeleton returned advisory intent only.",
                "Facts": {"authority": "csharp"},
            }
        ],
        "TraceSummary": "LangGraph skeleton accepted advisory handoff; C# remains authority.",
        "ContextContribution": "sidecar=langgraph_skeleton;authority=csharp",
        "FallbackRequired": True,
        "NoSqlAuthority": True,
        "ReadOnly": True,
        "RequestedToolNames": [],
        "RequestsCheckpointMutation": False,
        "RequestsVisibleText": False,
    }


def advisory_response(request: dict[str, Any]) -> dict[str, Any]:
    if StateGraph is None:
        return build_advisory_response(request)

    workflow = StateGraph(dict)
    workflow.add_node("advisory", lambda state: {"response": build_advisory_response(state["request"])})
    workflow.set_entry_point("advisory")
    workflow.add_edge("advisory", END)
    app = workflow.compile()
    result = app.invoke({"request": request})
    return result["response"]


class Handler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        if self.path == "/health":
            self._send_json({"ok": True, "manual_only": True})
            return
        self.send_error(404)

    def do_POST(self) -> None:
        if self.path != "/handshake":
            self.send_error(404)
            return

        length = int(self.headers.get("content-length", "0"))
        body = self.rfile.read(length)
        try:
            request = json.loads(body.decode("utf-8"))
        except json.JSONDecodeError:
            self.send_error(400)
            return

        self._send_json(advisory_response(request))

    def log_message(self, format: str, *args: Any) -> None:
        return

    def _send_json(self, value: dict[str, Any]) -> None:
        payload = json.dumps(value).encode("utf-8")
        self.send_response(200)
        self.send_header("content-type", "application/json")
        self.send_header("content-length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()

    if args.host not in {"127.0.0.1", "localhost", "::1"}:
        raise SystemExit("Only loopback hosts are allowed.")

    server = ThreadingHTTPServer((args.host, args.port), Handler)
    server.serve_forever()


if __name__ == "__main__":
    main()
