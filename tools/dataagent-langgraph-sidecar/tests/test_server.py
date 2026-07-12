import json
import unittest
from types import SimpleNamespace

from test_contracts import load_module, valid_request


def body_bytes(value):
    return json.dumps(value).encode("utf-8")


class FakeGraph:
    def __init__(self, response=None, error=None):
        self.response = response
        self.error = error
        self.invocations = 0

    def invoke(self, _state):
        self.invocations += 1
        if self.error is not None:
            raise self.error
        return {"response": self.response}


def runtime_state(*, ready=True, graph=None):
    graph = graph if graph is not None else FakeGraph({})
    health = {
        "ok": True,
        "ready": ready,
        "runtimeMode": "langgraph" if ready else "deterministic-stub",
        "langGraphLoaded": ready,
        "langGraphVersion": "0.3.34" if ready else None,
        "graphCompiled": ready,
        "contractVersion": "v4.7",
        "graphVersion": "dataagent-advisory-v1",
        "runtimeInstanceId": "12345678-1234-5678-9234-567812345678",
        "configurationFingerprint": "a" * 64,
        "startedAtUnixSeconds": 1_783_820_000,
    }
    return SimpleNamespace(
        compiled_graph=graph if ready else None,
        health_attestation=lambda: health,
    )


class ServerApplicationTests(unittest.TestCase):
    def setUp(self):
        self.server = load_module("server")
        self.assertTrue(
            hasattr(self.server, "SidecarApplication"),
            "server must expose a testable bounded application",
        )
        self.contracts = load_module("contracts")
        request = self.contracts.validate_handshake_request(valid_request())
        self.accepted = self.contracts.build_accepted_response(request)

    def post(self, app, body, content_type="application/json", declared_length=None):
        length = len(body) if declared_length is None else declared_length
        return app.handle(
            "POST",
            "/handshake",
            {"content-type": content_type, "content-length": str(length)},
            body,
        )

    def test_health_returns_fixed_runtime_attestation(self):
        app = self.server.SidecarApplication(runtime_state())
        result = app.handle("GET", "/health", {}, b"")
        self.assertEqual(200, result.status)
        self.assertTrue(result.body["ready"])
        self.assertEqual("v4.7", result.body["contractVersion"])

    def test_invalid_health_attestation_fails_closed(self):
        state = runtime_state()
        state.health_attestation()["runtimeInstanceId"] = "not-a-uuid"
        app = self.server.SidecarApplication(state)

        result = app.handle("GET", "/health", {}, b"")

        self.assertEqual(503, result.status)
        self.assertEqual({"error": "runtime_not_ready"}, result.body)

    def test_valid_request_invokes_graph_once(self):
        graph = FakeGraph(self.accepted)
        app = self.server.SidecarApplication(runtime_state(graph=graph))
        result = self.post(app, body_bytes(valid_request()))
        self.assertEqual(200, result.status)
        self.assertFalse(result.body["FallbackRequired"])
        self.assertEqual(1, graph.invocations)

    def test_unsupported_content_type_is_415_without_graph_invocation(self):
        graph = FakeGraph(self.accepted)
        app = self.server.SidecarApplication(runtime_state(graph=graph))
        result = self.post(app, body_bytes(valid_request()), "text/plain")
        self.assertEqual(415, result.status)
        self.assertEqual({"error": "unsupported_content_type"}, result.body)
        self.assertEqual(0, graph.invocations)

    def test_empty_malformed_and_non_object_bodies_are_400(self):
        app = self.server.SidecarApplication(runtime_state())
        for body in (b"", b"{", b"[]"):
            with self.subTest(body=body):
                result = self.post(app, body)
                self.assertEqual(400, result.status)
                self.assertEqual({"error": "invalid_request_schema"}, result.body)

    def test_oversized_body_is_413_before_graph_invocation(self):
        graph = FakeGraph(self.accepted)
        app = self.server.SidecarApplication(runtime_state(graph=graph))
        result = self.post(app, b"x" * 65537)
        self.assertEqual(413, result.status)
        self.assertEqual({"error": "request_body_too_large"}, result.body)
        self.assertEqual(0, graph.invocations)

    def test_not_ready_is_503_before_graph_invocation(self):
        app = self.server.SidecarApplication(runtime_state(ready=False))
        result = self.post(app, body_bytes(valid_request()))
        self.assertEqual(503, result.status)
        self.assertEqual({"error": "runtime_not_ready"}, result.body)

    def test_graph_failure_is_safe_fixed_500(self):
        graph = FakeGraph(error=RuntimeError("token SQL C:\\private"))
        app = self.server.SidecarApplication(runtime_state(graph=graph))
        result = self.post(app, body_bytes(valid_request()))
        self.assertEqual(500, result.status)
        self.assertEqual({"error": "graph_failure"}, result.body)
        serialized = json.dumps(result.body)
        self.assertNotIn("token", serialized)
        self.assertNotIn("private", serialized)

    def test_unsafe_graph_output_fails_closed(self):
        unsafe = dict(self.accepted)
        unsafe["NoSqlAuthority"] = False
        graph = FakeGraph(unsafe)
        app = self.server.SidecarApplication(runtime_state(graph=graph))
        result = self.post(app, body_bytes(valid_request()))
        self.assertEqual(500, result.status)
        self.assertEqual({"error": "graph_failure"}, result.body)


if __name__ == "__main__":
    unittest.main()
