from __future__ import annotations

import argparse
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any

from contracts import validate_handshake_request
from runtime import RuntimeState, create_runtime


class Handler(BaseHTTPRequestHandler):
    runtime_state: RuntimeState | None = None

    def do_GET(self) -> None:
        if self.path == "/health":
            if self.runtime_state is None:
                self._send_json({"ok": False, "ready": False})
                return
            self._send_json(self.runtime_state.health_attestation())
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

        request = validate_handshake_request(request)
        if self.runtime_state is None or self.runtime_state.compiled_graph is None:
            self.send_error(503)
            return
        result = self.runtime_state.compiled_graph.invoke({"request": request})
        self._send_json(result["response"])

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
    parser.add_argument("--runtime-mode", default="langgraph")
    args = parser.parse_args()

    if args.host not in {"127.0.0.1", "localhost", "::1"}:
        raise SystemExit("Only loopback hosts are allowed.")

    Handler.runtime_state = create_runtime(mode=args.runtime_mode)
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    server.serve_forever()


if __name__ == "__main__":
    main()
