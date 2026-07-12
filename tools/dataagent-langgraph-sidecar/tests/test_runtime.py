import importlib.util
import hashlib
import inspect
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace


SIDECAR_ROOT = Path(__file__).resolve().parents[1]
RUNTIME_PATH = SIDECAR_ROOT / "runtime.py"


def load_runtime_module():
    if not RUNTIME_PATH.is_file():
        raise AssertionError("runtime.py must implement the runtime contract")
    spec = importlib.util.spec_from_file_location("dataagent_sidecar_runtime", RUNTIME_PATH)
    if spec is None or spec.loader is None:
        raise AssertionError("runtime module could not be loaded")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def create_v47_runtime(runtime, **kwargs):
    parameters = inspect.signature(runtime.create_runtime).parameters
    if "instance_id_factory" not in parameters or "now_unix_seconds" not in parameters:
        raise AssertionError("create_runtime must expose V4.7 identity factories")
    return runtime.create_runtime(**kwargs)


class RuntimeTests(unittest.TestCase):
    INSTANCE_ID = "12345678-1234-5678-9234-567812345678"
    STARTED_AT = 1_783_820_000

    def test_version_attestation_uses_distribution_metadata(self):
        runtime = load_runtime_module()
        self.assertIn(
            "resolve_version",
            inspect.signature(runtime.create_runtime).parameters,
            "runtime must resolve the installed distribution version",
        )
        state = create_v47_runtime(runtime,
            import_module=lambda _name: object(),
            resolve_version=lambda name: "0.3.34" if name == "langgraph" else None,
            compile_graph=lambda _module: object(),
            instance_id_factory=lambda: self.INSTANCE_ID,
            now_unix_seconds=lambda: self.STARTED_AT,
        )
        self.assertEqual("0.3.34", state.langgraph_version)

    def test_default_mode_is_langgraph(self):
        runtime = load_runtime_module()
        self.assertEqual("langgraph", runtime.DEFAULT_RUNTIME_MODE)

    def test_missing_langgraph_fails_closed(self):
        runtime = load_runtime_module()

        def missing_import(_name):
            raise ModuleNotFoundError("not installed")

        with self.assertRaises(runtime.RuntimeStartupError) as raised:
            runtime.create_runtime(import_module=missing_import)

        self.assertEqual("runtime_dependency_unavailable", raised.exception.reason_code)
        self.assertEqual("runtime_dependency_unavailable", str(raised.exception))

    def test_incompatible_langgraph_version_fails_closed(self):
        runtime = load_runtime_module()
        module = SimpleNamespace(__version__="0.3.33")

        with self.assertRaises(runtime.RuntimeStartupError) as raised:
            runtime.create_runtime(
                import_module=lambda _name: module,
                resolve_version=lambda _name: "0.3.33",
            )

        self.assertEqual("runtime_dependency_version_mismatch", raised.exception.reason_code)

    def test_graph_compile_failure_fails_closed(self):
        runtime = load_runtime_module()
        module = SimpleNamespace(__version__="0.3.34")

        def fail_compile(_module):
            raise ValueError("sensitive compiler detail")

        with self.assertRaises(runtime.RuntimeStartupError) as raised:
            runtime.create_runtime(
                import_module=lambda _name: module,
                resolve_version=lambda _name: "0.3.34",
                compile_graph=fail_compile,
            )

        self.assertEqual("runtime_graph_compile_failed", raised.exception.reason_code)
        self.assertNotIn("sensitive", str(raised.exception))

    def test_langgraph_runtime_attests_loaded_and_compiled_graph(self):
        runtime = load_runtime_module()
        module = SimpleNamespace(__version__="0.3.34")
        compiled_graph = object()

        state = create_v47_runtime(runtime,
            import_module=lambda _name: module,
            resolve_version=lambda _name: "0.3.34",
            compile_graph=lambda loaded: compiled_graph if loaded is module else None,
            instance_id_factory=lambda: self.INSTANCE_ID,
            now_unix_seconds=lambda: self.STARTED_AT,
        )

        self.assertIs(compiled_graph, state.compiled_graph)
        self.assertTrue(
            hasattr(state, "health_attestation"),
            "runtime state must expose fixed health attestation",
        )
        self.assertEqual(
            {
                "ok": True,
                "ready": True,
                "runtimeMode": "langgraph",
                "langGraphLoaded": True,
                "langGraphVersion": "0.3.34",
                "graphCompiled": True,
                "contractVersion": "v4.7",
                "graphVersion": "dataagent-advisory-v1",
                "runtimeInstanceId": self.INSTANCE_ID,
                "configurationFingerprint": hashlib.sha256(
                    b"langgraph\n0.3.34\nv4.7\ndataagent-advisory-v1\n65536\n65536"
                ).hexdigest(),
                "startedAtUnixSeconds": self.STARTED_AT,
            },
            state.health_attestation(),
        )

    def test_deterministic_stub_is_explicit_and_never_ready(self):
        runtime = load_runtime_module()
        import_attempted = False

        def unexpected_import(_name):
            nonlocal import_attempted
            import_attempted = True
            raise AssertionError("stub must not import langgraph")

        state = create_v47_runtime(runtime,
            mode="deterministic-stub",
            import_module=unexpected_import,
            instance_id_factory=lambda: self.INSTANCE_ID,
            now_unix_seconds=lambda: self.STARTED_AT,
        )

        self.assertFalse(import_attempted)
        self.assertIsNone(state.compiled_graph)
        self.assertTrue(
            hasattr(state, "health_attestation"),
            "stub state must expose honest health attestation",
        )
        self.assertEqual(
            {
                "ok": True,
                "ready": False,
                "runtimeMode": "deterministic-stub",
                "langGraphLoaded": False,
                "langGraphVersion": None,
                "graphCompiled": False,
                "contractVersion": "v4.7",
                "graphVersion": "dataagent-advisory-v1",
                "runtimeInstanceId": self.INSTANCE_ID,
                "configurationFingerprint": hashlib.sha256(
                    b"deterministic-stub\nnone\nv4.7\ndataagent-advisory-v1\n65536\n65536"
                ).hexdigest(),
                "startedAtUnixSeconds": self.STARTED_AT,
            },
            state.health_attestation(),
        )

    def test_unknown_runtime_mode_is_rejected(self):
        runtime = load_runtime_module()

        with self.assertRaises(runtime.RuntimeStartupError) as raised:
            runtime.create_runtime(mode="automatic")

        self.assertEqual("runtime_mode_invalid", raised.exception.reason_code)

    def test_identity_is_stable_per_runtime_and_distinct_per_startup(self):
        runtime = load_runtime_module()
        identifiers = iter([
            "12345678-1234-5678-9234-567812345678",
            "87654321-4321-6789-a234-567812345678",
        ])

        first = create_v47_runtime(runtime,
            mode="deterministic-stub",
            instance_id_factory=lambda: next(identifiers),
            now_unix_seconds=lambda: self.STARTED_AT,
        )
        second = create_v47_runtime(runtime,
            mode="deterministic-stub",
            instance_id_factory=lambda: next(identifiers),
            now_unix_seconds=lambda: self.STARTED_AT + 1,
        )

        self.assertEqual(first.health_attestation(), first.health_attestation())
        self.assertNotEqual(first.runtime_instance_id, second.runtime_instance_id)

    def test_invalid_runtime_identity_and_start_time_fail_closed(self):
        runtime = load_runtime_module()
        with self.assertRaises(runtime.RuntimeStartupError) as invalid_id:
            create_v47_runtime(runtime,
                mode="deterministic-stub",
                instance_id_factory=lambda: "not-a-uuid",
                now_unix_seconds=lambda: self.STARTED_AT,
            )
        with self.assertRaises(runtime.RuntimeStartupError) as invalid_time:
            create_v47_runtime(runtime,
                mode="deterministic-stub",
                instance_id_factory=lambda: self.INSTANCE_ID,
                now_unix_seconds=lambda: 0,
            )

        self.assertEqual("runtime_identity_invalid", invalid_id.exception.reason_code)
        self.assertEqual("runtime_start_time_invalid", invalid_time.exception.reason_code)

    def test_project_metadata_pins_supported_runtime(self):
        pyproject_path = SIDECAR_ROOT / "pyproject.toml"
        lock_path = SIDECAR_ROOT / "requirements.lock"
        self.assertTrue(pyproject_path.is_file(), "pyproject.toml must be declared")
        self.assertTrue(lock_path.is_file(), "requirements.lock must be declared")
        pyproject = pyproject_path.read_text(encoding="utf-8")
        lock = lock_path.read_text(encoding="utf-8")

        self.assertIn('requires-python = ">=3.11,<3.14"', pyproject)
        self.assertIn('"langgraph==0.3.34"', pyproject)
        self.assertRegex(lock, r"(?m)^langgraph==0\.3\.34$")


if __name__ == "__main__":
    unittest.main()
