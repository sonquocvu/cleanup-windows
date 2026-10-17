# TASK-001-DIAG-02 — WinUI native crash investigation

Date: 2026-10-17, UTC+07:00. Status: **diagnostic review complete; root cause NOT established; TASK-001 remains blocked**. Production source, project/deployment configuration, manifest and package pins were preserved. No SDK/deployment-mode/other-machine experiment was executed.

## Main finding: the constructor is not reached

The previous probe evaluated this combined expression:

```csharp
Resources.MergedDictionaries.Add(new XamlControlsResources());
```

Its stage marker preceded the entire expression. C# evaluates the receiver before the argument, so that marker did not prove a constructor failure. Diagnostic-only markers now split the expression, preserving that order:

```csharp
var resources = Resources;
var dictionaries = resources.MergedDictionaries;
var controls = new XamlControlsResources();
dictionaries.Add(controls);
```

The final run stopped at **`before Application.Resources access`**. It did not reach `Application.Resources accessed`, the merged-dictionaries getter, the constructor, insertion, or window construction. The narrowest observed failing operation is therefore **the Application.Resources getter on the UI thread during Application construction**, under the existing app-local WinUI configuration. This supersedes the earlier interpretation that XamlControlsResources construction itself was isolated as the failing operation. It does not identify which native resource operation inside the getter failed.

## Reproduction evidence

Environment retained from TASK-001: Windows x64 10.0.26300, .NET SDK 10.0.302/MSBuild 18.6.11, .NET runtime 10.0.10, Windows App SDK 2.5.1. Reproduction used actual probe EXEs, normal user tokens (`Elevated=false`), the existing manifest and package graph. The probe has no CleanSpace assembly references, application XAML, DI or settings.

| Run | Local time / PID | Observed result |
| --- | --- | --- |
| Unchanged baseline | 15:24:34 / 22700 | Window handle 1246714, expected title and activation marker; running after 10 seconds, so exit code was null at that observation. Later closed normally; that later observer did not obtain a numeric exit code. |
| Unchanged `--controls` | 15:24:44 / 22180 | Exit **-1073741189 (`0xC000027B`)**, no window; Event 1000 record **73128**, 15:24:45. |
| Finer diagnostic markers, `--controls` | 15:30:05 / 6252 | Exit **-1073741189 (`0xC000027B`)**, no window; Event 1000 record **73130**, 15:30:06; failed at Resources access. |
| Final instrumented baseline | 15:32:14 / 30408 | Window and activation confirmed; remained open at 10 seconds, then **exit 0** after normal Alt+F4 closure. Original process handle was retained to obtain that exit code. |

Both failing Event 1000 records identify `Microsoft.UI.Xaml.dll`, version **3.2.3.0**, exception **0xC000027B**, offset **0x3A9DBD**. The loaded/faulting path is `<repo>/diagnostics/WinUiStartupProbe/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Microsoft.UI.Xaml.dll`, not a shared WindowsApps copy. Event report IDs are `a3875ca5-dcbb-4f3a-b89e-a81a14ed69b7` and `211a047a-ec20-4a25-b06c-b2c5b53b6224` respectively.

No controls dictionary or required resource was removed from production. The without-resources baseline is a diagnostic control, not a product workaround.

## Native exception findings

### Existing artifacts and available tooling

Read-only inventory found matching archived WER `Report.wer` files and existing application dumps under `%LOCALAPPDATA%/CrashDumps`. The repro also produced dumps through the machine's **already existing** crash-reporting configuration; no settings were queried or changed in the registry, and no dump-generation mechanism was configured by this task.

No `cdb`, `windbg`, `windbgx`, `ntsd`, `dotnet-dump`, `dumpbin`, LLVM binary reader, or objdump command was found on PATH. The inspected standard Windows Kits x64 debugger/dumpchk paths were absent, and the Microsoft.WinDbg package query returned no entry. This is a bounded inventory, not proof that no debugger exists anywhere on the machine. Python **3.14.5** is installed; third-party `pefile` and `minidump` modules are absent. Nothing was installed.

A small offline standard-library reader was added to inspect only explicit local files. It validates the minidump header/AMD64 architecture, stream locations, virtual-memory coverage, stowed structure size/signature, and bounded counts. It follows the dump exception's pointer array to documented SE02 records; it does not search arbitrary strings for a matching HRESULT. Eight synthetic tests cover known records/trace addresses, adjacent and absent memory, invalid headers/RVAs, privacy filtering, and distinguishing unrelated exception codes. **All eight passed.** This reader is not an independent debugger or symbolic stack unwinder.

The record layout follows Microsoft's [stowed exception structure](https://learn.microsoft.com/en-us/windows/win32/wer/stowed-exception-information-v2), [header signatures](https://learn.microsoft.com/en-us/windows/win32/wer/stowed-exception-information-header), and [minidump exception stream](https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/ns-minidumpapiset-minidump_exception_stream) documentation. Unknown private nested formats and uncaptured ranges remain undecoded.

### HRESULTs actually read from these dumps

| Local dump | Outer exception / address | Captured stowed ResultCodes |
| --- | --- | --- |
| Probe PID 21792, preceding review | `0xC000027B`, `Microsoft.UI.Xaml.dll+0x3A9DBD` | Three SE02 records, each **0x8000FFFF**. |
| Probe PID 22180, unchanged repro | Same | Three SE02 records, each **0x8000FFFF**. |
| Probe PID 6252, finer markers | Same | Three SE02 records, each **0x8000FFFF**. |
| CleanSpace PID 31384, preceding review | Same | **0x802B000A**, **0x802B000A**, **0x80004005**. |

These values are local observations, not borrowed from the public issue. They demonstrate that the same outer exception/address can contain different stowed records. They do not identify the failed resource or make all records one causal chain. The archived probe WER summary names `combase.dll+0x5CF34` with `8000ffff`; the CleanSpace summary names that address with `802b000a`. This is consistent with the first stowed record in each inspected dump, while Event 1000 reports the final outer failure.

The first stowed record's exception address is `combase.dll+0x5CF34`. Nested type `0x314F454C` denotes LEO1; other records include `0x4C4D4158` (XAML). These nested language/XAML objects were not decoded. No private format or message was guessed.

### Native trace availability and limits

Most recorded stack pointers refer to virtual memory omitted from these minidumps. The older probe dump contains one readable four-entry stowed trace: three entries map to `Microsoft.UI.Xaml.dll+0x3205C0`, `+0x12FCA2`, and `+0x12FC1B`; the fourth does not map to a captured module range. These are **stored trace entries**, not a freshly unwound or fully symbolized call stack. The newer probe dumps did not contain readable trace buffers for those records.

No matching local PDB was found in the inspected WinUI/InteractiveExperiences/Foundation package directories. No symbol-server query or download occurred. This task therefore cannot name `Parser::CreateXamlBinaryFileReader` as a verified CleanSpace/probe frame, nor establish an internal XBF/parser stack from the public issue. Missing memory and private nested records remain material limitations.

All dumps stayed at their existing local paths. Only selected metadata and module-relative addresses were written to ignored local diagnostic JSON. No memory dump, raw memory string, user-specific path, or private diagnostic artifact was sent to an external service.

## Deployment, dependencies and resource integrity

The configuration remains Windows App SDK self-contained and unpackaged: `WindowsAppSDKSelfContained=true`, `WindowsPackageType=None`, `AppxPackage=false`, registration-free WinRT initialization enabled, unused Deployment Manager initialization disabled. **.NET `SelfContained=false` is a separate axis:** the installed .NET runtime is used; this does not make the app-local WinUI payload framework-dependent.

The probe's extracted embedded manifest includes standard-user `asInvoker`/`uiAccess=false`, the existing OS compatibility declaration (`maxversiontested=10.0.26100.0`), and the expected Application/XamlControlsResources registration-free classes. Extraction using the restored SDK's `mt.exe` exited **0**. Nothing was added to the production manifest or bootstrapper. The preceding [Microsoft deployment guidance comparison](TASK-001-REPORT.md) remains applicable.

The selected native modules actually loaded from the baseline's app-local directory include Microsoft.UI.Xaml, WindowsAppRuntime, Windowing.Core, FrameworkUdk, and CoreMessagingXP. System CRT/FrameworkUdk.System modules loaded from System32. The checked DLLs had **valid Microsoft signatures**. PE32+ import inspection, without loading the inspected files, found AMD64 (`0x8664`) images and normal WinUI/Windows/CRT imports. XAML's delayed dependencies include Xaml.Internal and Windows UI/system libraries. API-set names were treated as loader contracts, not incorrectly reported as missing physical files. No complete loader-resolution or all-delay-import proof is claimed.

The following output hashes matched the corresponding unchanged package-cache files:

| File | SHA256 |
| --- | --- |
| `Microsoft.UI.Xaml.dll` | `AAD12524765E6FB63F0AE26A45A9BA3104F24FDE66413D8A3036FBED74A1E990` |
| `Microsoft.UI.Xaml.Controls.dll` | `ED5D153C099092E9927639AFAB70545CB48D5CA1B6FA79FDDFAFF0D4F3CA5651` |
| `Microsoft.UI.Xaml.Internal.dll` | `1AAB9CF0EEC4C1DC3D046D03C8A38B4DC214DC2B7BD892DC41095B4A4BECA4F0` |
| `Microsoft.UI.Xaml.Controls.pri` | `5EDB56C0E4688A74CECC4297CB21BD3AAF9F4FBBD2EFD18A1C49464744EF8CF3` |
| `CoreMessagingXP.dll` | `049A1379592513D05BADA39D43A46124833C2E5F6EC2EEC299927339E12D57DF` |

CoreMessagingXP is 1,062,712 bytes, version **10.0.27200.1054**, signature **Valid**. XAML and Controls report full file version 3.2.3.2609; Xaml.Internal reports its packaged 3.2.0.0 version. Cache matching does not independently prove the published payload is defect-free.

The already installed SDK's `makepri dump` read the Controls PRI successfully, exit **0**, producing one resource map and 176 named resources. This establishes that the index is readable by that tool; it does not prove which resource WinUI selected, whether an embedded XBF is semantically valid for the parser, or whether all resource dependencies resolve correctly. No runtime resource file was modified.

## Public issue comparison

Source: [WinUI issue 12169](https://github.com/microsoft/microsoft-ui-xaml/issues/12169), reviewed 2026-10-17.

| Aspect | Public evidence versus this investigation |
| --- | --- |
| Similar signature | The original report names Runtime 2.5.1, XAML 3.2.3.0, outer 0xC000027B, offset 0x3A9DBD and inner 0x8000FFFF. Our probe independently matches those values. |
| Native stack | The reporter provides a named parser/theme-loading stack. We have only partial, unsymbolized stored trace addresses. |
| Deployment distinction | An October 8 contributor comment says self-contained MUX worked and failures appeared confined to framework-dependent/inbox apps. Our app-local self-contained WinUI repro fails, so that observation does not establish our cause. |
| Separate dependency report | Another commenter describes earlier loader failures and an unsigned/damaged CoreMessagingXP copy. Our checked copy is signed, versioned, cache-matching, and supports baseline startup. |

The issue is relevant comparative evidence, not a confirmed diagnosis or workaround for this project. “Self-contained” must refer to the WinUI deployment axis; the exact public test binary/configuration is not available here for a controlled comparison.

## Root-cause hypotheses

Confidence below is confidence in the stated hypothesis, not proof from a matching exception code.

| Hypothesis | Confidence | Supporting evidence | Missing or contrary evidence |
| --- | --- | --- | --- |
| Failure boundary is native Application.Resources initialization | **High, localization only** | Finer markers stop at that getter; same independent probe reproduces. | Native operation/resource inside the getter remains unknown. |
| CleanSpace UI/DI/settings code causes this repro | **Low** | No supporting defect found. | Probe references none of those components and fails before the controls constructor. |
| WinUI resource loading/parsing defect or runtime/resource incompatibility | **Medium** | Getter boundary, stowed errors, valid/cache-matching payload and readable PRI. | No symbolic native stack, failing resource identifier, or resource-access trace. |
| Basic packaging/bootstrap omission or accidental mismatched copied files | **Low for checked items** | Embedded manifest, app-local loaded modules, baseline startup, signatures and hashes agree. | Complete resource resolution and every delay-loaded dependency were not traced. |
| Resource-specific deployment/configuration problem | **Unresolved / medium plausibility** | Failure occurs only when application resources initialize. | No alternate same-version deployment control has been run. |
| Machine/OS/runtime interaction | **Unresolved / medium plausibility** | Only this host has been tested. | No identical-binary run on a second supported machine; cannot call it machine-specific yet. |
| Same root cause as issue 12169 | **Low / unproven** | Some independently matched signature values. | Public self-contained result differs; our named stack is unknown; app stowed codes also differ. |
| Damaged CoreMessagingXP as in the separate comment | **Low for our checked copy** | None found. | Valid signature/version, cache match and functioning baseline argue against that specific damage. |

Evidence favors investigating the resource-initialization path. It currently cannot separate a runtime defect, resource-specific deployment issue, and host interaction with sufficient confidence to choose a fix.

## Architecture impact: six answers

1. **Application-code defect?** No evidence implicates CleanSpace business/UI orchestration code in the isolated failure. The previous diagnostic localization was too broad and has now been corrected.
2. **App-local deployment correct?** The inspected configuration, manifest, loaded DLL paths and payload checks are consistent with the intended deployment. Full resource-resolution correctness is not established by those checks alone.
3. **Narrowest repro?** Reading Application.Resources in the UI-thread Application constructor on this host/configuration. The controls constructor is not reached.
4. **Missing root-cause evidence?** A symbolic native stack, readable private XAML error context/failing resource details, and a controlled deployment or second-host comparison.
5. **Fix without sacrificing required resources?** No justified fix has been demonstrated. No evidence requires abandoning Fluent/WinUI; omitting resources is only a diagnostic control and is not an acceptable production workaround.
6. **Next lowest-risk step?** Review the approval-only plan below, starting with offline symbolization of existing local dumps using suitable local tooling. The current read-only tools have not established the root cause.

## Isolated experiment plan — NOT executed; approval required

| Proposed step | Single-variable control and prerequisites | Evidence to capture / stop condition |
| --- | --- | --- |
| Offline native analysis | Existing probe dump on this machine, local user-mode debugger and exact matching symbols. If tools/symbol downloads must be obtained, approve that separately; no installation or download was done here. No dump upload, registry changes or automatic dump configuration. | Resolve saved frames and private XAML context where possible. Stop and report omitted memory; do not invent a parser stack. |
| Same binary on another supported Windows host | Exact hashed probe output folder; same installed .NET 10 runtime; normal user; no runtime installation or repair. This requires approval/access to the second host. | Both modes, loaded paths/hashes, exit codes and events. If prerequisites differ, do not treat it as a controlled machine comparison. |
| Same SDK, different WinUI deployment mode | Separate diagnostic copy only. Keep .NET deployment and target framework unchanged. Framework-dependent trial only if the required runtime already exists; do not install it. | Verify actual loaded runtime version/hashes before comparing; bootstrap resolution may otherwise select a different compatible runtime. Reject mismatched comparisons. |
| Architect-selected different SDK version | Separate diagnostic project and local central-version file; production pins untouched. Exact stable candidate must be selected and restore approved first. | Keep code, host and deployment mode fixed; capture payload identity and both outcomes. No production upgrade/downgrade follows automatically. |

No alternative SDK, deployment mode, host, locale, compatibility flag, or system setting was tested. No production resources were removed. There is no recommendation to repair Windows, run SFC/DISM, elevate, or replace system files.

## Commands, checks and changed files

Successful commands ran on the actual Windows toolchain from the repository root. Process-local .NET telemetry/workload-update variables were set as in the existing task instructions.

```powershell
dotnet build diagnostics/WinUiStartupProbe/WinUiStartupProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
./diagnostics/Verify-Startup.ps1 -Target Probe
./diagnostics/Verify-Startup.ps1 -Target ProbeControls
python -B -m unittest discover -s diagnostics -p test_inspect_native.py -v
python -B diagnostics/inspect_native.py pe './diagnostics/WinUiStartupProbe/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Microsoft.UI.Xaml.dll'
$dumpRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CrashDumps'
python -B diagnostics/inspect_native.py dump (Join-Path $dumpRoot 'WinUiStartupProbe.exe.21792.dmp')
python -B diagnostics/inspect_native.py dump (Join-Path $dumpRoot 'CleanSpace.App.exe.31384.dmp')
python -B diagnostics/inspect_native.py dump (Join-Path $dumpRoot 'WinUiStartupProbe.exe.22180.dmp')
python -B diagnostics/inspect_native.py dump (Join-Path $dumpRoot 'WinUiStartupProbe.exe.6252.dmp')
git diff --check
```

PE inspection was also run against Controls, CoreMessagingXP and WindowsAppRuntime; all four reads exited 0. All four dump reads exited 0. Native tool paths and exact `mt.exe`/`makepri.exe` arguments are retained locally in the command evidence:

```powershell
$sdkBin = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools/10.0.26100.4654/bin/10.0.26100.0/x64'
# With $probeExe and $priPath set to the existing probe EXE and Controls PRI:
& (Join-Path $sdkBin 'mt.exe') -nologo "-inputresource:$probeExe;#1" '-out:artifacts/diag-02/probe-embedded.manifest'
& (Join-Path $sdkBin 'makepri.exe') dump /if $priPath /of 'artifacts/diag-02/controls-pri.xml'
```

Initial unchanged probe build: exit **0**, 0 warnings/errors, **1.70 seconds**. One instrumented rebuild was attempted while the baseline was still running and failed **MSB3027/MSB3021** after file-lock retries (10 warnings, 2 errors). The probe was closed normally; the same build then succeeded, exit **0**, **0 warnings/errors**, **1.55 seconds**. No process was killed. Reader tests: **8 passed**, exit **0**. Production unit tests were not rerun in this diagnostic-only task; the preceding 39-pass result remains historical evidence, not a new claim. Whitespace checks passed for tracked and new diagnostic files.

| File changed in DIAG-02 | Purpose |
| --- | --- |
| `diagnostics/WinUiStartupProbe/Program.cs` | Split diagnostic markers without changing original evaluation order or runtime configuration. |
| `diagnostics/WinUiStartupProbe/README.md` | Correct failure localization; explain offline reader and limitations. |
| `diagnostics/inspect_native.py` | Explicit-file, read-only PE/AMD64 minidump metadata reader; no third-party packages/network. |
| `diagnostics/test_inspect_native.py` | Eight in-memory synthetic parser checks; no real-user fixture mutations. |
| `docs/TASK-001-DIAG-02-REPORT.md` | This diagnostic review, evidence limits and approval-only plan. |

Source hashes captured before work confirm no changes to production source, package pins, projects, manifest, global SDK selection or existing tests. The only pre-existing code file changed is the diagnostic Program.cs; the README and new files above are the other task changes. Metadata, dependency inventories, resource-index dump and preservation check are beneath ignored `artifacts/diag-02`; launches are under ignored `artifacts/runtime`. The dumps were neither copied into the repository nor uploaded.

**Acceptance blocker remains:** CleanSpace cannot initialize required WinUI application resources on the tested configuration. Startup/GUI acceptance is not passed. No feature, scanner, cleanup operation, privilege elevation, tool installation, Windows/security modification, commit, push or TASK-002 work was performed. Stop for architectural review.
