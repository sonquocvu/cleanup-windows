# TASK-001-FIX-06 — Production startup fix and final acceptance

Verified on **2026-10-17**, on the existing Windows host. **Recommendation: ACCEPT TASK-001**, subject to architectural review. The approved production correction passes three real EXE launches and the required UI checks. The full solution builds with **0 warnings / 0 errors** and **39/39 tests pass**. TASK-002 was not started.

## Approved production patch

The only production source change is in `src/CleanSpace.App/MainWindow.xaml`:

```diff
-                <NavigationViewItem Content="Cleanup (unavailable)" IsEnabled="False" Icon="Lock"
+                <NavigationViewItem Content="Cleanup (unavailable)" IsEnabled="False" Icon="ProtectedDocument"
```

This matches `diagnostics/isolate-05/proposed-production.patch`. Cleanup remains disabled. The accepted ISOLATE-05 investigation identified the invalid Symbol conversion during `MainWindow.InitializeComponent()`. The correction retains the icon and required Fluent resources. No startup ordering, DI, manifest, theme architecture, other production XAML, or package pin was changed.

## Host, configuration and preservation

- Windows x64 `10.0.26300`; .NET SDK `10.0.302`; MSBuild `18.6.11+35b593beb`; installed .NET runtime `10.0.10`; PowerShell `7.6.5`.
- Target remains `net10.0-windows10.0.19041.0`, `win-x64` / x64; assembly identity `CleanSpace.App`; unpackaged `WindowsPackageType=None`.
- Windows App SDK remains centrally pinned to **[2.5.1]**, resolving `Microsoft.WindowsAppSDK.WinUI/2.3.9`. `WindowsAppSDKSelfContained=true`, `.NET SelfContained=false`, reg-free initialization true and deployment-manager initialization false remain unchanged.
- Initial HEAD was `a1c7293c65c572da11cd789c15e3792bd12e2138` (`Create UI`), following `783b52e` (`initial commit`). HEAD is unchanged. No commits, staging, resets or pushes were performed.
- The pre-existing untracked `diagnostics/isolate-05/` and `docs/TASK-001-ISOLATE-05-REPORT.md` were preserved. SHA256 snapshots cover all **77 pre-existing non-ignored files**. Only the approved MainWindow file and the requested short reference in the original TASK-001 report changed among those files.

## Build, tests and NuGet audit

Commands ran from `E:\01 - Work\cleanup-windows`. Logging used `Tee-Object` into `artifacts/fix-06/`; native command exit codes were captured. Network restore, tests and desktop launch observations used the normal-user host session outside the restricted process sandbox, without an elevated Windows token or UAC.

```powershell
dotnet restore CleanSpace.slnx --disable-parallel --force --no-http-cache --verbosity minimal -m:1 -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build CleanSpace.slnx --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet test CleanSpace.slnx --no-build --no-restore --verbosity normal -m:1 --logger 'trx;LogFilePrefix=fix-06' --results-directory artifacts/fix-06/test-results --blame-hang-timeout 30s
dotnet list CleanSpace.slnx package --vulnerable --include-transitive --no-restore --format json
```

| Verification | Exit | Exact result |
| --- | --- | --- |
| Forced restore, security audit enabled | 0 | All seven projects restored; no warnings, including no NU1900 |
| Full solution rebuild | 0 | 0 warnings, 0 errors; MSBuild elapsed **21.74 s** |
| All existing tests | 0 | **39 passed, 0 failed, 0 skipped**; MSBuild elapsed **7.46 s** |
| Direct and transitive vulnerability query | 0 | All seven projects returned no reported vulnerable packages; source `https://api.nuget.org/v3/index.json` |

| Test assembly | Passed | Failed / skipped | Reported test time |
| --- | --- | --- | --- |
| CleanSpace.Application.Tests | 13 | 0 / 0 | 2.0847 s |
| CleanSpace.Core.Tests | 7 | 0 / 0 | 2.0927 s |
| CleanSpace.IntegrationTests | 19 | 0 / 0 | 2.1260 s |

The existing JSON adapter integration tests include persistence across store instances, malformed/oversized input, read/write failures and cancellation. No test implementation or production settings adapter changed in this task. TRX results are retained in `artifacts/fix-06/test-results/`; console evidence is in `restore-solution.log`, `build-solution.log`, `test-solution.log` and `vulnerability-audit.json` under that artifact directory.

NuGet auditing stayed enabled. The previously reported NU1900 feed-connectivity warnings did **not** recur; they were not suppressed or treated as vulnerability findings. The fresh restore and the separate vulnerability query succeeded on this run. This is a point-in-time audit result.

## Generated output and package/configuration drift

The generated **production** `MainWindow.xbf` contains `ProtectedDocument`, contains no `Lock` literal, and is byte-identical to the validated ISOLATE-05 corrected candidate. The running app also successfully loads that MainWindow XAML. Its SHA256 is:

```text
EFE221154F1F83807C378CB0EFC05E14BBE6D5161D05EEF26645D2432EB86048
```

Evidence: `artifacts/fix-06/generated-xaml-verification.json`. A successful XAML compilation alone was not used as acceptance evidence.

The final check repeated the following read-only output inspection:

```powershell
$xbfPath = Join-Path $PWD 'src/CleanSpace.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/MainWindow.xbf'
$xbfBytes = [IO.File]::ReadAllBytes($xbfPath)
$xbfText = [Text.Encoding]::Unicode.GetString($xbfBytes) + [Text.Encoding]::UTF8.GetString($xbfBytes)
$xbfHash = (Get-FileHash -LiteralPath $xbfPath).Hash
```

`$xbfText.Contains('ProtectedDocument')` was true and `$xbfText.Contains('Lock')` was false. `artifacts/fix-06/final-verification.json` also records a fresh comparison of every project's current `obj/project.assets.json` libraries against the original snapshot: **7 projects, 0 library differences**, plus script parser errors **0** and diff-check exit **0**.

Before/after resolved-library snapshots for all seven projects are identical (`packages-before.json` / `packages-after.json`). Central package management, project files, solution, manifest, resources and build configuration hashes are unchanged. Evaluated properties were checked before and after with:

```powershell
dotnet msbuild src/CleanSpace.App/CleanSpace.App.csproj -getProperty:NuGetAudit,NuGetAuditMode,TargetFramework,RuntimeIdentifier,PlatformTarget,AssemblyName,WindowsPackageType,WindowsAppSDKSelfContained,SelfContained,WindowsAppSdkUndockedRegFreeWinRTInitialize,WindowsAppSdkDeploymentManagerInitialize
git diff --check
git diff --stat
git diff -- src/CleanSpace.App/MainWindow.xaml
```

The property command exited **0**, with identical before/after JSON, including `NuGetAudit=true` and `NuGetAuditMode=all`. `git diff --check` exited **0**. Git emitted its existing LF-to-CRLF conversion notices; there were no whitespace errors. The production diff is exactly the approved one-line substitution. Preservation evidence: `artifacts/fix-06/preservation-before.json` and `preservation-after.json`.

## Actual production EXE acceptance

Each run launched this exact EXE with its own directory as the working directory, without settings overrides or diagnostic instrumentation:

```text
E:\01 - Work\cleanup-windows\src\CleanSpace.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\CleanSpace.App.exe
```

Exact launch commands (each console log includes `SmokeExitCode=0`):

```powershell
./diagnostics/Verify-ProductionStartup.ps1 -RunId Production-1 2>&1 | Tee-Object artifacts/fix-06/Production-1-console.log; $smokeExit=$LASTEXITCODE; "SmokeExitCode=$smokeExit"; exit $smokeExit
./diagnostics/Verify-ProductionStartup.ps1 -RunId Production-2 2>&1 | Tee-Object artifacts/fix-06/Production-2-console.log; $smokeExit=$LASTEXITCODE; "SmokeExitCode=$smokeExit"; exit $smokeExit
./diagnostics/Verify-ProductionStartup.ps1 -RunId Production-3 2>&1 | Tee-Object artifacts/fix-06/Production-3-console.log; $smokeExit=$LASTEXITCODE; "SmokeExitCode=$smokeExit"; exit $smokeExit
```

These commands were executed in three separate shell invocations. The observer rejects elevated tokens and existing CleanSpace processes. Each launch had a stable window, visible Dashboard, subsequent working UI, and a normal Alt+F4 close. No process was terminated by the observer.

All three runs used these identical payload identities:

| File | SHA256 |
| --- | --- |
| CleanSpace.App.exe | `E012D241826000E4A2513D420068E25445B8DEEB5366227880A8A682B58D5D66` |
| CleanSpace.App.dll | `11F5D918B7D9E409B11E98ECC59F8E6E90AD92FAE55EF77735EF47CF6F24D5E6` |
| MainWindow.xbf | `EFE221154F1F83807C378CB0EFC05E14BBE6D5161D05EEF26645D2432EB86048` |
| Loaded Microsoft.UI.Xaml.dll | `AAD12524765E6FB63F0AE26A45A9BA3104F24FDE66413D8A3036FBED74A1E990` |

`Process.Modules` confirmed the loaded WinUI path on **each** run:

```text
E:\01 - Work\cleanup-windows\src\CleanSpace.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Microsoft.UI.Xaml.dll
```

File version on every run: **3.2.3.2609**, with full version string `3.2.3.2609 (ED416C845EFA(ContainerAdministrator)-89b7ad5b68f9f77311e09f3fc5eef5acb5f2460a)`. The build-account text in that file-version string does not describe the launch token: all launches were non-elevated. The loaded SHA256 matches the intended resolved WinUI package payload. The EXE hash alone is not used to identify the compiled application: managed assembly and XBF hashes are recorded too.

All times below are **UTC+07:00**, 2026-10-17.

| Run | PID / window handle | Start → normal exit observed | Activation | Process / observer exit | Correlated event IDs 1000 / 1026 | Result |
| --- | --- | --- | --- | --- | --- | --- |
| Production-1 | 9200 / 1836522 | 17:21:37.6009460 → 17:23:42.2335456 | PASS | 0 / 0 | None | PASS |
| Production-2 | 5504 / 2361850 | 17:24:17.4524163 → 17:30:23.9465698 | PASS | 0 / 0 | None | PASS |
| Production-3 | 2240 / 723686 | 17:30:30.1165227 → 17:32:03.8771175 | PASS | 0 / 0 | None | PASS |

Windows Application log queries succeeded and were correlated by launch time and PID. A follow-up query at **17:33:32.2297871** again found no correlated failures and **0 remaining production processes**. All three stderr files are empty. Individual JSON results and checkpoints are in `artifacts/fix-06/launches/Production-{1,2,3}.json`; the follow-up result is `final-runtime-event-check.json`.

A minor observer serialization issue in run 1 represented an empty error-ID list as `[null]`. `EventLogQuerySucceeded=true`, `Events=[]`, and the independent final query confirm there was no query failure. The observer's empty-array serialization was corrected before runs 2 and 3; production binaries did not change.

## UI and persistence acceptance

Native GUI automation targeted the actual production process path and window handle. Screenshots were inspected live and accessibility trees retained under `artifacts/fix-06/ui/`. No diagnostic-copy window was counted.

| Requirement | Status | Evidence |
| --- | --- | --- |
| Dashboard opens correctly | **PASS** | Visible title, read-only notice and foundation placeholder on all launches |
| Dashboard, Scan Results and Settings navigation | **PASS** | Mouse navigation on run 1; Down/Enter navigation on run 3 rendered Scan Results and Settings |
| Light Mode applies | **PASS** | Light selected, Apply theme invoked, light surfaces visible and “Appearance saved.” |
| Dark Mode applies | **PASS** | Run 2 used Tab, Alt+Down, End, Enter, Tab and Space to apply Dark; dark surfaces visible |
| System Theme follows Windows | **PASS** | On run 3, explicit Light → System visibly changed to Dark, matching the existing Windows application theme (`AppsUseLightTheme=0`, read only) |
| Theme survives restart | **PASS** | Light saved on run 1 and restored on run 2; Dark saved on run 2 and restored on run 3, including the Settings selection |
| Quick Scan stays disabled | **PASS** | Dashboard displays `Quick Scan (unavailable)`; accessibility reports a disabled button |
| Cleanup stays disabled | **PASS** | Visible unavailable item; accessibility reports disabled; keyboard Down skips it |
| Window resizing | **PASS** | Native Size operation narrowed the window from 1634 to 1125 logical pixels; layout remained usable; maximize and restore also worked |
| Keyboard navigation | **PASS** | Visible focus outlines; navigation arrows/Enter, theme dropdown keys, Tab and Space worked; Alt+F4 closed each run normally |
| Relevant accessible names and states | **PASS** | Named Dashboard/Scan Results/Settings items, App theme combo and Apply theme button; disabled states for both unavailable actions; named informational status content |

Persistence used only the existing production `JsonThemeSettingsStore` and CleanSpace-owned `%LOCALAPPDATA%\CleanSpace\settings.json`. No test-profile override or DI change was introduced. Read-only snapshots confirmed `{"Theme":"Light"}`, then `{"Theme":"Dark"}`, then final `{"Theme":"System"}`. The last value was saved through the app to restore the **initially observed System selection**. The initial restricted-sandbox file-existence observation was not authoritative for the actual desktop user's profile; this report makes no claim that the settings file was initially absent. Actual persistence snapshots were read in the normal-user session.

System verification covers the host's current Dark preference. Reaction to changing Windows preferences during a session is **NOT VERIFIED**: system preferences were not modified. A full screen-reader/high-contrast accessibility certification is also **NOT VERIFIED**; the required control-name, state and keyboard checks above were performed. These are scope limits, not observed acceptance failures.

Two early resize attempts did not establish resizing (one out-of-bounds drag was rejected; a second had no effect). Only the subsequent native Size operation and maximize/restore count as PASS. Some immediate accessibility-tree captures lagged visual transitions; settled screenshots and subsequent state captures were used for conclusions.

## Regression protection

Added `diagnostics/Verify-ProductionStartup.ps1` and `diagnostics/PRODUCTION-STARTUP-SMOKE.md`. The script is a dependency-free, interactive Windows runtime smoke test. It launches the production EXE, requires a stable window and the intended loaded WinUI payload, waits for normal exit, and checks correlated crash events. It returns **0 PASS / 1 FAIL / 2 NOT VERIFIED**, refusing elevation. It never kills the process, writes settings or modifies Windows. PowerShell parser validation reported **0 errors**; its actual runtime execution passed all three acceptance runs.

The unchanged startup resolves MainWindow, executes generated `InitializeComponent()` and then activates the window. A runtime XAML conversion failure such as `Icon="Lock"` therefore fails this check even when compilation succeeds. The accepted ISOLATE-05 investigation supplies the original failing control; this task did not reinsert a known defect into production. The script does not automate/assert page content and is not an unattended CI test. UI checks remain a documented interactive acceptance gate. No new test-package dependencies were added and the script is separate from the existing 39 tests.

## Changed files and remaining issues

| File changed by FIX-06 | Purpose |
| --- | --- |
| `src/CleanSpace.App/MainWindow.xaml` | Exactly the approved icon literal correction; Cleanup remains disabled |
| `diagnostics/Verify-ProductionStartup.ps1` | Actual production startup/exit/runtime/event observer |
| `diagnostics/PRODUCTION-STARTUP-SMOKE.md` | Reproducible regression procedure and its limitations |
| `docs/TASK-001-FIX-06-REPORT.md` | This final evidence and recommendation |
| `docs/TASK-001-REPORT.md` | Short reference to this report, preserving historical findings |

Ignored `artifacts/fix-06/` contains build/test, preservation, GUI-tree, settings and process evidence. Build outputs/intermediates and normal NuGet cache activity are expected. Only CleanSpace's own appearance preference was changed through its UI. No host-wide filesystem tracing was performed; no claim of a complete host write audit is made. No scanner, cleanup, deletion, Recycle Bin operation, privileged helper, registry write, Windows preference/security change, runtime installation, SDK change, unrelated refactor, commit or push was introduced.

**No remaining TASK-001 startup/UI acceptance blocker was observed. Recommendation: ACCEPT TASK-001.** This conclusion is limited to the tested existing-host Debug/win-x64 foundation; installer/distribution, other hosts and future scanner/cleanup work are outside this task. Stop here for architectural review.
