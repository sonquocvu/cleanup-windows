# TASK-001 implementation and verification

## Final production acceptance verification — 2026-10-17

The approved `Lock` → `ProtectedDocument` correction has been applied. The production EXE passed three independent launches with normal exit code 0; the solution builds without warnings, all 39 tests pass, and the required UI checks pass. See [TASK-001-FIX-06-REPORT.md](TASK-001-FIX-06-REPORT.md) for exact evidence, scope and limitations. Recommendation: **ACCEPT TASK-001**, pending architectural review. The earlier findings below are retained as historical diagnostic evidence.

## Runtime stabilization review — 2026-10-17

**Current status: conditionally passed, NOT fully accepted.** The actual post-manifest-update executable was rebuilt without source changes and still crashed before opening a window. A second launch after the test-only adapter seam reproduced the same native failure. No production WinUI, manifest, entry-point, bootstrapper, deployment, SDK, or package-version change was justified or applied during this review. The solution now builds with **0 warnings / 0 errors** and **39 tests pass**. The previous NU1900 warnings cleared after a fresh, network-accessible restore with security auditing explicitly enabled.

### Phase A: unchanged build and actual executable

The existing app has `OutputType=WinExe`, `net10.0-windows10.0.19041.0`, `RuntimeIdentifier=win-x64`, `PlatformTarget=x64`, `UseWinUI=true`, `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, and `WindowsAppSdkDeploymentManagerInitialize=false`. Evaluated properties additionally show `AppxPackage=false`, `EnableMsixTooling=false`, `SelfContained=false` (.NET uses the installed 10.0.10 runtime), and `WindowsAppSdkUndockedRegFreeWinRTInitialize=true`. `WindowsAppSdkBootstrapInitialize` is unset; a self-contained app does not need the framework bootstrapper. No explicit bootstrapper call exists in CleanSpace.

The generated `App.g.i.cs` entry point has `[STAThread]`, initializes WinRT COM wrappers, calls `Microsoft.UI.Xaml.Application.Start`, installs the dispatcher synchronization context, and constructs App. The startup composition root is unchanged. App settings are loaded only after resolving and activating MainWindow; the isolated probe below reproduces the failure without this code.

Before any source change, this command succeeded, exit **0**, **0 warnings / 0 errors**, **19.46 seconds**:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
dotnet build src/CleanSpace.App/CleanSpace.App.csproj --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
```

The generated `src/CleanSpace.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/CleanSpace.App.exe` was launched directly with `Start-Process -FilePath $exePath -WorkingDirectory (Split-Path -Parent $exePath) -PassThru -WindowStyle Normal`, followed by `$process.WaitForExit(10000)`. This was an **EXE launch**, not a DLL-only launch. No window appeared, and the process exited **-1073741189 (`0xC000027B`)**. EXE SHA256: `F323F1BC4EA85FB338027D0AB9545B7701A2B18DB3D5A7358D227BBD9006EE50`.

| Run (local UTC+07:00) | PID | Result | Application event |
| --- | --- | --- | --- |
| Unchanged app, 14:19:36 | 32484 | Exit -1073741189; no window | Event 1000, record 73116, 14:19:39 |
| Minimal probe, 14:22:34 | 31564 | Remained running; title `WinUI startup probe`; activation marker reached | No matching startup error |
| Probe with only control resources added, 14:24:04 | 21792 | Exit -1073741189; no window | Event 1000, record 73118 |
| Repeated actual app, 14:25:43 | 31384 | Exit -1073741189; no window; token not elevated | Event 1000, record 73120, 14:25:44 |
| Final probe baseline, 14:26:22 | 29968 | Running after ten seconds; window handle 264840; token not elevated | No matching startup error |

A null exit code for a running probe means it had not exited at observation time; it is not a claimed successful process exit. Probe accessibility exposed the expected window and TextBlock. It does not count as CleanSpace GUI acceptance.

### Phase B: diagnosis and evidence limits

Both CleanSpace Event 1000 entries and the failing probe entry identify:

```text
Faulting module: Microsoft.UI.Xaml.dll
Event-log module version: 3.2.3.0
Exception code: 0xc000027b
Fault offset: 0x00000000003a9dbd
Faulting module path (CleanSpace):
E:\01 - Work\cleanup-windows\src\CleanSpace.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Microsoft.UI.Xaml.dll
```

The repeated application's process module list independently confirmed that exact app-local path. The initial report ID was `41c64e43-0bab-4666-b019-f5e752dc4987`; the repeated report ID was `16989b55-431c-4a94-beb0-e77cbb1df063`. Event 1001 record 73117 also described the initial APPCRASH, with a WER problem signature naming `combase.dll` / `802b000a`; this differs from Event 1000's stowed-exception signature and has not been decoded into a root cause. No debugger/dump analysis was performed, and no crash dump was uploaded.

The XAML file reports full version `3.2.3.2609 (ED416C845EFA(ContainerAdministrator)-89b7ad5b68f9f77311e09f3fc5eef5acb5f2460a)`, with a valid Microsoft Authenticode signature. SHA256 comparisons of CleanSpace output, probe output, and the restored WinUI 2.3.9 package's x64 native files matched exactly:

| File | SHA256 in all three locations |
| --- | --- |
| `Microsoft.UI.Xaml.dll` | `AAD12524765E6FB63F0AE26A45A9BA3104F24FDE66413D8A3036FBED74A1E990` |
| `Microsoft.UI.Xaml.Controls.pri` | `5EDB56C0E4688A74CECC4297CB21BD3AAF9F4FBBD2EFD18A1C49464744EF8CF3` |

The embedded executable manifest was extracted using the restored Windows SDK's x64 `mt.exe`:

```powershell
$mtPath = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools/10.0.26100.4654/bin/10.0.26100.0/x64/mt.exe'
$exePath = Join-Path (Get-Location) 'src/CleanSpace.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/CleanSpace.App.exe'
& $mtPath -nologo "-inputresource:$exePath;#1" '-out:artifacts/runtime/embedded-app.manifest'
```

Extraction exited **0**. The embedded manifest contains `requestedExecutionLevel=asInvoker`, `uiAccess=false`, Windows 10 supportedOS GUID `{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}`, `maxversiontested=10.0.26100.0`, and `PerMonitorV2`. Manifest registration-free activation entries map Application to `Microsoft.UI.Xaml.dll` and XamlControlsResources to `Microsoft.UI.Xaml.Controls.dll`. These declarations were present in the actual EXE; no additional manifest edit was made.

The deployment configuration is consistent with Microsoft's [self-contained deployment guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps), [unpackaged C# guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app), and [auto-initializer guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/project-properties): the SDK runtime is app-local, framework bootstrap is unnecessary, and unused Main/Singleton deployment initialization is opted out. No manual bootstrap call or runtime installation was added.

The linked [WinUI issue 12169](https://github.com/microsoft/microsoft-ui-xaml/issues/12169) reports the same DLL version/outer exception/offset during theme parsing. It remains an investigation reference, **not proof of an identical root cause**. CleanSpace's stowed exception was not decoded, so its inner HRESULT and parser stack are unknown.

The diagnostic-only `diagnostics/WinUiStartupProbe` has no CleanSpace references, application XAML, DI, or settings. Its baseline constructs Application, Window and TextBlock and activates normally. `--controls` adds only `new XamlControlsResources()` to Application.Resources before creating the window. The failing run's final stage marker was:

```text
Probe: application constructed
Probe: before XamlControlsResources construction
```

It did not reach `controls resources added`, window construction, or the managed UnhandledException marker. This localizes the reproduction to native control-resource initialization independently of CleanSpace's shell and settings. It does **not** establish whether the pinned runtime payload, an OS/runtime interaction, or another lower-level condition causes the parser failure. The basic baseline's success also rules out claiming that all WinUI startup is broken on this machine. No speculative removal of required Fluent resources, package substitution, security change, or production workaround was applied.

### Phase C: GUI acceptance

| Acceptance check | Status | Evidence / limit |
| --- | --- | --- |
| Actual application opens on repeated launches | **FAIL** | Both actual EXE launches exited 0xC000027B without a window. |
| Dashboard, Scan Results and Settings navigation | **NOT VERIFIED** | App cannot reach interactive state. |
| Light, Dark and System visibly change | **NOT VERIFIED** | App cannot reach interactive state. |
| Theme persists after app restart | **NOT VERIFIED** | Adapter round trips pass; no GUI restart verification. |
| Quick Scan remains disabled at runtime | **NOT VERIFIED** | Disabled compiled XAML is unchanged; runtime not reached. |
| Cleanup remains disabled at runtime | **NOT VERIFIED** | Disabled compiled XAML is unchanged; runtime not reached. |
| Normal resize and keyboard navigation | **NOT VERIFIED** | App cannot reach interactive state. |
| CleanSpace's known settings directory/file unchanged by failed launch | **PASS (bounded)** | Both absent before and after repeated EXE launch. |
| No unexpected writes anywhere on the machine | **NOT VERIFIED** | No whole-machine I/O tracing was installed or performed. Windows wrote crash events/WER reports. |

The Computer Use skill was used to inspect only diagnostic probe windows. A coordinate-based Close attempt reported unavailable input geometry; keyboard closing and subsequent window listing were used instead where supported. There was no successful CleanSpace window to automate. No GUI check has been inferred from source or unit tests.

### Phase D: adapter integration tests and NuGet auditing

The adapter's public constructor and normal DI behavior retain the same app-owned path. A small internal constructor accepts an absolute settings directory for the friend integration-test assembly; no arbitrary-path option was exposed in the app. All test files are created beneath uniquely named `tests/CleanSpace.IntegrationTests/bin/Debug/net10.0/fixtures/theme-settings/<guid>/` directories with an owner marker and sentinel. Fixtures are retained; there is no delete/move operation, ACL modification, real Temp usage, or real-user settings access.

Nineteen integration cases cover missing settings without directory creation, each supported theme's real JSON persistence across adapter instances, a second save, malformed JSON, unsupported/null values, the 4096-byte read limit, locked read/write failures with safe service fallback, a directory at the settings-file path, and pre-cancelled reads/writes without file changes or new settings directories. Cancellation testing is deterministic at operation entry; atomicity of cancellation after a write has begun is not claimed.

The old NU1900 messages were feed-connectivity/audit-download failures, not advisories identifying vulnerable packages. Microsoft's [NU1900 reference](https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1900) describes that failure mode. The sandbox still failed DNS for a probe restore. A fresh restore outside that restricted sandbox succeeded with `NuGetAudit=true` and `NuGetAuditMode=all`; no warnings were suppressed and auditing was never disabled. A subsequent current direct/transitive vulnerability query exited **0** and returned no vulnerable packages for any of the seven solution projects. This is the feed's result at verification time, not a guarantee about undisclosed vulnerabilities.

Exact successful follow-up commands, from the repository root, with the process-local telemetry/workload variables above:

```powershell
dotnet restore CleanSpace.slnx --disable-parallel --force --no-http-cache --verbosity minimal -m:1 -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet restore diagnostics/WinUiStartupProbe/WinUiStartupProbe.csproj --disable-parallel --force --no-http-cache --verbosity minimal -m:1 -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build CleanSpace.slnx --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet build diagnostics/WinUiStartupProbe/WinUiStartupProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet test CleanSpace.slnx --no-build --no-restore --verbosity normal -m:1 --logger 'trx;LogFilePrefix=runtime-verification' --results-directory artifacts/test-results --blame-hang-timeout 30s
dotnet list CleanSpace.slnx package --vulnerable --include-transitive --no-restore --format json
./diagnostics/Verify-Startup.ps1 -Target App
./diagnostics/Verify-Startup.ps1 -Target Probe
git diff --check
```

The controls failure was first captured with `Start-Process` passing `-ArgumentList '--controls'` to the probe EXE and redirecting stderr. The equivalent saved command for reproduction is `./diagnostics/Verify-Startup.ps1 -Target ProbeControls`. The script itself returns normally after recording a crashed child's exit; **its exit 0 is not an app success**.

| Verification | Result |
| --- | --- |
| Fresh solution/probe restores with all-package audit | Both exit 0; 0 warnings. |
| Seven-project solution build | Exit 0; 0 warnings, 0 errors; 21.85 seconds. |
| Diagnostic probe build | Exit 0; 0 warnings, 0 errors; final controls-capable build 3.44 seconds. |
| Application tests | 13 passed, 0 failed/skipped; reported test time 0.8702 seconds. |
| Core tests | 7 passed, 0 failed/skipped; reported test time 0.8549 seconds. |
| JSON adapter integration tests | 19 passed, 0 failed/skipped; reported test time 2.5998 seconds. |
| Combined solution test command | Exit 0; **39 passed**, 0 warnings/errors; 5.67 seconds. |
| Direct/transitive vulnerability query | Exit 0; no vulnerable packages reported. |
| `git diff --check` and separate untracked-file whitespace checks | Exit 0; no whitespace errors. |

Logs, launch JSON, the extracted manifest, runtime hash comparisons, and audit JSON are under ignored `artifacts/runtime/`. TRX files are under ignored `artifacts/test-results/`.

### Changes in this review and remaining recommendations

| File | Change and rationale |
| --- | --- |
| `CleanSpace.slnx` | Add only the approved settings integration-test project. |
| `src/CleanSpace.Infrastructure/CleanSpace.Infrastructure.csproj` | Friend access for the integration-test assembly. |
| `src/CleanSpace.Infrastructure/Settings/JsonThemeSettingsStore.cs` | Internal absolute-directory test seam; preserve normal public-constructor storage behavior. |
| `tests/CleanSpace.IntegrationTests/CleanSpace.IntegrationTests.csproj` | Reuse existing pinned xUnit/test SDK packages. |
| `tests/CleanSpace.IntegrationTests/JsonThemeSettingsStoreTests.cs` | Nineteen isolated adapter cases; no fixture deletion. |
| `diagnostics/WinUiStartupProbe/WinUiStartupProbe.csproj` | Standalone same-version WinUI reproduction, outside product solution. |
| `diagnostics/WinUiStartupProbe/Program.cs` | Baseline/control-resource variants and path-free stage markers. |
| `diagnostics/WinUiStartupProbe/README.md` | Reproduction instructions and evidence limits. |
| `diagnostics/Verify-Startup.ps1` | Repeatable normal-user EXE/module/exit/event capture and bounded settings snapshots; never kills a process. |
| `README.md` | Current test/audit/startup status and integration-test instructions. |
| `docs/TASK-001-REPORT.md` | This review's evidence and explicit acceptance status. |

No production App/XAML/manifest/deployment property or package pin changed. No scanning, cleanup, privileged helper, network feature, system-file modification, UAC/security change, system-runtime repair, SFC/DISM, global tool install, commit, or push was performed. TASK-002 has not started.

**Remaining blocker:** native controls-resource initialization prevents CleanSpace from opening with the approved pinned runtime. Recommended next review step is running these exact diagnostic binaries on another supported Windows machine and using an available local user-mode debugger to decode the synthetic probe's stowed exception. No `cdb`, `windbg`, or `dotnet-dump` command was found on PATH here; no debugging tools were installed. Any package-version experiment or supported servicing change requires separate review; none was performed here. GUI acceptance must be rerun after a justified resolution.

## Historical foundation evidence (superseded by the review above)

Recorded on 2026-10-17. Scope is TASK-001 only. No TASK-002 code, commit, push, packaging, publication, global tool installation, or security setting change was performed.

### Initial outcome

The six-project foundation, WinUI shell, MVVM state, DI composition root, application-owned JSON appearance settings, and unit test scaffolds are implemented. Windows compilation and all 20 unit tests passed. **TASK-001 is not fully accepted:** interactive launch/navigation/theme acceptance remains blocked. Initial launches terminated in `Microsoft.UI.Xaml.dll` with exception code `0xc000027b`. A compatibility manifest update compiled successfully afterward; the user stopped Computer Use with the physical Escape key before that executable could be tested. No further UI automation was performed.

### Initial architecture decisions

- Core targets plain `net10.0` and contains the supported theme values and validated immutable settings. It has no NuGet packages or Windows/UI dependencies.
- Application targets `net10.0`, references Core, defines the settings storage port, and handles theme load/save defaults and failure notices. Cancellation is propagated; notices do not expose exception messages or paths.
- Infrastructure targets `net10.0`, references Application and Core, and implements JSON storage for one fixed application-owned settings path. It performs no file enumeration. Existing reparse-point components are rejected, file reads are size-bounded, and settings errors are handled by Application.
- App targets `net10.0-windows10.0.19041.0`, `win-x64`. App references Application and Infrastructure; Infrastructure is used only at the DI composition root. WinUI wiring stays in code-behind, navigation state lives in MainViewModel, and appearance state/commands live in SettingsViewModel. Toolkit observable partial properties avoid WinRT/AOT analyzer warnings.
- The shell uses native NavigationView automatic pane behavior and a scrollable content area. Dashboard and Scan Results are placeholders, Cleanup is visibly disabled, and Quick Scan is disabled. Settings offers System/Light/Dark and an explicit Apply button. These UI behaviors compiled but have not been observed interactively.
- DI registers only the settings adapter/service, view models, window, and logging infrastructure without output providers. There is no telemetry, file logger, hosted service, scanner, or cleanup registration.
- Development uses an unpackaged executable with app-local Windows App SDK files. This avoids installing a Windows App Runtime or changing the machine. Distribution remains deferred, consistent with PRODUCT.md. The executable manifest requests `asInvoker` with `uiAccess=false`.
- Central package management pins exact stable versions, including the architecture's specified Windows App SDK 2.5.1, which restored successfully. No preview packages were selected.

### Initially observed Windows environment

| Component | Observed value |
| --- | --- |
| OS | Windows x64, 10.0.26300 |
| PowerShell | 7.6.5 |
| Selected .NET SDK | 10.0.302, commit 35b593bebf |
| MSBuild | 18.6.11+35b593beb (`dotnet msbuild -version` also reported 18.6.11.33009) |
| .NET host/runtime | 10.0.10, x64 |
| Other installed SDK | 8.0.421 |
| Workloads | None installed, per unsandboxed `dotnet --info` |
| Visual Studio | `devenv`/standalone `msbuild` not on PATH; `vswhere.exe` absent at the standard Installer path; no usable installation found in inspected locations |
| WinUI templates | `dotnet new list winui` found none |
| Machine Windows SDK | Standard Windows Kits 10 Include/bin directories absent; UnionMetadata/Facade alone was present |
| Restored Windows SDK tooling | Microsoft.Windows.SDK.BuildTools 10.0.26100.4654; Microsoft.Windows.SDK.BuildTools.MSIX 1.7.251221100; Microsoft.Windows.SDK.NET.Ref 10.0.19041.57 |
| Windows App SDK | Microsoft.WindowsAppSDK 2.5.1 restored; app-local runtime copied by the build |

The initial sandboxed `dotnet --info` failed during its Service Control Manager query with access denied. The later unsandboxed, read-only inventory succeeded with exit code 0. No service was installed, started, stopped, or modified. Windows App Runtime registration could not be inventoried with sandboxed `Get-AppxPackage` because access was denied; an installed shared runtime is not required by this development configuration.

Directly referenced package pins: WindowsAppSDK 2.5.1, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection 10.0.10, Microsoft.Extensions.Logging 10.0.8, Microsoft.NET.Test.Sdk 18.8.1, xunit 2.9.3, and xunit.runner.visualstudio 3.1.5.

### Initial exact verification commands and results

All commands ran in `E:/01 - Work/cleanup-windows` on the Windows .NET toolchain. Sessions set `DOTNET_CLI_TELEMETRY_OPTOUT=true` and `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true`; these were process-local variables, not machine settings.

| Command | Observed result |
| --- | --- |
| `dotnet sln CleanSpace.slnx list` | Exit 0; all six approved projects listed. |
| `dotnet restore CleanSpace.slnx --disable-parallel --verbosity minimal -m:1` | Outside the network sandbox: exit 0; Windows App SDK 2.5.1 restored, five remaining projects up to date; NU1900 warnings from the two test-project audit caches. |
| `dotnet build CleanSpace.slnx --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false` | Exit 0; all six projects built; 0 errors, 2 NU1900 vulnerability-feed warnings, no code/XAML warnings; 15.39 seconds after replacing observable fields with partial properties. |
| `dotnet build src/CleanSpace.App/CleanSpace.App.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false` | Final application build after the manifest update: exit 0; 0 warnings, 0 errors; 13.75 seconds. |
| `dotnet build src/CleanSpace.Infrastructure/CleanSpace.Infrastructure.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false` | Exit 0; Core/Application/Infrastructure compiled; 0 warnings, 0 errors; 3.96 seconds. |
| `dotnet build tests/CleanSpace.Application.Tests/CleanSpace.Application.Tests.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false` | Exit 0; 0 errors, 1 cached NU1900 warning; 2.62 seconds. |
| `dotnet test tests/CleanSpace.Core.Tests/CleanSpace.Core.Tests.csproj --no-build --no-restore --verbosity normal --diag artifacts/core-tests.log --blame-hang-timeout 30s` | Outside the process sandbox: exit 0; **7 passed, 0 failed, 0 skipped**; reported test time 1.4132 seconds. |
| `dotnet test tests/CleanSpace.Application.Tests/CleanSpace.Application.Tests.csproj --no-build --no-restore --verbosity normal --diag artifacts/application-tests.log --blame-hang-timeout 30s` | Outside the process sandbox: exit 0; **13 passed, 0 failed, 0 skipped**; reported test time 2.2637 seconds. |
| `git diff --check` | Exit 0; no whitespace errors. New source/configuration files were also checked separately. |

The tests cover default and invalid themes, supported theme values, save/load through an in-memory store, malformed/unreadable settings, failed saves retaining the prior theme, path-free notices, and cancellation without unintended store calls. They do not touch real user folders or destructive fixtures. They do not verify the JSON adapter on disk or the WinUI UI.

Earlier unsuccessful attempts are retained as limitations, not counted as passes:

- Default multi-worker sandbox restores/builds sometimes returned exit 1 with no diagnostic (`Build FAILED`, 0 warnings/0 errors in the diagnostic restore log). `-m:1` produced actionable results and a successful build.
- Sandboxed WinUI restore with `-m:1` failed NU1301 because DNS could not resolve `api.nuget.org`. An authorized restore outside the sandbox succeeded. Audit checks were not disabled or suppressed.
- Sandboxed Core tests compiled but aborted because VSTest could not connect to testhost within 90 seconds. The subsequent sandboxed Application test attempt also returned exit 1 without a test result. Both compiled test assemblies ran successfully outside the sandbox with the commands above.
- Two pre-manifest-update launches, including `dotnet src/CleanSpace.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/CleanSpace.App.dll`, produced no targetable window. Read-only Application event log entries identified `Microsoft.UI.Xaml.dll`, version 3.2.3.0, exception `0xc000027b`. This does not establish the root cause or prove the final manifest update fixes it.

Local diagnostic logs under `artifacts/` and build outputs under `bin/` and `obj/` are ignored, not source changes.

### Initial interactive checks

| Check | Status |
| --- | --- |
| Initial launch attempt | Failed to expose a window; Windows recorded a WinUI startup crash. |
| Launch after final compatibility manifest update | Not performed; the user stopped Computer Use. |
| Dashboard / Scan Results / Settings navigation | Not observed. |
| Cleanup / Quick Scan unavailable at runtime | Not observed; disabled states are declared in compiled XAML. |
| Light / Dark / System visual switching | Not observed. |
| Theme persistence across app restarts | Not observed; in-memory application behavior only was unit-tested. |
| Keyboard, contrast, narrow-window layout | Not performed. |

### Initial changed files

All paths below are relative to the repository root. Existing planning documents, task specifications, and AGENTS.md were not changed.

| Files | Purpose |
| --- | --- |
| `.gitignore` | Ignore local build outputs, test diagnostics, and IDE files. |
| `global.json` | Select the installed stable .NET 10 SDK feature band. |
| `Directory.Build.props` | Enable nullable references, implicit usings, analyzers, and deterministic builds. |
| `Directory.Packages.props` | Centrally pin exact package versions. |
| `NuGet.Config` | Use the explicit NuGet.org package source. |
| `CleanSpace.slnx` | Reference the four runtime projects and two test projects. |
| `README.md` | Replace planning-only status with actual build/test/run instructions and limitations. |
| `docs/TASK-001-REPORT.md` | This task's evidence, decisions, safety review, and complete changed-file inventory. |
| `src/CleanSpace.Core/CleanSpace.Core.csproj` | Platform-independent Core project. |
| `src/CleanSpace.Core/Models/ThemePreference.cs` | Supported System/Light/Dark values. |
| `src/CleanSpace.Core/Models/ThemeSettings.cs` | Validated immutable preference and System default. |
| `src/CleanSpace.Application/CleanSpace.Application.csproj` | Application project with inward Core reference. |
| `src/CleanSpace.Application/Abstractions/IThemeSettingsStore.cs` | Async, cancellable appearance storage port. |
| `src/CleanSpace.Application/DTOs/ThemeSettingsResult.cs` | Theme state and safe user notice. |
| `src/CleanSpace.Application/UseCases/ThemeSettingsService.cs` | Load/save orchestration, defaults, errors, cancellation. |
| `src/CleanSpace.Infrastructure/CleanSpace.Infrastructure.csproj` | Infrastructure references Application and Core. |
| `src/CleanSpace.Infrastructure/Settings/JsonThemeSettingsStore.cs` | App-owned JSON appearance settings adapter. |
| `src/CleanSpace.App/CleanSpace.App.csproj` | WinUI 3, x64, app-local SDK runtime, runtime project references. |
| `src/CleanSpace.App/app.manifest` | Standard-user execution, DPI and OS compatibility declarations. |
| `src/CleanSpace.App/App.xaml` | WinUI control resources. |
| `src/CleanSpace.App/App.xaml.cs` | Startup and DI composition root. |
| `src/CleanSpace.App/MainWindow.xaml` | Responsive native navigation and visibly unavailable scan/cleanup controls. |
| `src/CleanSpace.App/MainWindow.xaml.cs` | Navigation and per-window theme wiring. |
| `src/CleanSpace.App/ViewModels/MainViewModel.cs` | Shell navigation and placeholder presentation state. |
| `src/CleanSpace.App/ViewModels/SettingsViewModel.cs` | Theme selection, Apply command, busy/error presentation. |
| `src/CleanSpace.App/Views/SettingsView.xaml` | Theme picker, Apply button, accessible status. |
| `src/CleanSpace.App/Views/SettingsView.xaml.cs` | Settings view binding wiring. |
| `tests/CleanSpace.Core.Tests/CleanSpace.Core.Tests.csproj` | xUnit Core scaffold. |
| `tests/CleanSpace.Core.Tests/ThemeSettingsTests.cs` | Seven Core test cases. |
| `tests/CleanSpace.Application.Tests/CleanSpace.Application.Tests.csproj` | xUnit Application scaffold. |
| `tests/CleanSpace.Application.Tests/ThemeSettingsServiceTests.cs` | Thirteen in-memory Application test cases. |

### Initial safety and remaining review

No disk scanning, drive metrics, filesystem enumeration, delete/move/recycle capability, shell cleanup, registry access, elevated manifest, privileged helper, auto-start, telemetry, or application network code was added. The only deliberate runtime write is to CleanSpace's own theme setting. Package network access was limited to NuGet restore. Build outputs and diagnostic logs stay in the repository's ignored output directories. No system/user data was used as a mutation fixture, and no Windows security feature was changed.

The final UI startup behavior and the remaining manual checks need review before TASK-001 can be considered fully complete. The unpackaged x64 development configuration is documented; MSIX distribution is deferred. No additional task has been started.
