import importlib.util
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def load_module(name):
    path = ROOT / f"{name}.py"
    if not path.is_file():
        raise AssertionError(f"{name}.py must implement the contract")
    if str(ROOT) not in sys.path:
        sys.path.insert(0, str(ROOT))
    spec = importlib.util.spec_from_file_location(f"dataagent_{name}", path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def valid_request():
    return {
        "RequestId": "request-1",
        "SessionId": "session-1",
        "TurnId": "turn-1",
        "CallerId": "caller-1",
        "GoalOrQuestion": "summarize controlled data",
        "ScenarioContextSummary": "bounded scenario",
        "RouteScope": "owner-only",
        "QueryConstraints": "read-only",
        "NodeManifests": [{
            "NodeName": "query_planner",
            "Purpose": "advisory plan",
            "AllowedToolNames": ["dataagent.query_plan.propose"],
            "DeniedCapabilityMarkers": ["dataagent.query.execute_readonly"],
            "InputShape": "goal",
            "OutputShape": "candidate",
            "BusinessTerms": ["dataset"],
            "SafetyNotes": "candidate only",
        }],
        "NoSqlAuthority": True,
        "ReadOnly": True,
        "FallbackAvailable": True,
        "TraceBudgetChars": 1800,
        "ProgressBudget": 16,
    }


class ContractTests(unittest.TestCase):
    def test_v47_health_contract_rejects_forged_or_unknown_fields(self):
        contracts = load_module("contracts")
        self.assertTrue(
            hasattr(contracts, "validate_health_attestation"),
            "contracts must expose strict V4.7 health validation",
        )
        health = {
            "ok": True,
            "ready": True,
            "runtimeMode": "langgraph",
            "langGraphLoaded": True,
            "langGraphVersion": "0.3.34",
            "graphCompiled": True,
            "contractVersion": "v4.7",
            "graphVersion": "dataagent-advisory-v1",
            "runtimeInstanceId": "12345678-1234-5678-9234-567812345678",
            "configurationFingerprint": "a" * 64,
            "startedAtUnixSeconds": 1_783_820_000,
        }
        self.assertEqual(health, contracts.validate_health_attestation(health))

        mutations = []
        for field, value in (
            ("runtimeInstanceId", "not-a-uuid"),
            ("configurationFingerprint", "ABC"),
            ("startedAtUnixSeconds", 0),
        ):
            candidate = dict(health)
            candidate[field] = value
            mutations.append(candidate)
        stub_ready = dict(health, runtimeMode="deterministic-stub")
        mutations.append(stub_ready)
        mutations.append(dict(health, endpoint="http://secret"))

        for candidate in mutations:
            with self.subTest(candidate=candidate):
                with self.assertRaises(contracts.ContractError):
                    contracts.validate_health_attestation(candidate)

    def test_valid_request_is_normalized_without_authority_expansion(self):
        contracts = load_module("contracts")
        request = contracts.validate_handshake_request(valid_request())
        self.assertEqual("request-1", request["RequestId"])
        self.assertEqual("query_planner", request["NodeManifests"][0]["NodeName"])

    def test_non_object_and_unknown_fields_are_rejected(self):
        contracts = load_module("contracts")
        for value in ([], "request", None):
            with self.subTest(value=value):
                with self.assertRaises(contracts.ContractError):
                    contracts.validate_handshake_request(value)
        request = valid_request()
        request["SqlAuthority"] = True
        with self.assertRaises(contracts.ContractError):
            contracts.validate_handshake_request(request)

    def test_unsafe_authority_and_unknown_node_are_rejected(self):
        contracts = load_module("contracts")
        for field, value in (("NoSqlAuthority", False), ("ReadOnly", False), ("FallbackAvailable", False)):
            request = valid_request()
            request[field] = value
            with self.subTest(field=field):
                with self.assertRaises(contracts.ContractError):
                    contracts.validate_handshake_request(request)
        request = valid_request()
        request["NodeManifests"][0]["NodeName"] = "execute_sql"
        with self.assertRaises(contracts.ContractError):
            contracts.validate_handshake_request(request)

    def test_accepted_response_has_fixed_safe_authority(self):
        contracts = load_module("contracts")
        response = contracts.build_accepted_response(
            contracts.validate_handshake_request(valid_request())
        )
        self.assertTrue(response["Accepted"])
        self.assertFalse(response["FallbackRequired"])
        self.assertTrue(response["NoSqlAuthority"])
        self.assertTrue(response["ReadOnly"])
        self.assertFalse(response["RequestsCheckpointMutation"])
        self.assertFalse(response["RequestsVisibleText"])
        self.assertEqual([], response["RequestedToolNames"])


if __name__ == "__main__":
    unittest.main()
