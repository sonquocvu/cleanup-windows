# TASK-002 — Drive Overview (read-only)

**Current status: ACCEPTED.** This specification is retained for reference; it does not authorize new work. See the [current roadmap](../docs/ROADMAP.md) and [ADR-003 feature freeze](../docs/ADR-003-QUICK-SCAN-SAFETY.md).

## Objective
Detect the actual Windows system volume and show used/available/total disk space on Dashboard.

## Implementation scope
- Add `IDriveInfoProvider` port, `DriveOverviewUseCase` and Infrastructure Windows provider.
- Find Windows volume from trusted OS path/APIs, not hard-coded `C:`.
- Account for drive-not-ready and capacity query failures.
- Add ViewModel loading/error states and UI cards + simple visual capacity bar.
- Add unit tests using fake drive provider. Optionally compare with Windows Explorer manually.

## Out of scope
No scan, enumeration, cleanup, arbitrary drive selection, disk configuration changes or elevation.

## Acceptance
Verified unit tests; no change to filesystem beyond app-local optional settings; UI remains responsive; precise byte formatting and no negative metrics; observed build/test output; wait for approval.
