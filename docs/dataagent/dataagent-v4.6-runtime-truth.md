# DataAgent V4.6 Runtime Truth

```text
runtime_truth=v4.6
source_baseline=v4.5
runtime_mode=langgraph
stub_mode=deterministic-stub
stub_production_ready=false
langgraph_version=0.3.34
contract_version=v4.6
graph_version=dataagent-advisory-v1
graph_compile_count_per_startup=1
request_body_max_bytes=65536
response_body_max_bytes=65536
live_smoke_count=5
live_smoke_health=true
live_smoke_valid_advisory=true
live_smoke_malformed_json=true
live_smoke_oversized_request=true
live_smoke_unsupported_content_type=true
loopback_only=true
starts_runtime=false
installs_dependencies=false
default_enabled=false
agent_advisory_only=true
csharp_validation_authority=true
allows_execution=false
allows_state_write=false
allows_checkpoint_mutation=false
allows_visible_text=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false
production_closure_complete=false
next_gate=v4.7_live_canary
```

V4.6 makes runtime identity and readiness observable and fail-closed. `langgraph` is the default production-canary mode; dependency absence, version mismatch, graph compilation failure, malformed requests, unsafe graph output, and oversized bodies cannot fabricate an accepted response. The developer stub is explicit and never ready.

The operator prepares Python 3.11-3.13, installs `requirements.lock`, starts the loopback process, runs the five-check manual smoke, and owns shutdown. Automated tests do not install dependencies, bind ports, contact external networks, or operate QQ/NapCat.

The manual smoke proves only health attestation, one valid advisory, malformed JSON 400, oversized request 413, and unsupported content type 415. Timeout, authority rejection, saturation, circuit recovery, and kill switch remain automated evidence until V4.7 executes the seven live drills.

V4.7 may begin only after an operator-run health and valid-advisory artifact proves the sidecar is actually running in `langgraph` mode. V4.7 owns the 20-request observation window, seven live fault drills, runtime instance identity, configuration fingerprint, aggregate persistence, and production-closure artifact.
