# TASK-003 — Read-only Quick Scan Engine

Implemented and verified on 2026-10-18 on the available Windows x64 host. **Recommendation: ACCEPT TASK-003 for architectural review**, within the limits below. The solution builds with **0 warnings / 0 errors**, **167 tests pass**, and **three startup smoke tests of the final production binary pass**. Real WinUI Start, Cancel, navigation, partial results, theme compatibility and a narrower layout were exercised. Cleanup remains disabled.

## Authorization and specification discrepancy

Read AGENTS.md, PRODUCT, ARCHITECTURE, SECURITY, ROADMAP, `tasks/003-quick-scan.md`, TASK-002-REPORT and the attached TASK-003 request before implementation. The worktree started clean.

The repository task/product/architecture describe candidate lists, age filters and safety classification. The attached authorization explicitly requires aggregates and says cleanup eligibility has NOT been evaluated. That stricter boundary takes precedence: **no candidate policy, age eligibility rule, candidate list, delete selection, cleanup plan or reclaimable-byte estimate was implemented**. The conflicting classification work was stopped at design review; repository task documents were preserved. No TASK-004 work was started.

## Architecture and contracts

- **Core:** `ScanStatus` (Idle, Scanning, Completed, Partial, Cancelled, Failed), `ScanScope`, `ScanTotals`, `ScanProgress`, `ScanSession`, `ScanWarning` and validated `ScanLimits`. These contain primitives and ordinary .NET types, with no WinUI/Windows references. Aggregates distinguish files, logical bytes, skipped entries and examined entries.
- **Application:** `IScanScopeProvider` establishes a scope lease. `IScanScopeLease` binds root/ancestry and volume provenance; `IScanDirectory` exposes incremental directory metadata, child directory leases and file metadata snapshots. There are no content-read or mutation methods on these ports. `ScanEntry` validates one unambiguous filename component and rejects unsafe attributes/protected directory names. Deterministic fake implementations require no real user folders.
- **Orchestration:** singleton `QuickScanUseCase` acquires an interlocked single-session guard, executes synchronous ports inside `Task.Run`, performs bounded depth-first traversal and returns a session summary. OS access stays in Infrastructure. Every scan has a fresh ID and fresh counters/identity set. A concurrent request returns Failed/AlreadyScanning without calling the provider. The guard remains held until traversal and handle disposal finish.
- **Infrastructure:** `WindowsTempScopeProvider`, `WindowsScanDirectory` and `WindowsScanNative` implement the metadata ports. Handles and bounded directory buffers remain inside this layer. No paths or filenames are collected for display, logging or a results cache.
- **App:** DI registers the production scope provider, use case and `ScanResultsViewModel`. `ScanResultsView` supplies native controls, totals, indeterminate progress, Start/Cancel and fixed safe summaries. The Dashboard Quick Scan command selects Scan Results and starts the same operation. Code-behind only wires dependency properties/navigation/theme UI. Nothing starts a scan automatically.

### Trusted scope and native boundary

1. Resolve current-user Local AppData and Profile with `SHGetKnownFolderPath` and the current token. Compare each current location with its OS-provided default location; refuse redirection, UNC/device paths, noncanonical paths, prefix siblings, unexpected ancestry or excessive ancestor depth. The profile path is never constructed manually. Only the literal child name `Temp` is appended to the recognized Local AppData location; `%TEMP%` is not trusted.
2. Resolve the Windows volume through the existing TASK-002 native metadata API. Require the recognized user location to be on that drive root. This adapter additionally requires **NTFS**: its 64-bit directory/file identity scheme has not been enabled for other filesystems. Unknown filesystem information fails closed.
3. Open the volume root by GUID without enumerating it. Open each individual ancestor relative to its verified parent handle. Only the verified Temp directory is enumerated. Ancestor handles remain held until the session ends. Windows, Program Files, System32, WinSxS, Downloads, Documents and Desktop names are rejected defensively during scope validation/directory traversal.
4. Each child open uses `NtCreateFile` with **FILE_OPEN**, one validated component, `RootDirectory`, and **FILE_OPEN_REPARSE_POINT**. There is no creation/overwrite fallback. Directory requests require FILE_DIRECTORY_FILE and list/attribute access. File requests require FILE_NON_DIRECTORY_FILE and attribute access, with FILE_OPEN_NO_RECALL; they do not request FILE_READ_DATA. Synchronous handles also request SYNCHRONIZE. No privilege is enabled.
5. Reject reparse/device/offline/recall attributes before entry access and after handle metadata retrieval. A reparse object opened during a race is rejected before enumeration. Junctions/mounts/symlinks are not followed. Directory opens use the reparse-object flag; FILE_OPEN_NO_RECALL is used for files, since its combination with FILE_DIRECTORY_FILE returned invalid-parameter on this host.
6. Validate directory type/attributes, file ID and normalized final volume-GUID path against the expected parent ancestry before each enumeration/access. Directory handles deny write/delete sharing. File metadata is queried twice on the same handle and compared with the enumerated snapshot; changed size, time, attributes, identity or name is excluded. Errors produce skips or a redacted unavailable/failed result; no search is broadened.

The trust source is the OS current-user known-folder/default-location relationship plus verified native ancestry and volume identity. This does **not** certify root ACL ownership, deletion authorization or file ownership. There are zero eligibility decisions.

API references used for the implementation: [NtCreateFile](https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile), [known-folder flags](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ne-shlobj_core-known_folder_flag), [directory metadata records](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_both_dir_info), and [CreateFile access/sharing semantics](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew).

### Budgets, lifecycle and cancellation

| Budget | Production default | Supported maximum |
| --- | --- | --- |
| Entries examined | 50,000 | 100,000 |
| Directory depth below Temp | 16 | 32 |
| Elapsed traversal time, including scope establishment | 10 seconds | 30 seconds |
| Retained file/directory identities | 16,384 | 100,001 |
| Enumeration buffer | 64 KiB per active directory | At most 33 buffers / 2,162,688 bytes |

The separate identity-record budget bounds the deduplication set independently of entry count. There is no whole-directory materialization, breadth-first queue or retained per-file result list. Directory buffers are allocated lazily. The limits bound scanner-owned retained data structures; they are **not** a cap on the WinUI/.NET process working set or native filesystem latency.

Progress is emitted at most once per 150 ms of traversal and marshalled through the WinUI synchronization context. A generation/session check rejects late progress from an earlier scan. The awaited result supplies final totals. Total work is unknown, so there is no invented percentage.

Idle transitions to Scanning only on a user action. Completed means the enumerator ended without warnings; skips or budgets produce Partial. A cancelled scan preserves prior accepted totals as Cancelled. Unavailable scope returns Failed with zero files and a fixed safe explanation. Unexpected provider failures are redacted. Skipped subtrees count once; unvisited descendants/entries are unknown, not invented skipped counts.

Cancellation is checked before scope access, between native operations, around enumeration and after file metadata retrieval. It does not interrupt a synchronous Windows call already in progress; Cancel says it is waiting for that call. Handles are disposed on completion/failure/cancellation. Window close cancels pending work. A scan is not a volume-wide atomic snapshot.

## Changed-file inventory

Seven existing files changed; eighteen files were added, including this report.

| Files | Change |
| --- | --- |
| `src/CleanSpace.Core/Models/ScanSession.cs` | New contracts and budgets |
| `src/CleanSpace.Application/Abstractions/IScanScopeProvider.cs` | New read-only metadata/lease ports and entry guards |
| `src/CleanSpace.Application/UseCases/QuickScanUseCase.cs` | New bounded scan orchestration |
| `src/CleanSpace.Infrastructure/Scanning/WindowsTempScopeProvider.cs`, `WindowsScanDirectory.cs`, `WindowsScanNative.cs` | New trusted Windows scope, handles, directory records and metadata adapter |
| `src/CleanSpace.App/ViewModels/ScanResultsViewModel.cs`, `Views/ScanResultsView.xaml`, `Views/ScanResultsView.xaml.cs` | New MVVM scan presentation and UI wiring |
| `src/CleanSpace.App/App.xaml.cs`, `MainWindow.xaml`, `ViewModels/MainViewModel.cs`, `Views/DashboardView.xaml`, `Views/DashboardView.xaml.cs` | DI, navigation and Quick Scan integration |
| `tests/CleanSpace.Core.Tests/ScanLimitsTests.cs` | New budget validation tests |
| `tests/CleanSpace.Application.Tests/QuickScanUseCaseTests.cs` | New deterministic scanner tests |
| `tests/CleanSpace.App.Tests/ScanResultsViewModelTests.cs`, `CleanSpace.App.Tests.csproj` | New presentation tests and linked production source |
| `tests/CleanSpace.IntegrationTests/WindowsQuickScanTests.cs`, `ScanApiSafetyTests.cs` | New trust-rule/native fixture/compiled API guards |
| `diagnostics/QuickScanUiProbe/QuickScanUiProbe.csproj`, `App.xaml.cs`, `README.md` | New non-shipping fake-state WinUI probe linking production views/ViewModels |
| `diagnostics/DriveOverviewUiProbe/App.xaml.cs` | Supply the new shell dependency with an unavailable fake scan port; preserve existing drive probe build |
| `docs/TASK-003-REPORT.md` | Delivery evidence and limits |

Package pins, solution/project production configuration, manifest, startup observer, settings adapter and prior reports remain unchanged. No commit, staging, push, deployment, publishing or global installation occurred.

## Windows build, tests and security audit

Working directory: `E:\01 - Work\cleanup-windows`. Available SDK: .NET 10.0.302; test runtime 10.0.10; Windows 10.0.26300.0, x64. PowerShell is 7.6.5 with its own CLR 10.0.11 (`artifacts/task-003/final-checks.json`). Commands below ran on the actual Windows toolchain. Tests, audited network restore and interactive observations ran in the normal-user host session outside the restricted process sandbox; startup observers independently confirmed `Elevated: false`. No UAC/admin execution was used.

```powershell
dotnet restore CleanSpace.slnx --disable-parallel --force --no-http-cache --verbosity minimal -m:1 -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build CleanSpace.slnx --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet test CleanSpace.slnx --no-build --no-restore --verbosity normal -m:1 --logger 'trx;LogFilePrefix=task-003-final' --results-directory artifacts/task-003/test-results-final --blame-hang-timeout 30s
git diff --check
```

| Check | Exit / evidence |
| --- | --- |
| Forced full restore, NuGetAudit=true, AuditMode=all | 0; all eight projects restored, no audit warnings (`artifacts/task-003/restore-audit.log`) |
| Full solution build | 0; 0 warnings / 0 errors, 25.07 s (`build-final.log`) |
| Full solution tests | 0; 167 passed, 0 failed, 0 skipped, 10.67 s (`tests-final.log`, `test-results-final/`, `test-summary.json`) |
| Whitespace check | 0; no errors; Git's existing LF/CRLF conversion notices only |

| Test assembly | Passed | Failed / skipped |
| --- | --- | --- |
| App | 29 | 0 / 0 |
| Application | 67 | 0 / 0 |
| Core | 31 | 0 / 0 |
| Integration | 40 | 0 / 0 |

This preserves the 110 accepted tests and adds 57. Coverage includes missing/denied roots; default/current known-folder validation, system-volume mismatch and prefix/path escapes; unsafe/junction/symlink/cloud attribute fakes; changed files/directories; denied metadata; zero files; a lazy million-file provider bounded after 123 entries; depth/time/identity-memory limits; negative sizes, invalid IDs/time and byte overflow; duplicate identities/hard links; pre/during-enumeration cancellation; partial/failure/retry behavior; progress throttling; disposal on cancellation; repeated sessions; and overlap exclusion. Compiled scanner API guards require the six reviewed native imports and reject managed content/mutation/process/registry/network calls.

The native fixture test creates a real junction from its `scan/link` to a separate `outside` directory under the same unique test owner. The outside sentinel is 99 bytes. Only the two in-scope files (37 + 51 bytes) are counted: **2 files / 88 logical bytes / 1 skipped junction**, with unchanged length/modification time on the checked file. Opening the junction as a root is rejected. A separate fixture proves invalid child names are refused and changed file metadata is detected even with an open writer. Fixtures are retained under `artifacts/task-003/fixtures`; no deletion API is used. `cmd /d /c mklink /J` is used only to create that test-owned junction, never in production or for cleanup.

Diagnostic verification commands:

```powershell
dotnet restore diagnostics/QuickScanUiProbe/QuickScanUiProbe.csproj --disable-parallel --verbosity minimal -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build diagnostics/QuickScanUiProbe/QuickScanUiProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet build diagnostics/DriveOverviewUiProbe/DriveOverviewUiProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
```

All final diagnostic commands exited 0. Final scan probe build: 0 warnings/errors, 13.69 s; existing drive probe compatibility build: 0 warnings/errors, 14.21 s. Logs are `probe-restore-host.log`, `probe-build-final.log`, `drive-probe-compat-build.log`.

Earlier failures are retained, not counted as passes: restricted `dotnet --info` failed querying Service Control Manager workload metadata; restricted test-host startup stalled and was interrupted; restricted probe restore failed DNS/NU1301 and audit retrieval/NU1900, then host restore succeeded without suppressing auditing. Initial native fixtures exposed incompatible directory/no-recall open options (NTSTATUS C000000D). A test initially assumed an attribute handle would reject an open writer; Windows permits such metadata queries, so the test now verifies metadata-only access and change detection. Final corrected native fixtures and the full suite pass. There were no package upgrades or security-setting changes.

## Production startup and WinUI evidence

Executed the unchanged observer separately for each final run:

```powershell
./diagnostics/Verify-ProductionStartup.ps1 -RunId Task003-Final-1
./diagnostics/Verify-ProductionStartup.ps1 -RunId Task003-Final-2
./diagnostics/Verify-ProductionStartup.ps1 -RunId Task003-Final-3
```

Each command's output was captured with `Tee-Object` to `artifacts/task-003/final-production-{1,2,3}-console.log`. Each launched the actual production EXE from its output directory, observed a stable window and pinned app-local XAML module, queried crash events successfully and exited 0 after Alt+F4. No process was killed. Times below are 2026-10-18, Asia/Bangkok (UTC+07:00).

| Run | PID / window | Start → normal exit | Status / matching crash events |
| --- | --- | --- | --- |
| Task003-Final-1 | 30232 / 657762 | 09:59:00 → 10:01:38 | PASS / none |
| Task003-Final-2 | 27176 / 1377298 | 10:01:41 → 10:02:50 | PASS / none |
| Task003-Final-3 | 14284 / 1050276 | 10:02:52 → 10:03:24 | PASS / none |

All three used managed assembly SHA256 `C14D0662C65970605293F15726FD91E20CF8C96FB0891ACD1D75D2D531C449E7` and MainWindow XBF `7C19765A3335D29DEFB4A100F477F86D03E52E88FD7C682CFE1B0843AC573509`. SDK remains 2.5.1 / resolved WinUI 2.3.9. Loaded XAML SHA256 is `AAD12524765E6FB63F0AE26A45A9BA3104F24FDE66413D8A3036FBED74A1E990`, matching the pinned package. Individual observer JSON/checkpoints remain in `artifacts/fix-06/launches/Task003-Final-*`; consolidated evidence is `artifacts/task-003/final-runtime.json`. An earlier development-binary run also passed but is not substituted for these final three. Final process check found zero production/probe processes.

Native computer-use automation selected returned window identities. Screenshots were inspected inline; retained accessibility text is under `artifacts/task-003/ui/`. Some immediate captures lagged XAML animations; settled captures supplied the assertions.

| UI check | Observed result |
| --- | --- |
| No automatic scanning | Final production Scan Results initially Idle with zero totals after startup/navigation (`final-idle.txt`) |
| Dashboard Quick Scan | Navigates to Scan Results and starts the read-only operation; observed in production |
| Start/progress/overlap prevention | Scanning status, changing counts, indeterminate accessible progress, disabled Start and enabled Cancel (`final-scanning.txt`) |
| Responsive navigation | Settings rendered while scanning; returning showed the later partial result (`final-navigation-pending.txt`, `final-partial.txt`) |
| Final production Partial | 342 files, 1.08 GiB displayed logical size, 1 skipped entry; 10.00 s time budget, changing entry excluded |
| Final production Cancel | Repeated scan cancelled at 7.75 s; 343 files, 1.08 GiB displayed, 1 skipped; Start reenabled, Cancel disabled (`final-cancelled.txt`) |
| Scope error/retry | Probe Failed, zero totals, fixed unavailable message, injected private path absent, Start enabled (`probe-error.txt`) |
| Partial and Completed | Probe unsafe-entry result: 11 files / 44 KiB / 1 skip; later Completed: 12 files / 48 KiB / 0 skips (`probe-unsafe-partial.txt`, `probe-completed-light.txt`) |
| Themes | Production System/Dark; probe explicit Light and Dark, readable warning/card/action surfaces. Probe themes use memory only, without changing real settings (`probe-light-partial.txt`, `probe-dark.txt`) |
| Narrow layout | Native Size operation reduced production width from approximately 1,634 to 894 pixels. Compact navigation, wrapping text and both actions remained usable (`final-narrow.txt`) |
| Cleanup boundary | Cleanup navigation stays disabled; no deletion checkboxes, cleanup button, eligibility classification or reclaimable estimate |

### Exact live scan snapshot

A separate read-only invocation of the compiled production use case established the same authorized scope and recorded exact integer totals. It ran after the earlier production window had closed, with no concurrent UI scan. At **09:56:54 UTC+07:00**, it returned Partial after **10.0016606 s**, with **341 files**, **1,152,982,424 logical bytes**, **1 skipped entry** and **6,321 examined entries**. Warnings were ChangedEntry and TimeLimit. File/entry totals differ because directory metadata entries are examined too. Evidence: `artifacts/task-003/live-scan-snapshot.json`.

The invocation loaded Core/Application/Infrastructure from the production output directory and executed:

```powershell
$scanBin = Join-Path (Get-Location) 'src/CleanSpace.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64'
foreach ($scanAssembly in @('CleanSpace.Core','CleanSpace.Application','CleanSpace.Infrastructure')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $scanBin ($scanAssembly + '.dll')))
}
$scanCase = [CleanSpace.Application.UseCases.QuickScanUseCase]::new([CleanSpace.Infrastructure.Scanning.WindowsTempScopeProvider]::new(), [TimeProvider]::System)
$scanSnapshot = $scanCase.ExecuteAsync($null, [Threading.CancellationToken]::None, $null).GetAwaiter().GetResult()
```

Its public counters/status/duration/warnings were serialized to the evidence JSON. This supplements real UI observations; it is not presented as an EXE startup test. Results can differ across repeated scans because Temp changes and traversal is budgeted.

## Safety conclusions and remaining limits

- No deletion, move, rename, recycle, truncation, overwrite, ACL/permission change, registry cleanup, privileged helper, service, driver, scheduled task, auto-start, background scan, telemetry or runtime network feature was added. Scanner file handles cannot read content and no hashing/content stream API is called. Native open dispositions never create or overwrite targets.
- Scanner-owned target operations are metadata queries/handle opens/enumeration only. No real Windows/user Temp data was created or modified for testing. Build outputs, retained test fixtures and diagnostic evidence are ordinary workspace writes. The existing settings adapter retains its separate authorized behavior; probe theme changes are in memory. **No host-wide filesystem trace was collected, so this is not a claim of zero disk writes by Windows/.NET/the app/process tree.**
- Attribute-only file opens do not prove a file is unused or freeze its content. Comparisons reject detected changes but cannot make the entire scan atomic or prove that a value stays unchanged afterward. Native directory handles/relative opens protect traversal ancestry; they do not authorize future file operations. Filesystem filters and in-flight native calls can still delay cancellation/time-budget completion.
- IDs deduplicate observed NTFS identities within one session, including observed hard links. Identity reuse/concurrent changes can conservatively undercount. Logical length is not allocation size or recoverable space; sparse/compressed data, hard links outside scope and shared storage prevent that inference.
- Scope provenance trusts Windows current-user/default known-folder configuration. ACL ownership certification and alternative filesystems are outside this implementation. Redirected/unavailable/non-NTFS scopes remain unavailable. There is no fallback to another Temp location or broader scan.
- Reparse/symlink/cloud behavior is covered by deterministic attribute/port tests and a real junction/root-rejection fixture. Real cloud hydration, privileged symlink creation, adversarial kernel/filter changes and exhaustive concurrent swap stress were not exercised. No symlink privilege or safeguard was changed. No destructive test was run.
- Verification covers this Windows host's Debug/win-x64 unpackaged app, current System preference plus explicit Light/Dark, and approximately 894–1,634 pixel widths. Release/MSIX/distribution, high-contrast/screen-reader certification, other hosts/filesystems and smaller windows are not certified. The live Temp tree did not complete within the default budget; that is shown honestly as Partial.

No observed TASK-003 acceptance blocker remains within this read-only scope. Recommend acceptance subject to architectural review of the documented native/trust/race limits. Stop here; TASK-004 and all cleanup capabilities require separate authorization.
