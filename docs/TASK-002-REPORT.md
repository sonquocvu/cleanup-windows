# TASK-002 — Windows Drive Overview

Implemented and verified on **2026-10-18** on the available Windows x64 host. **Recommendation: ACCEPT TASK-002 for architectural review.** The production Dashboard displays real Windows-volume metadata, Refresh works, the solution builds with **0 warnings / 0 errors**, **110 tests pass**, and all **three production startup smoke runs pass**. No TASK-003 work was started.

## Architecture and contracts

- **Core:** immutable `DriveOverview` contains primitive, nullable byte metrics, display-safe volume identity, optional filesystem type and fetch time. It references no WinUI or Windows API. It rejects negative and inconsistent metrics. `UsedBytes = TotalBytes - TotalFreeBytes`; percentage uses that derived value and exists only when total capacity is positive and free space is known. Null is unavailable; zero remains an actual zero. No quota-limited value is substituted for total capacity or volume free space.
- **Application:** `IDriveInfoProvider.GetSystemDriveAsync(CancellationToken)` is the read-only port. `DriveInfoSnapshot` is its untrusted/raw DTO. `DriveOverviewUseCase` validates identity and metrics and returns `DriveOverviewResult`. Only a drive root or opaque volume GUID may reach the UI; private mounted-folder paths and unsafe filesystem labels are withheld. Expected access, I/O, native, unsupported, invalid-data and overflow failures become a fixed retryable notice, never an exception message or path. Cancellation propagates.
- **Infrastructure:** `WindowsDriveInfoProvider` implements the port. Its internal `IWindowsDriveMetadataApi` seam exposes only five metadata queries, enabling strict fakes without a filesystem fixture. It obtains the current shared Windows directory with `GetSystemWindowsDirectoryW`, resolves its volume with `GetVolumePathNameW` and `GetVolumeNameForVolumeMountPointW`, then queries the volume by GUID. It rechecks the root/identity and rejects changes. It never guesses a drive letter or uses the working directory/environment variables to choose a drive.
- **App:** DI registers the provider, use case and `DashboardViewModel`. The existing shell hosts `DashboardView`; its code-behind only declares the ViewModel dependency property and initializes XAML. Window activation still precedes asynchronous settings/Dashboard initialization. Native queries run inside `Task.Run`; ViewModel awaits resume on the UI context. Refreshes are serialized, disable Refresh while pending, clear stale metrics/error state, and permit retry. DI disposal cancels pending work on window close. There is one startup refresh and user-triggered refreshes only, without polling or scheduling.

### Native metric semantics

`GetDiskSpaceInformationW` returns a single allocation-unit snapshot. Checked 64-bit conversion uses:

| Display/contract value | Windows field / calculation |
| --- | --- |
| Total capacity | `ActualTotalAllocationUnits × SectorsPerAllocationUnit × BytesPerSector` |
| Free space (volume) | `ActualAvailableAllocationUnits × allocation-unit bytes` |
| Available to you | `CallerAvailableAllocationUnits × allocation-unit bytes` |
| Used space | Total capacity minus free space (volume) |
| Percentage used | Used space / positive total capacity × 100 |
| Filesystem type | Optional `GetVolumeInformationW` result |

The quota-limited `CallerTotalAllocationUnits` is deliberately unused. In particular, `GetDiskFreeSpaceExW`'s total-byte result can be quota-limited, so it is not used as filesystem-wide total capacity. Missing allocation geometry yields unknown capacity metrics; overflow or inconsistent metrics fail closed. A filesystem-name failure leaves that optional label unavailable. A space/identity query failure is an error, without invented fallback values.

Microsoft references: [disk-space fields](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/ns-fileapi-disk_space_information), [GetDiskSpaceInformationW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getdiskspaceinformationw), [quota semantics of GetDiskFreeSpaceExW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getdiskfreespaceexw), [system Windows directory](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getsystemwindowsdirectoryw), and [volume GUID identity](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumenameforvolumemountpointw). The selected disk-space API supports Windows 10 build 17763, below the unchanged app minimum of 19041.

### Dashboard behavior

The Dashboard shows Windows drive identity, filesystem, total/used/volume-free space, percentage, a Fluent ProgressBar, separately labeled user-available space, snapshot time and Refresh. Explicit KiB/MiB/GiB units use base 1024, with rounded values and exact-byte tooltips; formatting follows the current culture. Zero/unknown totals never become a fabricated 0% bar. Loading has an active ProgressRing and disabled Refresh. Errors use a native error InfoBar with a safe notice and enabled retry. The no-junk-analysis message and disabled Quick Scan remain visible; Cleanup remains disabled. Existing navigation, theme resources and settings persistence remain in use.

## Changed files

Four existing files changed; the other additions are scoped to this task. The initial worktree was clean.

| Files | Purpose |
| --- | --- |
| `CleanSpace.slnx` | Add presentation unit-test project |
| `src/CleanSpace.Core/Models/DriveOverview.cs` | Validated immutable model and derivations |
| `src/CleanSpace.Application/Abstractions/IDriveInfoProvider.cs` | Read-only port |
| `src/CleanSpace.Application/DTOs/DriveInfoSnapshot.cs`, `DriveOverviewResult.cs` | Raw snapshot and safe result |
| `src/CleanSpace.Application/UseCases/DriveOverviewUseCase.cs` | Validation, cancellation, redacted failure handling |
| `src/CleanSpace.Infrastructure/Windows/IWindowsDriveMetadataApi.cs`, `WindowsDriveMetadataApi.cs`, `WindowsDriveInfoProvider.cs` | Narrow native seam, read-only Win32 queries, asynchronous adapter |
| `src/CleanSpace.App/App.xaml.cs` | DI registration and startup initialization |
| `src/CleanSpace.App/MainWindow.xaml`, `ViewModels/MainViewModel.cs` | Dashboard integration; Scan Results keeps its placeholder |
| `src/CleanSpace.App/ViewModels/DashboardViewModel.cs` | Loading, error, refresh, presentation and cancellation state |
| `src/CleanSpace.App/Formatting/DriveValueFormatter.cs` | Byte/percentage display formatting |
| `src/CleanSpace.App/Views/DashboardView.xaml`, `DashboardView.xaml.cs` | Fluent Dashboard and UI wiring |
| `tests/CleanSpace.Core.Tests/DriveOverviewTests.cs` | Capacity/consistency/unknown/quota derivation tests |
| `tests/CleanSpace.Application.Tests/DriveOverviewUseCaseTests.cs` | Fake-provider validation, failure redaction, identity and cancellation tests |
| `tests/CleanSpace.App.Tests/CleanSpace.App.Tests.csproj`, `DashboardViewModelTests.cs`, `DriveValueFormatterTests.cs` | Unit-test the actual UI-independent presentation sources, linked without loading WinUI |
| `tests/CleanSpace.IntegrationTests/WindowsDriveInfoProviderTests.cs`, `DriveMetadataSafetyTests.cs` | Strict native fakes, real Windows query, conversion and compiled API safety guards |
| `diagnostics/DriveOverviewUiProbe/DriveOverviewUiProbe.csproj`, `App.xaml.cs`, `README.md` | Non-shipping WinUI host linking production UI sources with fake drive/in-memory theme ports |
| `docs/TASK-002-REPORT.md` | This report |

The probe is outside the production solution and has no production reference or failure-injection switch. It exercises real generated WinUI XAML, with deterministic 12-second reads, an initial failure, a quota example and unavailable metrics. Its theme fake does not write user settings. It complements production verification; it is not presented as the production executable.

## Windows build and automated tests

Working directory for all commands: `E:\01 - Work\cleanup-windows`. Host: Windows `10.0.26300.0`, x64; .NET SDK `10.0.302`, MSBuild `18.6.11.33009`, runtime `10.0.10`, PowerShell `7.6.5`. Host execution was explicitly verified non-elevated. Restores with network audit, tests and production observations used the normal-user host session outside the restricted process sandbox; this did not use UAC or an Administrator token.

Final solution commands (output piped to the corresponding log with `Tee-Object`; native exit codes captured):

```powershell
dotnet restore CleanSpace.slnx --disable-parallel --force --no-http-cache --verbosity minimal -m:1 -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build CleanSpace.slnx --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet test CleanSpace.slnx --no-build --no-restore --verbosity normal -m:1 --logger 'trx;LogFilePrefix=task-002-final' --results-directory artifacts/task-002/test-results --blame-hang-timeout 30s
```

| Verification | Exit | Observed result |
| --- | --- | --- |
| Forced restore with auditing enabled | 0 | All 8 projects restored, no warnings |
| Full solution build | 0 | 0 warnings, 0 errors; 26.94 seconds |
| Full solution tests | 0 | **110 passed, 0 failed, 0 skipped**; 10.75 seconds |
| `git diff --check` | 0 | No whitespace errors; existing LF/CRLF conversion notices only |

| Test assembly | Passed | Failed / skipped |
| --- | --- | --- |
| CleanSpace.App.Tests | 26 | 0 / 0 |
| CleanSpace.Application.Tests | 31 | 0 / 0 |
| CleanSpace.Core.Tests | 24 | 0 / 0 |
| CleanSpace.IntegrationTests | 29 | 0 / 0 |

This preserves all 39 existing tests and adds 71. Coverage includes valid/full/empty/zero/unknown/partial metrics, negatives, inconsistent capacities, large values/overflow, quota differences, safe identity/labels, permission/I/O/not-ready/unavailable failures, cancellation before/after retrieval, duplicate refresh exclusion, retry/recovery without stale data, initialization once, disposal cancellation, explicit binary units, percentage bounds and culture. Adapter guards inspect compiled calls (including Task.Run callbacks) for filesystem enumeration/content/mutation, process, registry, WMI and network APIs, and require the exact five approved read-only native imports.

Early verification results are retained rather than counted as passes: the first no-restore build failed with `NETSDK1004` because the new test project's assets did not exist yet; restore resolved that prerequisite. Restricted restore initially reported `NU1900`; the forced normal-user restore above successfully fetched vulnerability data without suppressing audit. The restricted test attempt stalled before test-host progress and was interrupted. The first host suite had 22 test-invocation failures because xUnit could not bind Int32 InlineData literals to nullable Int64/Double parameters; explicit `L`/`d` test-data literals corrected them. The final full suite above passes. No production correction was needed for those test-data failures. Restricted `dotnet --info` also reported Service Control Manager access denied while querying workload information; the installed SDK/MSBuild, build and host test execution were verified separately.

Diagnostic host commands:

```powershell
dotnet restore diagnostics/DriveOverviewUiProbe/DriveOverviewUiProbe.csproj --disable-parallel --verbosity minimal -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build diagnostics/DriveOverviewUiProbe/DriveOverviewUiProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
```

Both exited 0; probe build had 0 warnings / 0 errors in 17.99 seconds. It uses existing centrally pinned dependencies.

Logs and TRX: `artifacts/task-002/restore-verified.log`, `build-final.log`, `tests-final.log`, `test-results/`, `test-summary.json`, `probe-restore.log`, `probe-build.log`. Initial attempts are retained beside them.

## Actual Windows runtime and WinUI evidence

The unchanged production startup observer was executed three times, each in a separate shell invocation:

```powershell
./diagnostics/Verify-ProductionStartup.ps1 -RunId Task002-Production-1 2>&1 | Tee-Object -FilePath artifacts/task-002/production-1-console.log
./diagnostics/Verify-ProductionStartup.ps1 -RunId Task002-Production-2 2>&1 | Tee-Object -FilePath artifacts/task-002/production-2-console.log
./diagnostics/Verify-ProductionStartup.ps1 -RunId Task002-Production-3 2>&1 | Tee-Object -FilePath artifacts/task-002/production-3-console.log
```

Each launched the actual `src/CleanSpace.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/CleanSpace.App.exe`, from that output directory. Each was closed normally with Alt+F4; no process was killed. All times below are 2026-10-18, UTC+07:00.

| Run | PID / window handle | Start → exit observed | Observer / process exit | Crash events 1000 / 1026 |
| --- | --- | --- | --- | --- |
| Task002-Production-1 | 17024 / 655528 | 09:24:58 → 09:26:54 | 0 / 0, PASS | None |
| Task002-Production-2 | 9964 / 5113282 | 09:27:03 → 09:28:07 | 0 / 0, PASS | None |
| Task002-Production-3 | 4480 / 656836 | 09:28:09 → 09:28:59 | 0 / 0, PASS | None |

Each observer confirmed a stable CleanSpace window, successful Application-log queries, and the app-local `Microsoft.UI.Xaml.dll` matching the resolved pinned package. SDK remains `[2.5.1]`, resolving WinUI `2.3.9`; loaded XAML version is `3.2.3.2609`, SHA256 `AAD12524765E6FB63F0AE26A45A9BA3104F24FDE66413D8A3036FBED74A1E990`. All three runs used managed assembly SHA256 `1EFAF73201480335585150AA93ADBAEBA9437FCB4F1F51DCE890975420FFF2CE` and MainWindow XBF SHA256 `0BDDD877128287A209F0BDEDFEEDF7F0FD5CE85694E37427DB413FE005479C42`.

Individual observer results/checkpoints/stderr retain the observer's existing location, `artifacts/fix-06/launches/Task002-Production-{1,2,3}.*`; summary is in `artifacts/task-002/final-runtime.json`. Final read-only checks found **0 remaining production/probe processes** and restored `{"Theme":"System"}`.

### UI acceptance observations

Native GUI automation targeted returned process/window identities using the computer-use skill. Screenshots were inspected live and saved, with accessibility trees, under `artifacts/task-002/ui/`.

| Requirement | Result / evidence |
| --- | --- |
| Real system volume and metrics | PASS: production displays C:\ / NTFS on this host, 464.33 GiB total, about 274.47–274.68 GiB used and 189.86–189.64 GiB free across snapshots; this observed drive letter is not hardcoded. `production-system-dashboard.png` |
| Refresh | PASS: timestamp advanced from 09:25:02 to 09:26:23 and metrics changed; named accessible Refresh and numeric ProgressBar. `production-refreshed.txt` |
| Light | PASS: light surfaces, readable card, labels and usage bar. `production-light-dashboard.png` |
| Dark / System | PASS: explicit Dark and System render correctly; System matches the existing Windows dark application preference, read without changing it. `production-dark-restart.png`, `production-final-system-dashboard.png` |
| Appearance persistence | PASS: Light saved on run 1 and restored on run 2; Dark saved on run 2 and restored on run 3; original System selection saved again through Settings. `production-light-restart.png`, `production-system-restored.txt` |
| Navigation / disabled scope | PASS: Dashboard, Scan Results and Settings render; Quick Scan and Cleanup stay disabled. `production-scan-results.png` and production trees |
| Responsive layout | PASS: native Size operation narrowed the window from 1634 to approximately 894 logical pixels; compact navigation, wrapping labels and card remain usable. `production-sized-light.png`. An earlier border drag had no effect and is not counted as resizing. |
| Loading | PASS in real WinUI probe using production UI: ProgressRing/reading notice, disabled Refresh, unavailable metrics and no usage bar. `probe-loading-dark.png`, `probe-refresh-loading.png` |
| Error / retry | PASS in probe: redacted error InfoBar, no injected private path, no stale statistics, enabled Refresh. `probe-error-dark.png` |
| Responsiveness during retrieval | PASS in probe: Scan Results rendered during the 12-second pending refresh; retrieval completed and returning to Dashboard showed the result. `probe-navigation-pending-refresh.png` |
| Quota values / recovery | PASS in probe: fake Q:\, 100 GiB total, 75 used, 25 volume-free, 10 user-available, accessible bar value 75, and explanatory notice. `probe-quota-recovered.png` |
| Unknown metrics | PASS in probe, Light: Q:\ identity retained, filesystem and metrics unavailable, no percentage bar or invented zero values. `probe-unknown-light.png` |

Production and fake-state evidence remain clearly distinguished. The probe title explicitly identifies fake data. Some immediate accessibility captures lagged XAML navigation/animations; settled captures were used for assertions, including the completed 75% bar.

![Production Dashboard in Light](../artifacts/task-002/ui/production-light-dashboard.png)

![Production Dashboard in System/Dark](../artifacts/task-002/ui/production-final-system-dashboard.png)

![Probe: safe provider error](../artifacts/task-002/ui/probe-error-dark.png)

![Probe: quota differences and recovery](../artifacts/task-002/ui/probe-quota-recovered.png)

An additional read-only call to the compiled production provider at 09:30:20 was cross-checked with .NET `DriveInfo`: both reported **498,566,426,624 total bytes**, **203,626,971,136 free bytes**, the same user-available bytes and NTFS. Evidence: `artifacts/task-002/live-drive-crosscheck.json`. Values are a snapshot and can change due to other processes. Quota behavior is verified with fakes; actual quotas were not modified on this machine.

## Safety, preservation and limits

- The provider uses only the five documented read-only metadata queries. There is no directory/file enumeration, directory walking, file-content access, target mutation, cleanup, Recycle Bin call, WMI, device-control request or arbitrary drive selection. Volume-root/identity resolution queries the OS; it does not walk directory trees or follow entries in a scanner.
- No Administrator requirement, UAC, privilege helper, service, driver, scheduled task, auto-start, background daemon, telemetry or runtime network use was introduced. No Windows/security setting was changed or tool installed. The manifest and all existing project/build/package settings are unchanged. The only app preference writes were the already-supported CleanSpace theme saves during verification, restored to System.
- Compiled API guards, strict metadata-port fakes and source review support the no-enumeration/no-mutation conclusion for the new adapter. No host-wide filesystem trace was collected, so this is not a claim of zero Windows/.NET/build/cache writes. Builds and tests create ordinary workspace artifacts; existing settings tests use their accepted isolated fixtures. No new destructive test fixture or real user-folder mutation test was added.
- SHA256 preservation evidence records all 80 initially tracked files: only the four listed existing files changed, with 76 unchanged. No package pin, application project, manifest, startup smoke observer, settings adapter or TASK-001 report changed. See `artifacts/task-002/before.json`, `preservation.json` and `final-runtime.json`. No commit, staging, push, deployment or publication occurred.
- Native calls are synchronous inside a worker task. Cancellation prevents subsequent queries/publication but cannot interrupt a Windows query already in progress. This implementation has no claim of bounded native-call latency.
- Real not-ready/denied/missing-volume states, non-C Windows installations and quota differences were covered by deterministic fake ports without changing this host's drive state; the live host itself has C:\ Windows and equal volume/user free values. Filesystem-name availability is optional. Values beyond supported signed 64-bit byte range are rejected.
- Verification covers this existing Windows host's Debug/win-x64 unpackaged app, Light/Dark and current System preference, and approximately 894–1634 pixel widths. Other hosts, Release/distribution, smallest possible windows, screen-reader/high-contrast certification and reaction to changing Windows theme settings during a session were not verified. Windows preferences were not changed for testing.

**No TASK-002 acceptance blocker was observed. Recommend ACCEPT TASK-002, subject to architectural review. Stop here; TASK-003, scanning and cleanup remain unimplemented.**
