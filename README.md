# CleanSpace — Codex Handoff Blueprint

CleanSpace is a safety-first Windows desktop app for disk space analysis and carefully gated cleanup.

**Current status:** TASK-001 and TASK-002 are **ACCEPTED**. TASK-003's scanner is implemented, but user-facing scanning remains **BLOCKED**; its production UI mitigation is accepted. Quick Scan stays disabled. TASK-004 and TASK-005 are **NOT AUTHORIZED**. The accepted [ADR-003 safety decision](docs/ADR-003-QUICK-SCAN-SAFETY.md) records why and the separate approvals required before re-enablement; see the [roadmap](docs/ROADMAP.md) for current task status.

The latest [REWORK-02 evidence](docs/TASK-003-REWORK-02-REPORT.md) records a clean build, 169 passing tests, three production startup smoke tests and real WinUI disablement/navigation/theme checks. These are existing results, not a new verification run or approval of production scanning. The earlier startup failure was resolved in [TASK-001-FIX-06](docs/TASK-001-FIX-06-REPORT.md); historical reports remain unchanged.

## Build and test on Windows

The verified environment is Windows x64 build 10.0.26300, .NET SDK 10.0.302, MSBuild 18.6.11, and .NET runtime 10.0.10. `global.json` selects the .NET 10.0.302 SDK feature band and allows stable patch updates. Visual Studio, an installed WinUI template, and a machine-wide Windows SDK were not found at the inspected locations. Package restore provided Windows App SDK 2.5.1 and Windows SDK build tools 10.0.26100.4654; the CLI compiled the WinUI application successfully.

Run these commands in PowerShell from the repository root:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
dotnet restore CleanSpace.slnx --disable-parallel --verbosity minimal -m:1
dotnet build CleanSpace.slnx --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet test CleanSpace.slnx --no-build --no-restore --verbosity normal -m:1
git diff --check
```

Use a normal, non-administrator Windows terminal. Earlier verification encountered sandbox restrictions on NuGet feed access and test-host communication; authorized restore/tests succeeded outside that sandbox without Administrator/UAC elevation. Historical audit commands and results are recorded in the task reports. This documentation-only decision does not rerun auditing, builds or tests.

The development application is unpackaged and carries the Windows App SDK runtime beside its executable. It still uses the installed .NET 10 runtime. No runtime installer, MSIX installation, signing, publication, or Windows configuration change is part of TASK-001. The launch command is:

```powershell
& './src/CleanSpace.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/CleanSpace.App.exe'
```

The accepted mitigation retains Drive Overview, Dashboard/Scan Results/Settings navigation and themes. Dashboard Quick Scan and Scan Results Start remain disabled with a temporary-unavailability notice; Cleanup remains disabled. Existing production runtime evidence is in REWORK-02. A successful build or startup alone does not approve scanning. New scanner designs, native experiments and re-enablement require separate authorization under ADR-003; do not run the production scanner against user Temp as part of this feature freeze.

The only intended application data write is its own appearance preference at `%LOCALAPPDATA%/CleanSpace/settings.json`. First launch defaults to System. Missing settings are normal; malformed or unreadable settings fall back to System with a path-free notice. A failed save keeps the previously active theme. Drive Overview reads Windows-volume metadata. The scanner implementation and tests are retained for review, with user-facing entry points disabled. No cleanup implementation is authorized.

Integration fixtures are uniquely named beneath `tests/CleanSpace.IntegrationTests/bin/Debug/net10.0/fixtures/theme-settings/`. They never use real user settings or Temp, make no permission changes, and are retained without deletion. The adapter's internal directory-injection constructor exists only for isolated testing; normal application DI continues to use the same public, fixed-location constructor.

## Working agreement

- Human: owns approval, product priorities and validation on a real Windows PC.
- Architect (ChatGPT): owns requirements, architecture, safety constraints, task specs and technical review.
- Codex: implements one approved task at a time, reports exact changes and observed build/test results.

## Initial repository preparation (historical)

1. Create an empty Git repository named `CleanSpace` on your Windows PC.
2. Copy **this folder's contents** to its root (`AGENTS.md` must be at repo root).
3. Open the local folder in the Codex Windows app (native PowerShell environment; no WSL needed).
4. Start with `tasks/001-foundation.md` using the handoff prompt below.
5. Review diffs and test output. Approve one task before opening the next.
6. Do not enable any cleanup operations until the separate security gate is approved.

## Initial TASK-001 prompt (historical)

> Read `AGENTS.md`, `docs/PRODUCT.md`, `docs/ARCHITECTURE.md`, `docs/SECURITY.md`, and `tasks/001-foundation.md`. Implement **only Task 001**. First inspect the Windows development environment. Follow the task scope exactly. Do not implement scanning, delete/move/recycle functions or privilege elevation. Run the actual build/tests supported by this machine and report exact commands/results. If a toolchain prerequisite is missing, explain it and stop instead of claiming success. Do not start Task 002 or commit/push without approval.

## Source-of-truth precedence

1. Current human approval for the task
2. `AGENTS.md` hard safety constraints
3. `docs/SECURITY.md`
4. `docs/ARCHITECTURE.md`
5. `docs/PRODUCT.md` and `docs/ROADMAP.md`
6. Approved individual task spec

If instructions conflict, **do not implement** the unsafe or contradictory step. Surface the conflict and ask for architectural review.

## Documents

- `AGENTS.md` — Codex repository guidance and prohibitions.
- `docs/PRODUCT.md` — product scope and exclusions.
- `docs/ARCHITECTURE.md` — project topology, contracts and flows.
- `docs/SECURITY.md` — threat model, cleanup invariants, release gates.
- `docs/ROADMAP.md` — milestones, dependencies and definition of done.
- `docs/ADR-003-QUICK-SCAN-SAFETY.md` — accepted Quick Scan disablement, evaluated strategies and separate re-enablement approvals.
- `tasks/001-foundation.md` — initial implementation task.
- `tasks/002-drive-overview.md` — system drive overview.
- `tasks/003-quick-scan.md` — allowlisted read-only scanner.
- `tasks/004-safety-engine.md` — domain policy engine.
- `tasks/005-recycle-research.md` — non-production safety spike (no app cleanup).
- `tasks/006-gated-recycle.md` — conditional implementation requiring fresh explicit approval.

## References

- WinUI 3 and Windows App SDK: https://learn.microsoft.com/en-us/windows/apps/
- Windows App SDK stable downloads: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads
- Windows Shell IFileOperation: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation
- Codex development workflow: https://developers.openai.com/cookbook/examples/codex/iterating-development-workflows-with-codex
