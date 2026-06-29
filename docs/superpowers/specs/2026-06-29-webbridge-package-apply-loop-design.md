# WebBridge Package Apply Loop Design

## Goal

Complete the Alife .NET 9 side of the WebBridge local confirmation loop. After a package is already staged, a local Alife action can apply the staged config draft, update the local WebBridge catalog to `applied`, and report `packageApplied` to FOXD Web.

## Scope

This change stays inside `Alife.Function.WebBridge` and focused tests. It does not start the live Alife runtime, does not touch default Alife storage during tests, does not change Unity code, and does not add a Web-side activation button.

## Architecture

`WebBridgePackageInstaller` remains responsible for local package files, catalog, and draft persistence. It gains an explicit apply method that reads `catalog.json`, validates a staged package, copies its config draft to a stable active WebBridge config file, marks the catalog record as `applied`, and returns a `WebBridgeInstallResult`.

`WebBridgeService` gains a local confirmation entry point that calls the installer apply method. If local apply succeeds, it reports `packageApplied` to `/api/pet/sync/status`. If local apply fails, it reports `packageFailed` with the same error mapping used by install, then rethrows the original exception.

## Data Flow

1. `InstallPackage(packageId)` keeps the current staging flow and stops at `pendingActivation`.
2. Local Alife confirmation calls `ApplyPackage(packageId)`.
3. The installer loads `catalog.json`, finds the package record, and requires `pendingActivation`.
4. The installer validates that `ConfigDraftPath` still exists.
5. The draft is copied to `ActiveConfig/<packageId>.json` under the configured WebBridge package root.
6. The catalog record status changes to `applied`.
7. The service posts `packageApplied` with the package version from the stored manifest.

## Error Handling

Missing catalog, missing package record, non-pending package status, missing config draft, and invalid manifest JSON are local apply failures. They must not mark the package as `applied`. The service should best-effort report `packageFailed` and preserve the original exception for the local caller.

Milestone reporting remains best-effort. A failed `packageApplied` POST does not roll back a successful local apply.

## Testing

Focused NUnit tests cover:

- Installer apply updates the active config file and catalog status.
- Installer apply rejects missing config drafts without changing catalog status.
- Service apply reports `packageApplied`.
- Service apply reports `packageFailed` when the local apply step fails.

Verification uses the user-local .NET 9 SDK at `C:\Users\hu shu\.dotnet\dotnet.exe`.
