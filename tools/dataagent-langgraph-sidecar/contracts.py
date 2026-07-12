from typing import Any


MAX_BODY_BYTES = 65536
KNOWN_NODE_NAMES = frozenset({
    "route_gate", "scenario_knowledge", "query_planner",
    "query_plan_validator", "sql_safety", "read_only_execute",
    "result_explainer", "checkpoint_progress", "diagnostics_router",
    "terminal", "reject",
})

REQUEST_FIELDS = frozenset({
    "RequestId", "SessionId", "TurnId", "CallerId", "GoalOrQuestion",
    "ScenarioContextSummary", "RouteScope", "QueryConstraints",
    "NodeManifests", "NoSqlAuthority", "ReadOnly", "FallbackAvailable",
    "TraceBudgetChars", "ProgressBudget",
})
MANIFEST_FIELDS = frozenset({
    "NodeName", "Purpose", "AllowedToolNames", "DeniedCapabilityMarkers",
    "InputShape", "OutputShape", "BusinessTerms", "SafetyNotes",
})
RESPONSE_FIELDS = frozenset({
    "RequestId", "Accepted", "ReasonCode", "SelectedNodes", "NodeProgress",
    "TraceSummary", "ContextContribution", "FallbackRequired",
    "NoSqlAuthority", "ReadOnly", "RequestedToolNames",
    "RequestsCheckpointMutation", "RequestsVisibleText",
})


class ContractError(ValueError):
    def __init__(self, reason_code: str = "invalid_request_schema"):
        self.reason_code = reason_code
        super().__init__(reason_code)


def _bounded_string(value: Any, maximum: int) -> bool:
    return isinstance(value, str) and 0 < len(value) <= maximum


def _string_list(value: Any, maximum: int) -> bool:
    return (
        isinstance(value, list)
        and len(value) <= maximum
        and all(_bounded_string(item, 256) for item in value)
    )


def validate_handshake_request(value: Any) -> dict[str, Any]:
    if not isinstance(value, dict) or frozenset(value) != REQUEST_FIELDS:
        raise ContractError()
    limits = {
        "RequestId": 128, "SessionId": 128, "TurnId": 128,
        "CallerId": 128, "GoalOrQuestion": 2048,
        "ScenarioContextSummary": 4096, "RouteScope": 512,
        "QueryConstraints": 1024,
    }
    if any(not _bounded_string(value[field], limit) for field, limit in limits.items()):
        raise ContractError()
    if value["NoSqlAuthority"] is not True or value["ReadOnly"] is not True:
        raise ContractError("unsafe_authority")
    if value["FallbackAvailable"] is not True:
        raise ContractError("fallback_unavailable")
    if not isinstance(value["TraceBudgetChars"], int) or not 1 <= value["TraceBudgetChars"] <= 1800:
        raise ContractError()
    if not isinstance(value["ProgressBudget"], int) or not 1 <= value["ProgressBudget"] <= 16:
        raise ContractError()
    manifests = value["NodeManifests"]
    if not isinstance(manifests, list) or not 1 <= len(manifests) <= 16:
        raise ContractError()
    for manifest in manifests:
        if not isinstance(manifest, dict) or frozenset(manifest) != MANIFEST_FIELDS:
            raise ContractError()
        if manifest["NodeName"] not in KNOWN_NODE_NAMES:
            raise ContractError("unknown_node_manifest")
        for field in ("Purpose", "InputShape", "OutputShape", "SafetyNotes"):
            if not _bounded_string(manifest[field], 1024):
                raise ContractError()
        if not _string_list(manifest["AllowedToolNames"], 8):
            raise ContractError()
        if not _string_list(manifest["DeniedCapabilityMarkers"], 16):
            raise ContractError()
        if not _string_list(manifest["BusinessTerms"], 16):
            raise ContractError()
    return value


def build_accepted_response(request: dict[str, Any]) -> dict[str, Any]:
    selected_node = request["NodeManifests"][0]["NodeName"]
    return {
        "RequestId": request["RequestId"],
        "Accepted": True,
        "ReasonCode": "langgraph_advisory_accepted",
        "SelectedNodes": [selected_node],
        "NodeProgress": [{
            "NodeName": selected_node,
            "Status": "Completed",
            "ReasonCode": "advisory_only",
            "Message": "Advisory graph completed without authority transfer.",
            "Facts": {"authority": "csharp"},
        }],
        "TraceSummary": "Advisory graph completed; C# remains authority.",
        "ContextContribution": "sidecar=langgraph;authority=csharp",
        "FallbackRequired": False,
        "NoSqlAuthority": True,
        "ReadOnly": True,
        "RequestedToolNames": [],
        "RequestsCheckpointMutation": False,
        "RequestsVisibleText": False,
    }


def validate_handshake_response(value: Any) -> dict[str, Any]:
    if not isinstance(value, dict) or frozenset(value) != RESPONSE_FIELDS:
        raise ContractError("invalid_response_schema")
    if value["Accepted"] is not True or value["FallbackRequired"] is not False:
        raise ContractError("invalid_response_schema")
    if value["NoSqlAuthority"] is not True or value["ReadOnly"] is not True:
        raise ContractError("unsafe_response_authority")
    if value["RequestsCheckpointMutation"] is not False:
        raise ContractError("unsafe_response_authority")
    if value["RequestsVisibleText"] is not False or value["RequestedToolNames"] != []:
        raise ContractError("unsafe_response_authority")
    if not _bounded_string(value["RequestId"], 128):
        raise ContractError("invalid_response_schema")
    if not _bounded_string(value["ReasonCode"], 128):
        raise ContractError("invalid_response_schema")
    if not _string_list(value["SelectedNodes"], 16):
        raise ContractError("invalid_response_schema")
    if not isinstance(value["NodeProgress"], list) or len(value["NodeProgress"]) > 16:
        raise ContractError("invalid_response_schema")
    if not isinstance(value["TraceSummary"], str) or len(value["TraceSummary"]) > 1800:
        raise ContractError("invalid_response_schema")
    if not isinstance(value["ContextContribution"], str) or len(value["ContextContribution"]) > 1200:
        raise ContractError("invalid_response_schema")
    return value
