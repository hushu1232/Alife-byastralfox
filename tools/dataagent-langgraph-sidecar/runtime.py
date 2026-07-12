from dataclasses import dataclass
from importlib import import_module as _import_module
from importlib.metadata import version as _distribution_version
from typing import Callable, Optional


DEFAULT_RUNTIME_MODE = "langgraph"
PINNED_LANGGRAPH_VERSION = "0.3.34"


class RuntimeStartupError(RuntimeError):
    def __init__(self, reason_code: str):
        self.reason_code = reason_code
        super().__init__(reason_code)


@dataclass(frozen=True)
class RuntimeState:
    mode: str
    langgraph_version: Optional[str]
    compiled_graph: object | None

    def health_attestation(self) -> dict[str, object]:
        graph_compiled = self.compiled_graph is not None
        langgraph_loaded = self.langgraph_version is not None
        return {
            "ok": True,
            "ready": self.mode == "langgraph" and langgraph_loaded and graph_compiled,
            "runtimeMode": self.mode,
            "langGraphLoaded": langgraph_loaded,
            "langGraphVersion": self.langgraph_version,
            "graphCompiled": graph_compiled,
            "contractVersion": "v4.6",
            "graphVersion": "dataagent-advisory-v1",
        }


def _default_compile_graph(_langgraph_module):
    graph_module = _import_module("graph")
    return graph_module.compile_advisory_graph()


def create_runtime(
    mode: str = DEFAULT_RUNTIME_MODE,
    *,
    import_module: Callable[[str], object] = _import_module,
    resolve_version: Callable[[str], str] = _distribution_version,
    compile_graph: Callable[[object], object] = _default_compile_graph,
) -> RuntimeState:
    if mode not in ("langgraph", "deterministic-stub"):
        raise RuntimeStartupError("runtime_mode_invalid")

    if mode == "deterministic-stub":
        return RuntimeState(mode=mode, langgraph_version=None, compiled_graph=None)

    try:
        langgraph_module = import_module("langgraph")
    except (ImportError, ModuleNotFoundError):
        raise RuntimeStartupError("runtime_dependency_unavailable") from None

    try:
        version = resolve_version("langgraph")
    except Exception:
        raise RuntimeStartupError("runtime_dependency_version_unavailable") from None
    if version != PINNED_LANGGRAPH_VERSION:
        raise RuntimeStartupError("runtime_dependency_version_mismatch")

    try:
        compiled_graph = compile_graph(langgraph_module)
    except Exception:
        raise RuntimeStartupError("runtime_graph_compile_failed") from None

    if compiled_graph is None:
        raise RuntimeStartupError("runtime_graph_compile_failed")

    return RuntimeState(
        mode=mode,
        langgraph_version=version,
        compiled_graph=compiled_graph,
    )
