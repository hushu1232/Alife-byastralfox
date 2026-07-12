import hashlib
import time
import uuid
from dataclasses import dataclass
from importlib import import_module as _import_module
from importlib.metadata import version as _distribution_version
from typing import Callable, Optional


DEFAULT_RUNTIME_MODE = "langgraph"
PINNED_LANGGRAPH_VERSION = "0.3.34"
CONTRACT_VERSION = "v4.7"
GRAPH_VERSION = "dataagent-advisory-v1"
REQUEST_BODY_MAX_BYTES = 65536
RESPONSE_BODY_MAX_BYTES = 65536


class RuntimeStartupError(RuntimeError):
    def __init__(self, reason_code: str):
        self.reason_code = reason_code
        super().__init__(reason_code)


@dataclass(frozen=True)
class RuntimeState:
    mode: str
    langgraph_version: Optional[str]
    compiled_graph: object | None
    runtime_instance_id: str
    configuration_fingerprint: str
    started_at_unix_seconds: int

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
            "contractVersion": CONTRACT_VERSION,
            "graphVersion": GRAPH_VERSION,
            "runtimeInstanceId": self.runtime_instance_id,
            "configurationFingerprint": self.configuration_fingerprint,
            "startedAtUnixSeconds": self.started_at_unix_seconds,
        }


def _default_compile_graph(_langgraph_module):
    graph_module = _import_module("graph")
    return graph_module.compile_advisory_graph()


def _new_instance_id() -> str:
    return str(uuid.uuid4())


def _now_unix_seconds() -> int:
    return int(time.time())


def _configuration_fingerprint(mode: str, langgraph_version: Optional[str]) -> str:
    canonical = "\n".join((
        mode,
        langgraph_version or "none",
        CONTRACT_VERSION,
        GRAPH_VERSION,
        str(REQUEST_BODY_MAX_BYTES),
        str(RESPONSE_BODY_MAX_BYTES),
    ))
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def create_runtime(
    mode: str = DEFAULT_RUNTIME_MODE,
    *,
    import_module: Callable[[str], object] = _import_module,
    resolve_version: Callable[[str], str] = _distribution_version,
    compile_graph: Callable[[object], object] = _default_compile_graph,
    instance_id_factory: Callable[[], str] = _new_instance_id,
    now_unix_seconds: Callable[[], int] = _now_unix_seconds,
) -> RuntimeState:
    if mode not in ("langgraph", "deterministic-stub"):
        raise RuntimeStartupError("runtime_mode_invalid")

    try:
        runtime_instance_id = instance_id_factory()
        parsed_instance_id = uuid.UUID(runtime_instance_id)
    except (AttributeError, TypeError, ValueError):
        raise RuntimeStartupError("runtime_identity_invalid") from None
    if str(parsed_instance_id) != runtime_instance_id:
        raise RuntimeStartupError("runtime_identity_invalid")

    try:
        started_at_unix_seconds = now_unix_seconds()
    except Exception:
        raise RuntimeStartupError("runtime_start_time_invalid") from None
    if (isinstance(started_at_unix_seconds, bool)
            or not isinstance(started_at_unix_seconds, int)
            or started_at_unix_seconds <= 0):
        raise RuntimeStartupError("runtime_start_time_invalid")

    if mode == "deterministic-stub":
        return RuntimeState(
            mode=mode,
            langgraph_version=None,
            compiled_graph=None,
            runtime_instance_id=runtime_instance_id,
            configuration_fingerprint=_configuration_fingerprint(mode, None),
            started_at_unix_seconds=started_at_unix_seconds,
        )

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
        runtime_instance_id=runtime_instance_id,
        configuration_fingerprint=_configuration_fingerprint(mode, version),
        started_at_unix_seconds=started_at_unix_seconds,
    )
