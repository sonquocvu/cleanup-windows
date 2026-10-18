# CleanSpace — System Architecture (v1)

## Architectural style
One Windows desktop executable, modular monolith with Clean Architecture boundaries and MVVM UI. Favor boring, auditable components over overengineering. No network backend, database, elevated worker, Windows Service, kernel component, background daemon or telemetry in MVP.

## Technology
- C#/.NET 10 LTS; nullable reference types; analyzers enabled.
- WinUI 3 with **stable** Windows App SDK 2.5.1 (as of 2026-10-17); pin explicit package versions after verifying current environment.
- CommunityToolkit.Mvvm for commands/observable state; Microsoft.Extensions.DependencyInjection and Microsoft.Extensions.Logging.
- xUnit for automated tests; fake/in-memory filesystem for policy tests; isolated on-disk integration fixtures for Windows-only adapters.
- JSON for minimal settings. No SQLite until concrete history/scale needs emerge.
- Windows MSIX distribution target; development runs through the installed supported Windows toolchain.

## Intended repository topology
```
CleanSpace.slnx  # or .sln if toolchain or templates require
src/
  CleanSpace.App/
    App.xaml, MainWindow.xaml, Views/, ViewModels/, Controls/, Themes/
  CleanSpace.Application/
    Abstractions/, UseCases/, DTOs/
  CleanSpace.Core/
    Models/, Rules/, Safety/
  CleanSpace.Infrastructure/
    Scanning/, Windows/, Safety/, Cleanup/, Logging/
tests/
  CleanSpace.Core.Tests/
  CleanSpace.Application.Tests/
  CleanSpace.IntegrationTests/
docs/  tasks/  AGENTS.md
```

Do not precreate empty placeholder files or modules unless they are needed for the current task.

## Dependency direction (compile-time)
```
CleanSpace.App ──> CleanSpace.Application ──> CleanSpace.Core
                      ^                         ^
                      |                         |
                CleanSpace.Infrastructure ──────┘
```
`CleanSpace.App` DI composition root may reference Infrastructure for registration. No other App-to-Infrastructure use. Core must not reference Windows libraries. Infrastructure implements Application ports.

## Logical components
1. `DriveOverviewUseCase`: asks `IDriveInfoProvider` for OS volume and usage metrics.
2. `QuickScanUseCase`: resolves an allowlisted scan scope, streams metadata from `IFileEnumerator`, validates/classifies via `ICandidatePolicy`, batches progress.
3. `CandidatePolicy`: pure decision from trusted scope provenance, file metadata and classification rules. A policy decision is **not** permission to delete.
4. `BuildCleanupPlanUseCase`: later creates a short-lived selection of candidate IDs from the scan session and requires explicit user confirmation.
5. `ExecuteCleanupUseCase`: not implemented until separately approved; must call OS-backed safety revalidation and a tightly bounded adapter.

## Ports/contracts (interfaces, not mandatory exact signatures)
- `IDriveInfoProvider.GetSystemDriveAsync(CancellationToken)` -> `DriveOverview`.
- `IScanScopeProvider.GetApprovedScopes()` -> validated scope descriptors.
- `IFileEnumerator.EnumerateAsync(ScanScope, CancellationToken)` -> streamed metadata/results.
- `ICandidatePolicy.Evaluate(TrustedScanContext, FileMetadata)` -> `SafetyDecision` + rationale.
- `IScanService.ScanAsync(ScanRequest, IProgress<ScanProgress>, CancellationToken)` -> `ScanSessionSummary`, with a bounded candidate collection/read-only results cache.
- `ICleanupExecutor` -> **future only**, no actual implementation during read-only milestones.

## Data contracts
`DriveOverview`: volume root, total bytes, used bytes, available/free bytes, fetch time.
`ScanScope`: canonical root, trusted source type, volume descriptor and rule-set identifier.
`FileMetadata`: display path, parent-relative path, logical length, modified UTC, attributes, optional verified volume/file identity, optional link count, optional placeholder flags.
`ScanCandidate`: opaque candidate ID, session ID, metadata snapshot, source scope ID, rule ID, decision, rationale; not a durable delete authorization.
`ScanSessionSummary`: started/completed UTC, inspected/skipped/error counts, candidate count, candidate logical bytes; scan cancellation state.
`CleanupPlan`: user selections plus short expiry; generated and reviewed independently from scan results.

## Scan flow

**Current architecture decision:** [ADR-003](ADR-003-QUICK-SCAN-SAFETY.md) retains the accepted production UI mitigation. The TASK-003 scanner and tests are preserved, but user-facing scanning is **BLOCKED**. Production DI explicitly passes `scanAvailable: false`; both scan command paths guard execution, and navigation alone does not dispatch a scan. There is no user-facing enabling switch. The flow below describes intended scanning behavior, not an active approved feature. No long-lived restrictive, short-lease or fully shared strategy is approved for user-facing operation. Re-enablement requires a separately approved safe design and the ADR's evidence gates; trusted-scope relaxation requires explicit product/security approval before implementation. TASK-004 and TASK-005 remain **NOT AUTHORIZED**.

`Dashboard -> QuickScanUseCase -> ScanScopeProvider -> Enumerate -> CandidatePolicy -> batched progress + ScanResultsViewModel`.

Only an approved user-scoped Temp directory is in v0.1 scope. Do not enumerate entire system drive. Validate the scope root and every entry; reject symlinks, mount points, junctions, special reparse points, off-volume entries and cloud placeholders. Skip inaccessible nodes and log counts. No automatic permission changes. Scan only metadata, not contents. Limit UI notifications (e.g. batch every 100 records or 100–250ms), but choose actual values after measurement. Stop on cancellation and keep partial results clearly labeled.

## Concurrency and state
- Single active scan session; cancellable, serialized per session.
- UI updates occur on UI dispatcher; filesystem enumeration runs off UI thread.
- State model: `Idle -> Scanning -> Completed | Cancelled | Failed`; later `Reviewing -> Confirming -> Cleaning -> Completed | Partial | Cancelled | Failed`.
- No cleanup while scan is active. A new scan invalidates previous candidate IDs and pending plans.

## UI screen boundaries
- Dashboard: system volume cards + scan CTA.
- Scan Results: category totals, candidate list, risk/reason, skipped counts, Cancel while scanning.
- Cleanup: placeholder/read-only until security release gate. Never show a clickable button that performs an unapproved operation.
- Settings: theme; performance tuning defaults chosen conservatively. No auto clean.

## Cleanup adapter notes for later releases
`IFileOperation` runs on an STA thread and may recycle files with suitable flags. It is path-oriented: validation followed by a path-based call can have a TOCTOU race. The design must investigate whether the exact validated object can be guaranteed to be the object acted upon, or otherwise constrain/reject uncertain cases. Do not enable production cleanup just because a pre-delete metadata recheck passes.

Recycle Bin changes do not equate to space reclaimed. If cleanup is later approved, report per-file outcomes and distinguish potential logical bytes vs moved-to-Recycle-Bin vs actual volume free-space delta; do not promise deltas equal file sizes (sparse/compressed/hardlinked data, concurrent disk activity).

## Cross-cutting requirements
- Local-first privacy; no telemetry or external storage.
- Structured logging with redacted paths by default.
- No auto-updater or background indexing in MVP.
- Unit tests validate business rules; Windows integration tests exercise real OS boundary only in test-owned paths.
- Keep architecture decisions recorded in docs on material changes; no unilateral scope expansion.
