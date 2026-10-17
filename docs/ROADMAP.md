# CleanSpace — Delivery Roadmap

## Task workflow
Each task is bounded, reviewed and approved individually. Codex reports: changed files, exact build/test commands and observed results, UI state if tested, safety impact, unresolved blockers. Do not start next task without approval.

## v0.1 — Read-only Analyzer

### Task 001 — Foundation
Create solution and project references, WinUI 3 shell/navigation, basic theme persistence, DI, test scaffolds. **No filesystem enumeration or cleanup.**
Exit: Windows build and test results reported honestly; app opens with navigation and theme switching.

### Task 002 — Drive Overview
Read OS drive capacity via Windows/.NET APIs; dashboard cards; error handling and tests. **Read-only.**
Exit: OS drive discovered correctly without assuming C:; values are tested or manual-check instructions reported.

### Task 003 — Quick Scan
Enumerate only verified user temp scope, metadata and progress/cancel, candidate list with conservative status. **Read-only.**
Exit: symlink/junction entries skipped, permissions failures do not crash, UI responsive, cancellation works, no writes to target data.

## v0.2 — Safety Engine

### Task 004 — Policy and Adversarial Tests
Pure domain rules + OS identity ports; candidate decision and rationale; test matrix with fake filesystem plus Windows integration fixtures. **Still read-only.**
Exit: unit and integration evidence, rejected adversarial fixtures, untrusted paths fail closed.

## Conditional v0.3 — Controlled Cleanup

### Task 005 — Recycle Safety Research
Read-only design spike with synthetic sandbox experiments where individually approved, audit Win32/Shell interactions, propose safe options, validate failure modes. No production cleanup in app.
Exit: reviewed decision record with unresolved TOCTOU treatment and go/no-go recommendation.

### Task 006 — Recycle Implementation (approval gate)
**Blocked until explicit human authorization after Task 005 security review.** If approved, implement user-confirmed, per-file recycle-only action with strict validation and auditing. No permanent delete, admin or recursive directories.
Exit: accepted risk, tests on disposable fixtures, correct per-file and space reporting, independent review.

## Deferred
Treemap/deep scan, duplicate file detection, browser cache cleanup, Windows Update integration, scheduled reports, telemetry (opt-in only if ever desired), permanent deletion, Store publication.

## Performance and reliability goals
- Never block WinUI main thread with enumeration or long file operations.
- One active scan session; cancellation, graceful skipped/error results.
- Bound memory via incremental enumeration/batched UI updates.
- Empirical budgets for wall time/RAM/I/O are set **after** measurements on real devices; do not claim unsupported benchmarks.

## Definition of Done — v0.1
- App launches as normal user, supports light/dark/system.
- Displays actual Windows volume usage (not assumed drive letter).
- Can scan limited user Temp in read-only manner, display rationale and skip counts, and cancel.
- No cleanup execution path compiled into the shipped app.
- Policy and test constraints are documented; build/test proof included.
- No forced elevation, service, background autorun or OS setting modification.
