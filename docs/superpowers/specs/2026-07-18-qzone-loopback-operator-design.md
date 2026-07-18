# QZone Per-Character Loopback Operator Design

**Status:** Approved design, awaiting written-spec review
**Scope:** Deliver the final local execution bridge from `Test-QZoneRealRuntime.ps1` to each already-running character instance's `QZoneService`.

## Decision

Each Alife character instance hosts its own QZone Operator endpoint on loopback only. The endpoint belongs to the instance that owns its OneBot connection and `QZoneService`; it never shares a QZone session, Cookie, BKN, runtime object, or role identity with the other character.

No third operator process is introduced. DataAgent and LangGraph receive no route, credential, scheduling, execution, or endpoint authority.

## Addressing and lifecycle

The local supervisor supplies an operator base URL for each existing character process. It must be an absolute `http://127.0.0.1:<port>/` or `http://localhost:<port>/` URL; non-loopback hosts are rejected before binding or calling.

The endpoint starts and stops with that character process. A missing operator endpoint is reported as `local_qzone_runtime_unavailable`; scripts do not start the character, NapCat, or QQ process themselves.

The operator request contains an operation and safe parameters only. It never contains or returns Cookie, BKN, OneBot token, LLM key, prompt, raw QZone body, or image bytes.

## Operations

Supported loopback operations are `Read`, `Post`, `Comment`, `Like`, `Image`, and `Delete`.

- `Read` calls the character's configured `QZoneService` read path.
- `Post`, `Comment`, `Like`, and `Image` invoke the existing service operations.
- `Delete` invokes only `QZoneDeleteOwnPost` with the full metadata from the character's own test post. The existing QZone runtime independently validates current-session ownership.

The operator returns a compact success/failure result made from safe service result fields. It never claims completion unless the underlying service reports completion.

## Authority and safety boundary

The endpoint is loopback-only and is operator-triggered. The PowerShell script remains inert unless `-Execute` is specified. An `-Execute` call forwards only to the matching local role endpoint; it does not construct QZone HTTP requests or retrieve a NapCat Cookie itself.

Real publishing remains controlled by existing QZone configuration. Automatic publication remains disabled until the separate two-account matrix passes.

## Verification

1. Unit-test loopback URL validation, port-to-role selection, safe request/result mapping, and rejection of non-loopback URLs.
2. Preserve the current inert script test and add a fake local operator listener test proving `-Execute` forwards only safe JSON to the selected endpoint.
3. Start both existing local role instances, invoke `Read` once per account through the script, then execute the existing once-per-account matrix: unique text post, comment or like, local image post, and delete of own test content.
4. Verify no configuration, logs, audit records, Git files, or command output contain Cookie, BKN, tokens, API keys, or image bytes.

## Non-goals

- No browser automation, external host binding, third operator process, or automatic QQ/NapCat startup.
- No target whitelist and no autonomous remote-image collection. Domain URL whitelisting remains a future feature before any autonomous remote retrieval.
- No DataAgent or LangGraph integration.
