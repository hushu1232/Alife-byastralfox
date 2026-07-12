from __future__ import annotations

import argparse
import json
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any

from contracts import (
    ContractError,
    MAX_BODY_BYTES,
    validate_health_attestation,
    validate_handshake_request,
    validate_handshake_response,
)
from runtime import RuntimeState, create_runtime


@dataclass(frozen=True)
class HttpResult:
    status: int
    body: dict[str, Any]


class SidecarApplication:
    def __init__(self, runtime_state: RuntimeState):
        self.runtime_state = runtime_state

    def handle(
        self,
        method: str,
        path: str,
        headers: dict[str, str],
        body: bytes,
    ) -> HttpResult:
        if method == "GET" and path == "/health":
            try:
                health = validate_health_attestation(self.runtime_state.health_attestation())
            except ContractError:
                return HttpResult(503, {"error": "runtime_not_ready"})
            return HttpResult(200, health)
        if method != "POST" or path != "/handshake":
            return HttpResult(404, {"error": "not_found"})

        content_type = headers.get("content-type", "").split(";", 1)[0].strip().lower()
        if content_type != "application/json":
            return HttpResult(415, {"error": "unsupported_content_type"})
        try:
            declared_length = int(headers.get("content-length", ""))
        except ValueError:
            return HttpResult(400, {"error": "invalid_request_schema"})
        if declared_length > MAX_BODY_BYTES or len(body) > MAX_BODY_BYTES:
            return HttpResult(413, {"error": "request_body_too_large"})
        if declared_length < 1 or declared_length != len(body):
            return HttpResult(400, {"error": "invalid_request_schema"})
        try:
            health = validate_health_attestation(self.runtime_state.health_attestation())
        except ContractError:
            return HttpResult(503, {"error": "runtime_not_ready"})
        if not health["ready"]:
            return HttpResult(503, {"error": "runtime_not_ready"})
        try:
            request = validate_handshake_request(json.loads(body.decode("utf-8")))
        except (ContractError, UnicodeDecodeError, json.JSONDecodeError):
            return HttpResult(400, {"error": "invalid_request_schema"})
        try:
            result = self.runtime_state.compiled_graph.invoke({"request": request})
            response = validate_handshake_response(result["response"])
        except Exception:
            return HttpResult(500, {"error": "graph_failure"})
        return HttpResult(200, response)


class Handler(BaseHTTPRequestHandler):
    runtime_state: RuntimeState | None = None

    def do_GET(self) -> None:
        if self.runtime_state is None:
            self._send_json({"error": "runtime_not_ready"}, 503)
            return
        result = SidecarApplication(self.runtime_state).handle("GET", self.path, {}, b"")
        self._send_json(result.body, result.status)

    def do_POST(self) -> None:
        if self.runtime_state is None:
            self._send_json({"error": "runtime_not_ready"}, 503)
            return
        try:
            length = int(self.headers.get("content-length", ""))
        except ValueError:
            length = 0
        headers = {key.lower(): value for key, value in self.headers.items()}
        if length > MAX_BODY_BYTES:
            result = SidecarApplication(self.runtime_state).handle(
                "POST", self.path, headers, b""
            )
            self._send_json(result.body, result.status)
            return
        body = self.rfile.read(length)
        result = SidecarApplication(self.runtime_state).handle(
            "POST", self.path, headers, body
        )
        self._send_json(result.body, result.status)

    def log_message(self, format: str, *args: Any) -> None:
        return

    def _send_json(self, value: dict[str, Any], status: int = 200) -> None:
        payload = json.dumps(value).encode("utf-8")
        self.send_response(status)
        self.send_header("content-type", "application/json")
        self.send_header("content-length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--runtime-mode", default="langgraph")
    args = parser.parse_args()

    if args.host not in {"127.0.0.1", "localhost", "::1"}:
        raise SystemExit("Only loopback hosts are allowed.")

    Handler.runtime_state = create_runtime(mode=args.runtime_mode)
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    server.serve_forever()


if __name__ == "__main__":
    main()
