# TASK-001-COMPAT-03 — isolated WinUI SDK compatibility test

Date: 2026-10-17, UTC+07:00. **Experiment complete; TASK-001 remains blocked.** Changing the isolated probe from Windows App SDK 2.5.1 to 2.4.0 did **not** change Application.Resources initialization behavior on this host.

| SDK | Normal-window mode | Resource-access mode |
| --- | --- | --- |
| 2.5.1 | **3/3** activated; normal close, exit **0** | **3/3** crashed before the getter returned; exit **0xC000027B** |
| 2.4.0 | **3/3** activated; normal close, exit **0** | **3/3** crashed before the getter returned; exit **0xC000027B** |

All **12 runs** completed under non-elevated tokens, with the intended app-local runtime identity independently recorded from `Process.Modules` and matched against the resolved package payload. No run timed out or required process termination. There is no observed startup benefit that would justify evaluating a production downgrade to 2.4.0 as a fix on this evidence.

## Preservation and configuration

Before edits, `git status --porcelain=v1 -uall` was saved to `artifacts/compat-03/working-tree-before.txt`. The working tree already contained the uncommitted foundation/diagnostic work; it was neither reset nor committed. SHA256 baselines were saved for all **51 pre-existing tracked and non-ignored untracked files**. Final comparison found **zero changes** to those files, including production source, root package pins, manifests, existing diagnostics, tests and global SDK selection.

Phase A rebuilt and ran the preserved 2.5.1 probe in both modes before the alternative project was created. The comparison uses a separate `diagnostics/WinUiStartupProbe240` project, outside `CleanSpace.slnx`. Its closest central-version file pins `[2.4.0]` and does not import/edit production `Directory.Packages.props`. The alternate project links the **same existing Program.cs**, rather than a modified copy, and the same executable manifest. Source SHA256 in every run: `4FD6117CF6EDD5EF54BDD1C4570B7729C5ED7507F125BC61858F402088AEA2A7`.

Host/toolchain: Windows x64 10.0.26300, .NET SDK **10.0.302**, MSBuild **18.6.11**, installed .NET **10.0.10**, same user and session. No OS/runtime installation or configuration change occurred. The trials ran from 16:12:56 to 16:17:08 with normal UI closure afterward, including interleaved SDK runs. No material instability or interruption was observed.

### Configuration comparison

| Setting | Preserved 2.5.1 baseline | Isolated 2.4.0 comparison |
| --- | --- | --- |
| Project | `diagnostics/WinUiStartupProbe/WinUiStartupProbe.csproj` | `diagnostics/WinUiStartupProbe240/WinUiStartupProbe240.csproj` |
| Microsoft.WindowsAppSDK pin | Root `[2.5.1]`, unchanged | Project-local `[2.4.0]` |
| Resolved Runtime package | 2.5.1 | 2.4.0 |
| Resolved WinUI component | 2.3.9 | 2.3.6 |
| Resolved Foundation component | 2.3.12 | 2.3.9 |
| Resolved InteractiveExperiences component | 2.1.9 | 2.1.6 |
| TargetFramework | `net10.0-windows10.0.19041.0` | Same |
| TargetPlatformMinVersion | `10.0.19041.0` | Same |
| PlatformTarget / RuntimeIdentifier | `x64` / `win-x64` | Same |
| OutputType / UseWinUI | `WinExe` / `true` | Same |
| WindowsPackageType / AppxPackage | `None` / `false` | Same |
| WindowsAppSDKSelfContained | `true` | Same |
| .NET SelfContained | `false` | Same; installed .NET runtime used |
| Deployment Manager initialization | `false` | Same |
| Registration-free WinRT initialization | `true` | Same |
| EnableMsixTooling | `false` | Same |
| AssemblyName / RootNamespace / TargetName | `WinUiStartupProbe` | Same, set explicitly to preserve identity |
| ApplicationManifest | `../../src/CleanSpace.App/app.manifest` | Same file and content |
| Embedded user manifest settings | `asInvoker`, `uiAccess=false`, existing OS GUID, `maxversiontested=10.0.26100.0`, PerMonitorV2 | Same; extracted declarations compared equal |
| Application PRI map name | `WinUiStartupProbe` | Same; both readable with SDK makepri |
| Windows SDK BuildTools | 10.0.26100.4654 | Same |
| Probe logic and stage instrumentation | Existing Program.cs | Linked identical file |
| Outputs / working directory | Baseline project's own `bin/.../win-x64` | Comparison project's own `bin/.../win-x64` |

All queried effective runtime/build properties matched. The separate project name/path and local central-version file provide isolation; explicit AssemblyName/RootNamespace settings counteract the changed project filename's defaults. No compatibility flag or production configuration fix was needed. Generated SDK manifests, native components, projected references and binaries naturally differ as a consequence of selecting the alternate SDK package graph; this is a metapackage/payload comparison, not a single-DLL replacement test.

## Actual runtime identity and hashes

Repository root for the paths below is `E:/01 - Work/cleanup-windows`.

- **A output:** `E:/01 - Work/cleanup-windows/diagnostics/WinUiStartupProbe/bin/Debug/net10.0-windows10.0.19041.0/win-x64`.
- **B output:** `E:/01 - Work/cleanup-windows/diagnostics/WinUiStartupProbe240/bin/Debug/net10.0-windows10.0.19041.0/win-x64`.

The actual executable is `WinUiStartupProbe.exe` and the actually loaded WinUI file is `Microsoft.UI.Xaml.dll` beneath the applicable output directory. Every run JSON contains their complete absolute paths and individually captured SHA256 values. No run used a shared WindowsApps XAML DLL or the other SDK's output directory. Hashes were constant within each SDK's six runs.

| File | A: SDK 2.5.1 SHA256 | B: SDK 2.4.0 SHA256 |
| --- | --- | --- |
| Actual EXE | `D9BFA9D539E8D87CE79452916E15BBC0902D19357D7BFBA477EA169F970B5ABD` | `B7F918EC8FADE5A37C456D34247280DA7B6E3FAAFB3BBF661F882C16CE4F1F37` |
| Managed probe DLL | `5E42948CE1F358513B925D323CD4BCB91AC79F55236D9671703AB02828E5292F` | `BF6795CDDF6FFED67741933FAF86F961203B2D0AADCB665803E180460BF6796C` |
| Actually loaded XAML DLL | `AAD12524765E6FB63F0AE26A45A9BA3104F24FDE66413D8A3036FBED74A1E990` | `B241F72BFC9460E823D8A122FBDBC2E2D7AC6BA60B9E5BF981E9B5054D4555B5` |
| Controls PRI | `5EDB56C0E4688A74CECC4297CB21BD3AAF9F4FBBD2EFD18A1C49464744EF8CF3` | Identical |
| CoreMessagingXP DLL | `049A1379592513D05BADA39D43A46124833C2E5F6EC2EEC299927339E12D57DF` | `C6D3A66183DA33B558ABB14CC83C609048A8405C29FBF8AB3EE35E27D36C440A` |

| Native file version | A: SDK 2.5.1 | B: SDK 2.4.0 |
| --- | --- | --- |
| XAML DLL full build | **3.2.3.2609**, commit `89b7ad5b68f9f77311e09f3fc5eef5acb5f2460a` | **3.2.3.2608**, commit `aa188bfdf74608810e8e57228db6dd137f4e74e4` |
| XAML DLL length | 15,324,472 bytes | 15,323,960 bytes |
| Event-log XAML version | 3.2.3.0 | 3.2.3.0 |
| CoreMessagingXP version | 10.0.27200.1054 | 10.0.27200.1048 |

The inspected XAML, Controls, WindowsAppRuntime and CoreMessagingXP DLL signatures were **Valid** in both folders. Per-run XAML hashes matched the correct resolved WinUI component's `runtimes-framework/win-x64/native` package-cache file. Resolved SDK and Runtime packages were exactly 2.4.0 for B; the superficially similar Event Log version was not used as proof of payload identity. Thus this is a valid comparison of the requested SDK payloads.

Both ship a WinUI build labeled 3.2.3 and an identical Controls PRI. This limits the conclusion: the experiment does not compare independent WinUI generations or rule out a defect in shared resource data/code. No semantic equivalence of faulting native functions is inferred from nearby offsets.

## Per-run results

Identity **A/B** refers to the actual paths and constant, per-run-verified hashes above. Run IDs map directly to `artifacts/compat-03/<SDK>-<Variant>-<Run>.json` and companion stderr logs. All windows were also observed through their accessibility tree before normal Alt+F4 closure; no process was killed.

Last-marker keys:

- **W:** `Probe: activated` — activation marker, nonzero window handle, expected title and accessible TextBlock observed.
- **R:** `Probe: before Application.Resources access` — the getter did not return; controls construction/insertion and activation were not reached.

| Identity / SDK | Variant / run | Start UTC+07:00 | PID | Child exit code | Window activated | Last marker | Event 1000 record / offset |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A / 2.5.1 | Window 1 | 16:12:56 | 17444 | **0** | Yes | W | None |
| A / 2.5.1 | Resources 1 | 16:13:21 | 25252 | **-1073741189** | No | R | 73132 / 0x3A9DBD |
| A / 2.5.1 | Resources 2 | 16:14:50 | 25228 | **-1073741189** | No | R | 73134 / 0x3A9DBD |
| B / 2.4.0 | Resources 1 | 16:15:03 | 30440 | **-1073741189** | No | R | 73136 / 0x3A9C5D |
| B / 2.4.0 | Window 1 | 16:15:38 | 9276 | **0** | Yes | W | None |
| A / 2.5.1 | Window 2 | 16:15:58 | 30860 | **0** | Yes | W | None |
| B / 2.4.0 | Resources 2 | 16:16:20 | 23704 | **-1073741189** | No | R | 73138 / 0x3A9C5D |
| B / 2.4.0 | Window 2 | 16:16:24 | 2996 | **0** | Yes | W | None |
| A / 2.5.1 | Resources 3 | 16:16:43 | 3412 | **-1073741189** | No | R | 73140 / 0x3A9DBD |
| A / 2.5.1 | Window 3 | 16:16:48 | 4108 | **0** | Yes | W | None |
| B / 2.4.0 | Resources 3 | 16:17:03 | 16728 | **-1073741189** | No | R | 73142 / 0x3A9C5D |
| B / 2.4.0 | Window 3 | 16:17:08 | 4372 | **0** | Yes | W | None |

All six resource failures have exception **0xC000027B**, faulting module **Microsoft.UI.Xaml.dll**, event module version **3.2.3.0**. Event ModulePath matches the corresponding app-local output. The fault offset is consistent within each binary build and differs between builds. Every failure event was correlated by PID; every successful window run had no matching Application error in its observation interval. Full event data and stage output are retained locally. No dump or private diagnostic data was uploaded.

## Exact commands and observed command results

Commands ran from the repository root on Windows PowerShell. The only NuGet download authorization used was for the isolated 2.4.0 project. No global runtime, Windows package, workload or diagnostic tool was installed.

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
git status --porcelain=v1 -uall
dotnet build diagnostics/WinUiStartupProbe/WinUiStartupProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.5.1 -Variant Window -Run 1
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.5.1 -Variant Resources -Run 1
dotnet restore diagnostics/WinUiStartupProbe240/WinUiStartupProbe240.csproj --disable-parallel --verbosity minimal -m:1 -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build diagnostics/WinUiStartupProbe240/WinUiStartupProbe240.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.5.1 -Variant Resources -Run 2
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.4.0 -Variant Resources -Run 1
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.4.0 -Variant Window -Run 1
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.5.1 -Variant Window -Run 2
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.4.0 -Variant Resources -Run 2
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.4.0 -Variant Window -Run 2
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.5.1 -Variant Resources -Run 3
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.5.1 -Variant Window -Run 3
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.4.0 -Variant Resources -Run 3
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.4.0 -Variant Window -Run 3
git diff --check
```

| Command/check | Result |
| --- | --- |
| Baseline build | Exit **0**; 0 warnings/errors; **1.81 seconds**. |
| Isolated 2.4.0 restore | Exit **0**; **43.16 seconds**; no warning reported, auditing enabled for all packages. |
| Isolated 2.4.0 build | Exit **0**; 0 warnings/errors; **5.30 seconds**. |
| Twelve observation-script invocations | Each returned **0** after recording its child result. Wrapper success does not mean a crashed child succeeded; use the per-run table. |
| Matrix validation | Exactly 12 completed, non-elevated runs; three in each SDK/mode group; all intended payload matches. |
| Effective-property queries | Both exit **0**; queried runtime/build settings equal. |
| SDK `mt.exe` extraction | Both exit **0**; embedded user declarations equal. |
| SDK `makepri.exe` reads | Both exit **0**; both application resource maps named `WinUiStartupProbe`. |
| Preserved-file hash comparison | **0 pre-existing files changed**, including baseline and production. |
| Whitespace checks | `git diff --check` and separate new-file checks passed. |

Property queries used `dotnet msbuild <project> -getProperty:<comma-separated-properties> -nologo`; queried values are in the configuration table and local `*-properties.json`. Manifest/resource checks used the already restored Windows SDK 10.0.26100.4654 tools:

```powershell
# For each SDK's actual $exePath and $outDir:
& (Join-Path $sdkBin 'mt.exe') -nologo "-inputresource:$exePath;#1" "-out:artifacts/compat-03/$sdk-embedded.manifest"
& (Join-Path $sdkBin 'makepri.exe') dump /if (Join-Path $outDir 'WinUiStartupProbe.pri') /of "artifacts/compat-03/$sdk-probe-pri.xml"
```

`Get-FileHash` and `Get-AuthenticodeSignature` read the local runtime files. Evidence includes the working-tree snapshot, preserved hashes, per-run checkpoints/results/stderr, resolved package lists, effective properties, extracted manifests, PRI dumps and payload integrity under ignored `artifacts/compat-03`. Existing run IDs are not overwritten. No production unit-test rerun was needed or claimed for this diagnostic-only addition; the twelve real-process runs are this task's behavioral verification.

## Validity and uncertainties

**Valid within the requested scope:** code/instrumentation/source hash, host, .NET SDK, target, architecture, executable identity, user manifest declarations, WinUI deployment mode and .NET deployment mode match. Outputs are isolated. Each process actually loaded the intended XAML build and its hash matched its own resolved package. All runs completed consistently, including baseline runs after comparison runs.

The controlled independent input is the Windows App SDK metapackage version and its dependency payload. Its expected generated manifest/reference/native changes are part of that input; this experiment cannot attribute a cause to one individual changed DLL. The common Controls PRI and shared WinUI 3.2.3 lineage remain common inputs. Three repetitions per case establish repeatability here, not a statistical reliability guarantee across machines.

This task did not collect a new symbolic stack or decode further stowed records, trace the exact selected XBF/resource, test a second machine, change deployment mode, or validate full CleanSpace GUI behavior. Similar outer exception codes do not prove identical internal root causes. The host remains a possible contributor, not a proven machine-specific defect.

### Updated hypotheses

| Hypothesis | Current confidence / change | Reason |
| --- | --- | --- |
| Application.Resources is the observed failure boundary | **High** | All six resource runs stop before that getter returns; all six without that access activate. |
| Regression unique to SDK 2.5.1, fixed by SDK 2.4.0 | **Low / reduced** | The intended 2.4.0 payload also fails 3/3. |
| Shared WinUI resource initialization defect or common resource input | **Medium; remains plausible** | Both builds share lineage/Controls PRI and the trigger; no native root-cause evidence yet. |
| OS/host interaction with this resource path | **Medium plausibility, unproven** | Failure persists across the two package payloads on one host; no second-host control. |
| Resource-specific unpackaged deployment/configuration issue common to both probes | **Unresolved** | Deployment settings match; no alternate-deployment or actual resource-resolution trace. |
| Wrong 2.5.1 runtime accidentally loaded by the comparison | **Low for these runs** | All B runs loaded B's app-local file with the intended distinct 2.4.0-component hash. |
| CleanSpace business/UI orchestration defect | **Low for this isolated failure** | Neither probe references production projects or their services. |

## Six analysis answers and recommendation

1. **Baseline still reproduces?** Yes, resource access fails 3/3 on preserved 2.5.1; normal-window controls pass 3/3.
2. **Resources fails on 2.4.0?** Yes, 3/3, with the intended payload actually loaded.
3. **Consistent across runs?** Yes for exit, activation, stage boundary and each build's event signature; no incomplete runs.
4. **Version-dependent behavior?** No observed difference in initialization outcome between these SDK versions. Binary identity and fault offset differ, which does not constitute an initialization fix.
5. **Uncertainties?** The precise failing native operation/resource and the contribution of common code/data, deployment and host state remain unresolved.
6. **Production SDK change worth evaluating separately?** This experiment provides **no startup-fix justification for changing production to 2.4.0**. Keep production pins unchanged. Any future package change needs independent motivation and approval, not an inference of success from the comparison's build.

Recommended next architectural decision: prioritize a symbolized analysis of the existing local probe crash evidence or an approved identical-binary run on a second supported host, as planned in DIAG-02, rather than a production downgrade. Those steps were not executed in COMPAT-03. Required Fluent resources remain in CleanSpace.

### Files added in this task

| File | Purpose |
| --- | --- |
| `diagnostics/WinUiStartupProbe240/Directory.Packages.props` | Isolated exact stable 2.4.0 pin. |
| `diagnostics/WinUiStartupProbe240/WinUiStartupProbe240.csproj` | Separate same-logic, same-manifest diagnostic project with independent outputs. |
| `diagnostics/WinUiStartupProbe240/README.md` | Reproduction, capture and evidence-retention instructions. |
| `diagnostics/compat-03/Observe-Probe.ps1` | Normal-user actual-process observation, runtime identity verification, PID-correlated events and retained exit status; no process killing or UI input. |
| `docs/TASK-001-COMPAT-03-REPORT.md` | This experiment's complete result and review recommendation. |

No pre-existing source file or package pin was edited. No scan/cleanup feature, registry change, privilege elevation, Windows servicing, global runtime installation, production resource removal, commit, push or TASK-002 work was performed. **Compatibility experiment complete; production startup and TASK-001 acceptance remain blocked. Stop for architectural review.**
