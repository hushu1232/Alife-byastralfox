import importlib.util
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


class RuntimeTests(unittest.TestCase):
    def test_version_attestation_uses_distribution_metadata(self):
        runtime = load_runtime_module()
        self.assertIn(
            "resolve_version",
            inspect.signature(runtime.create_runtime).parameters,
            "runtime must resolve the installed distribution version",
        )
        state = runtime.create_runtime(
            import_module=lambda _name: object(),
            resolve_version=lambda name: "0.3.34" if name == "langgraph" else None,
            compile_graph=lambda _module: object(),
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

        state = runtime.create_runtime(
            import_module=lambda _name: module,
            resolve_version=lambda _name: "0.3.34",
            compile_graph=lambda loaded: compiled_graph if loaded is module else None,
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
                "contractVersion": "v4.6",
                "graphVersion": "dataagent-advisory-v1",
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

        state = runtime.create_runtime(
            mode="deterministic-stub",
            import_module=unexpected_import,
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
                "contractVersion": "v4.6",
                "graphVersion": "dataagent-advisory-v1",
            },
            state.health_attestation(),
        )

    def test_unknown_runtime_mode_is_rejected(self):
        runtime = load_runtime_module()

        with self.assertRaises(runtime.RuntimeStartupError) as raised:
            runtime.create_runtime(mode="automatic")

        self.assertEqual("runtime_mode_invalid", raised.exception.reason_code)

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
