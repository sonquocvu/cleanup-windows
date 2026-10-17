# TASK-001-LIFECYCLE-04 — XAML initialization verification

Date: 2026-10-17, Windows host local time UTC+07:00. Diagnostic task completed; **TASK-001 remains blocked**.

The template-equivalent control **launched successfully in 3/3 runs** with the unchanged Windows App SDK 2.5.1, compiled App.xaml, required XamlControlsResources, generated metadata, and a styled NavigationView. Direct Application.Resources access in its constructor, even after InitializeComponent returned, **crashed in 3/3 runs**. The unchanged production executable still crashed in 3/3 runs.

The existing code-only probe is therefore **not a complete representation of normal WinUI XAML startup**. Its failure does not establish that production fails at the same operation. Production already uses the standard initialization sequence; no production correction is justified by this experiment alone. No production source or package pins were changed.

## Preservation and environment

Read AGENTS.md, PRODUCT, ARCHITECTURE, SECURITY, ROADMAP, the foundation task, all three preceding diagnostic/runtime reports, and the baseline probe README/source. The initial working tree already contained modified README.md and untracked foundation source/configuration/diagnostics/reports. No reset, commit, staging, or push occurred.

Before edits, recorded git status and SHA256 for **56 existing tracked/nonignored files**. Final comparison: **56 unchanged, 0 changed/missing**. This includes all production projects, central package configuration, both earlier SDK probes, their observer, and previous reports. Build-generated bin/obj files were regenerated normally. Local preservation records are in artifacts/lifecycle-04/preservation-before.json and preservation-result.json.

| Item | Observed value |
| --- | --- |
| Windows | x64, 10.0.26300.0 |
| PowerShell | 7.6.5 |
| .NET SDK / MSBuild | 10.0.302 / 18.6.11+35b593beb |
| Test runtime | .NET 10.0.10, x64 |
| Windows App SDK | Microsoft.WindowsAppSDK/[2.5.1], unchanged central pin |
| Resolved WinUI component | Microsoft.WindowsAppSDK.WinUI/2.3.9 |
| XAML compiler generator stamp | 3.0.0.2609 |
| Launch token | Non-elevated; observer rejects administrator tokens |

Some commands used normal-user execution outside the restricted process sandbox for NuGet, interactive launches, Application log access, and test-host IPC. This did not request UAC, change token privileges, or install machine components.

## Production lifecycle audit

| Area | Evidence and assessment |
| --- | --- |
| App.xaml | x:Class matches CleanSpace.App.App. Application.Resources contains ResourceDictionary/MergedDictionaries/XamlControlsResources. No additional application resource dictionaries are declared. The declaration matches the form used in Microsoft's WinUI Gallery. |
| App.xaml.cs | Constructor calls InitializeComponent first, then builds DI registrations. It does **not** directly read Resources or construct/insert XamlControlsResources. OnLaunched resolves MainWindow, registers Closed, activates it, and then loads settings. No custom Program/bootstrapper replaces the generated entry point. |
| App.g.i.cs | Generated STA Main calls InitializeComWrappers, Application.Start, sets DispatcherQueueSynchronizationContext, and constructs App. InitializeComponent uses the _contentLoaded guard and Application.LoadComponent(this, new Uri("ms-appx:///App.xaml")). Debug handlers neither consume exceptions nor alter normal startup. |
| App.g.cs | Companion partial source contains the conditional DISABLE_XAML_GENERATED_MAIN creation hook. That define is absent. Its small size does not mean InitializeComponent is missing: the implementation is in App.g.i.cs. |
| XamlTypeInfo.g.cs | App implements IXamlMetadataProvider; GetXamlType overloads/GetXmlnsDefinitions forward to its generated provider. OtherProviders includes Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider. XamlControlsResources and NavigationView types/activators are generated. No missing provider is apparent from source. This is structural evidence, not an independent runtime COM registration test of production. |
| Build items/compiler | App.xaml is ApplicationDefinition; MainWindow.xaml and Views/SettingsView.xaml are pages. RealBuildPass2, UsingCSWinRT and EnableWin32Codegen are present. The isolated compiled control uses the same compiler path and flags. |
| Compiled resources | Successful makepri dumps show the production primary map CleanSpace.App, Files/App.xbf and Files/MainWindow.xbf; the control map WinUiXamlLifecycleProbe contains its own App.xbf/MainWindow.xbf. Both contain the WinUI resource subtrees. Presence/indexing does not prove every production resource parses correctly. |
| Window lifecycle | Production MainWindow assigns its view model before InitializeComponent, then wires UI events/theme. Window creation/activation occurs in OnLaunched. No inspected production constructor explicitly accesses Application.Resources. |
| Conclusion | Authored ordering and generated startup are structurally consistent with supported C# WinUI examples. There is no demonstrated missing InitializeComponent, generated Main, XAML compilation, or metadata-provider declaration. Production's exact failing runtime stage remains unverified because production instrumentation was prohibited. |

Microsoft's [Windowing App.xaml.cs sample](https://github.com/microsoft/WindowsAppSDK-Samples/blob/main/Samples/Windowing/cs/cs-winui/App.xaml.cs) initializes the component in the App constructor and creates/activates its window in OnLaunched. The [WinUI Gallery App.xaml](https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/App.xaml) merges XamlControlsResources inside Application.Resources. Microsoft's [XamlControlsResources reference](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.xamlcontrolsresources?view=windows-app-sdk-2.0) also documents declaring these styles through application XAML. These sources support the lifecycle/declaration comparison; they do not certify every CleanSpace view or setting.

## Isolated controls and controlled variables

| Configuration | Application XAML / metadata | Explicit resource inspection | Required resources and window |
| --- | --- | --- | --- |
| BaselineWindow | None; original explicit Main | None | Original Window/TextBlock; no control resources |
| BaselineConstructor | None; original explicit Main | Application constructor | Original --controls sequence; crashes before constructing dictionary |
| DeferredLaunched | None; original Main sequence | Same resource block moved to OnLaunched | Resources getter succeeds; XamlControlsResources construction fails |
| XamlLaunched | Real compiled App.xaml; generated Main, InitializeComponent and IXamlMetadataProvider | OnLaunched, after constructor completion | Required resources declared in XAML; real compiled MainWindow/NavigationView |
| XamlConstructor | **Same executable as XamlLaunched** | Additional inspection after InitializeComponent in constructor | Keeps identical compiled resources; crashes at getter before OnLaunched |
| Production | Existing compiled App.xaml and generated provider | No new instrumentation | Existing unchanged CleanSpace executable |

The deferred project's Program.cs is a copy of the baseline with the resource block moved into OnLaunched and one entry marker added. AssemblyName/RootNamespace remain WinUiStartupProbe; outputs are in a separate project directory. The original probe was not edited. There is no manual metadata provider or custom resource workaround.

The XAML control uses a separate namespace/assembly, no DI, settings, CleanSpace assemblies, view models, custom user controls, or x:Bind. Its App.xaml resource declaration has the same structure as production. It uses compiler-generated startup and metadata without custom registration. MainWindow contains a NavigationView, one item and TextBlock. These differences reduce scope for isolation but prevent treating its success as production acceptance or proof that metadata alone explains all failures.

| Evaluated property | Production / baseline / deferred / compiled control |
| --- | --- |
| TargetFramework | net10.0-windows10.0.19041.0 |
| TargetPlatformMinVersion | 10.0.19041.0 |
| PlatformTarget / RuntimeIdentifier | x64 / win-x64 |
| UseWinUI / WindowsPackageType | true / None |
| WindowsAppSDKSelfContained / .NET SelfContained | true / false |
| UndockedRegFreeWinRTInitialize | true |
| BootstrapInitialize / DeploymentManagerInitialize | unset / false |
| Manifest | Same production app.manifest, referenced relatively by diagnostic projects |
| Build configuration | Debug; no new conditional compilation switches |

Extracted production/control EXE manifests both contain asInvoker, uiAccess=false, Windows 10 supportedOS GUID {8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}, maxversiontested=10.0.26100.0 and PerMonitorV2. The standard project additionally retains Platforms=x64, like production; the original baseline has PlatformTarget=x64 without that list property. No runtime/deployment property was changed to make the control work.

## Exact build and launch evidence

All commands below ran from `E:\01 - Work\cleanup-windows`. Logs and per-run records are under artifacts/lifecycle-04. The observer invokes **Start-Process on the generated EXE**, with WorkingDirectory equal to its output folder, PassThru, WindowStyle Normal, and redirected stderr; it retains the process handle before waiting. It does not launch the DLL with dotnet, close windows itself, or kill processes.

```powershell
dotnet restore diagnostics/WinUiXamlLifecycleProbe/WinUiXamlLifecycleProbe.csproj --disable-parallel --verbosity minimal -m:1
dotnet restore diagnostics/WinUiDeferredStartupProbe/WinUiDeferredStartupProbe.csproj --disable-parallel --verbosity minimal -m:1
dotnet build diagnostics/WinUiXamlLifecycleProbe/WinUiXamlLifecycleProbe.csproj --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet build diagnostics/WinUiDeferredStartupProbe/WinUiDeferredStartupProbe.csproj --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet build diagnostics/WinUiStartupProbe/WinUiStartupProbe.csproj --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet build src/CleanSpace.App/CleanSpace.App.csproj --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet build CleanSpace.slnx --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet test CleanSpace.slnx --no-build --no-restore --verbosity normal -m:1 --logger 'trx;LogFilePrefix=lifecycle-04' --results-directory artifacts/lifecycle-04/test-results --blame-hang-timeout 30s
```

| Command/result | Exit and exact result |
| --- | --- |
| dotnet --info inside restricted sandbox | **1**; read-only workload inventory could not open Service Control Manager (access denied). Same command as normal user: **0**. No workload/system change was attempted. |
| Initial XAML-control restore inside restricted sandbox | **1**; NU1301, api.nuget.org host could not resolve; NU1900 audit-feed warning. No package/security fix inferred. |
| Same restore using normal-user network access | **0**, restored in 338 ms, no warnings |
| Deferred restore using normal-user network access | **0**, restored in 309 ms, no warnings |
| XAML-control build | **0**, 0 warnings/errors, 12.17 s |
| Deferred build | **0**, 0 warnings/errors, 5.07 s |
| Unchanged baseline build | **0**, 0 warnings/errors, 4.40 s |
| Unchanged production rebuild | **0**, 0 warnings/errors, 18.48 s |
| Solution build | **0**, 0 warnings/errors, 14.62 s |
| All existing solution tests | **0**, **39 passed, 0 failed, 0 skipped**: Application 13, Core 7, Integration 19; MSBuild elapsed 3.87 s |
| git diff --check | **0**; existing README line-ending conversion notice only. All 10 new files checked separately: 0 trailing-whitespace findings. Observer PowerShell parser: 0 errors. |

No security audit was disabled. The restore warning was a sandbox feed-connectivity failure and disappeared on the successful restore. A fresh vulnerability audit of every production dependency was not part of this lifecycle experiment; build success is not a blanket vulnerability assessment.

Exact observer invocations were these configuration/run combinations, each executed once (order was interleaved as the per-run timestamps record):

```powershell
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration BaselineWindow -Run 1
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration BaselineWindow -Run 2
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration BaselineWindow -Run 3
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration BaselineConstructor -Run 1
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration BaselineConstructor -Run 2
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration BaselineConstructor -Run 3
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration DeferredLaunched -Run 1
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration DeferredLaunched -Run 2
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration DeferredLaunched -Run 3
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration XamlConstructor -Run 1
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration XamlConstructor -Run 2
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration XamlConstructor -Run 3
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration XamlLaunched -Run 1
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration XamlLaunched -Run 2
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration XamlLaunched -Run 3
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration Production -Run 1
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration Production -Run 2
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration Production -Run 3
```

Most invocations redirected the observer's console output to a corresponding *-console.log; its JSON/stderr evidence is identical in shape. Observer script exit **0 for all 18** means collection completed. Child exit codes are reported separately below.

For native read-only inspection, SDK tools were already present beneath the NuGet cache (no installation):

```powershell
$sdkTools = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools/10.0.26100.4654/bin/10.0.26100.0/x64'
& (Join-Path $sdkTools 'makepri.exe') dump /if src/CleanSpace.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/CleanSpace.App.pri /of artifacts/lifecycle-04/production-pri.xml /dt detailed
& (Join-Path $sdkTools 'makepri.exe') dump /if diagnostics/WinUiXamlLifecycleProbe/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUiXamlLifecycleProbe.pri /of artifacts/lifecycle-04/control-pri-final.xml /dt detailed
```

Both final dumps exited **0**. An earlier production dump attempted while the nonincremental rebuild was replacing outputs returned PRI1800 (input file absent); the post-build retry above succeeded. Manifest extraction used mt.exe -nologo with `-inputresource:<actual-exe>;#1` and `-out:artifacts/lifecycle-04/<production-or-control>-embedded.manifest`; both exited **0**. Evaluated-property queries used dotnet msbuild -getProperty and the XAML-item query used -getItem:ApplicationDefinition,Page; all exited **0**, with their full JSON saved locally.

## Executable/runtime identity

For this table, let R = `E:\01 - Work\cleanup-windows`, and O = `bin\Debug\net10.0-windows10.0.19041.0\win-x64`. These are the actual output paths, not published/repackaged copies.

| Configurations | Actual executable | Executable SHA256 | Managed DLL SHA256 |
| --- | --- | --- | --- |
| BaselineWindow / BaselineConstructor | R\diagnostics\WinUiStartupProbe\O\WinUiStartupProbe.exe | D9BFA9D539E8D87CE79452916E15BBC0902D19357D7BFBA477EA169F970B5ABD | 5E42948CE1F358513B925D323CD4BCB91AC79F55236D9671703AB02828E5292F |
| DeferredLaunched | R\diagnostics\WinUiDeferredStartupProbe\O\WinUiStartupProbe.exe | D9BFA9D539E8D87CE79452916E15BBC0902D19357D7BFBA477EA169F970B5ABD | B99CFB5D9D5E3DBECF4BC7739BEE601F81A28B4326652CC7E1ECF7525BFECC2B |
| XamlLaunched / XamlConstructor | R\diagnostics\WinUiXamlLifecycleProbe\O\WinUiXamlLifecycleProbe.exe | FE9F7518DA02EA8DBACD0B1F3C60DB849CFBB6F3468363823324468DB9DADE35 | 9DC2C715C14A0E05490D86C14412423BBEFBB9C0F817901A5929223D1059A971 |
| Production | R\src\CleanSpace.App\O\CleanSpace.App.exe | F323F1BC4EA85FB338027D0AB9545B7701A2B18DB3D5A7358D227BBD9006EE50 | 54A1FBC4AD184F03D2ACFEE13467D6859B3F06A857D3C7784050F5CD1563FF48 |

The baseline/deferred apphosts have equal hashes because they name the same managed assembly; their different managed DLL hashes identify the different code. Every run independently recorded these identities.

Final binary comparison after the solution build matched all 18 recorded EXE/managed-DLL pairs: **0 mismatches**. All 18 children exited; no diagnostic or production test process was left running.

**All 18 runs** sampled Microsoft.UI.Xaml.dll through Process.Modules from the same directory as their respective executable. File version: **3.2.3.2609**, event version **3.2.3.0**. SHA256 in every loaded path: **AAD12524765E6FB63F0AE26A45A9BA3104F24FDE66413D8A3036FBED74A1E990**. Each matched the resolved 2.5.1 package's WinUI/2.3.9 payload; RuntimePayloadMatches=true throughout. The four output DLL signatures are **Valid**. Microsoft.UI.Xaml.Controls.pri in all four output folders has SHA256 **5EDB56C0E4688A74CECC4297CB21BD3AAF9F4FBBD2EFD18A1C49464744EF8CF3**. No shared/global WinUI payload was substituted.

## Per-run results and checkpoints

All crashes below exited **-1073741189 (0xC000027B)**. Each correlated Application **Event 1000** identifies Microsoft.UI.Xaml.dll **3.2.3.0**, exception **c000027b**, fault offset **00000000003a9dbd**, and the app-local module path. There were no matching Event 1026 records. The six successful runs had no matching Event 1000/1026 records in their observation intervals. Matching outer signatures do **not** establish equal inner exceptions or native parsing operations.

| Configuration/run | PID | Child exit | Observed HWND / activation | Event 1000 record |
| --- | ---: | ---: | --- | ---: |
| BaselineWindow-1 | 20900 | 0 | 3932364 / yes | none |
| BaselineWindow-2 | 9808 | 0 | 3934288 / yes | none |
| BaselineWindow-3 | 20984 | 0 | 3934444 / yes | none |
| BaselineConstructor-1 | 19524 | -1073741189 | 0 / no | 73151 |
| BaselineConstructor-2 | 19648 | -1073741189 | 0 / no | 73153 |
| BaselineConstructor-3 | 25844 | -1073741189 | 0 / no | 73155 |
| DeferredLaunched-1 | 13324 | -1073741189 | 0 / no | 73157 |
| DeferredLaunched-2 | 28668 | -1073741189 | 0 / no | 73159 |
| DeferredLaunched-3 | 17444 | -1073741189 | 0 / no | 73161 |
| XamlConstructor-1 | 20072 | -1073741189 | 0 / no | 73149 |
| XamlConstructor-2 | 14232 | -1073741189 | 0 / no | 73163 |
| XamlConstructor-3 | 27716 | -1073741189 | 0 / no | 73165 |
| XamlLaunched-1 | 3376 | 0 | 2886260 / yes | none |
| XamlLaunched-2 | 6904 | 0 | 920208 / yes | none |
| XamlLaunched-3 | 20316 | 0 | 4065496 / yes | none |
| Production-1 | 20864 | -1073741189 | 0 / not observed | 73167 |
| Production-2 | 4404 | -1073741189 | 0 / not observed | 73169 |
| Production-3 | 9104 | -1073741189 | 0 / not observed | 73171 |

| Configuration | Last reached marker / last completed operation, identical across its three runs |
| --- | --- |
| BaselineWindow | `Probe: activated`; window activation returned. Normal UI closure then exit 0. |
| BaselineConstructor | `Probe: before Application.Resources access`; Application construction callback entered, getter never returned. XamlControlsResources constructor never reached. |
| DeferredLaunched | `Probe: unhandled COMException, HRESULT 0x80004005`; Resources and MergedDictionaries getters returned in OnLaunched; final operation marker `before XamlControlsResources construction`, no `constructed`. No exception was handled/suppressed. The resource key/native inner cause is unknown. |
| XamlConstructor | `Lifecycle: before Application.Resources access (constructor after InitializeComponent)`; generated metadata interface=True, InitializeComponent returned. Getter did not return; constructor completion/OnLaunched/window not reached. |
| XamlLaunched | `Lifecycle: window closed normally`; InitializeComponent returned, constructor completed, OnLaunched Resources getter returned, XamlControlsResources present=True, MainWindow.InitializeComponent returned/NavigationView constructed, activated, NavigationView loaded with TemplatePresent=True and VisualChildren=1. |
| Production | No source checkpoints added. Exact failing operation/lifecycle stage **NOT VERIFIED**. EXE/module identity, crash exit, event signature and absence of an observed window are verified. |

In all successful compiled runs, Loaded reported **ApplyTemplate=False; TemplatePresent=True; VisualChildren=1**. ApplyTemplate returning false at Loaded is consistent with the template having already been applied; the non-null template, visual child, and observed NavigationView UI provide the styling evidence. Computer Use observed the NavigationView pane, toggle, diagnostic item, Settings item and text on all three runs. Each surviving baseline/control window was closed normally through Alt+F4; none was killed. Computer Use was not interrupted during these checks.

Successful compiled sequence:

```text
Lifecycle: generated metadata interface = True
Lifecycle: before InitializeComponent
Lifecycle: InitializeComponent returned
Lifecycle: Application constructor completed
Lifecycle: OnLaunched entered
Lifecycle: before Application.Resources access (OnLaunched)
Lifecycle: Application.Resources accessed (OnLaunched)
Lifecycle: XamlControlsResources present = True
Lifecycle: MainWindow.InitializeComponent returned; NavigationView constructed
Lifecycle: activated
Lifecycle: NavigationView loaded; ApplyTemplate=False; TemplatePresent=True; VisualChildren=1
Lifecycle: window closed normally
```

## Historical issue and interpretation

[WinUI issue #7606](https://github.com/microsoft/microsoft-ui-xaml/issues/7606) concerns a hand-created, unpackaged Win32 application on older WinUI/Windows versions. Discussion identifies constructor resource access and missing IXamlMetadataProvider as separate concerns. A timing suggestion did not resolve the author's entire failure; later discussion points to compiled application XAML/provider support. This is evidence to test, not an authoritative guarantee about current runtime internals.

Here, moving the **code-only** resource access to OnLaunched changes the failing operation: getters succeed but dictionary construction fails. The compiled control initializes the required dictionary and NavigationView successfully; the same compiled binary fails when explicitly inspecting resources before the App constructor returns. These observations support both lifecycle sensitivity and the need for a representative compiled-XAML control. They do not isolate metadata as the only difference between code-only and XAML controls, and no historical missing-resource-key message or native stack is attributed to CleanSpace.

The earlier SDK compatibility results remain valid for that particular code-only constructor-access probe. They cannot establish that both SDKs fail normal compiled Application.Resources initialization on this host: the current SDK 2.5.1 compiled control demonstrates otherwise. SDK 2.4.0 was not retested or changed in this task.

## Architecture assessment and hypotheses

| Question | Answer |
| --- | --- |
| Is the minimal probe fully representative? | **No.** It omits application XAML/generated metadata and directly reads resources during construction. Its getter crash is reproducible but insufficient to explain production. |
| Is CleanSpace's generated startup correct? | **Structurally yes** against inspected C# examples and compiler output. Runtime completion of production InitializeComponent/provider resolution is **NOT VERIFIED** without checkpoints. |
| Does the template-equivalent diagnostic launch? | **PASS, 3/3**, with required resources and NavigationView template loading, normal exit 0. |
| Does access timing change the result? | **Yes.** Same compiled EXE: constructor getter fails 3/3; OnLaunched getter succeeds 3/3. Code-only delayed getter also succeeds, then fails at resource construction. |
| Specific initialization/configuration defect? | Demonstrated **probe representativeness/timing defect**. No specific production initialization or deployment defect has been demonstrated. Production may fail in XAML loading, later view construction, bindings, activation or another application/runtime interaction; its phase is not localized. |
| Smallest justified production correction? | **None identified.** Production already declares XAML resources and calls InitializeComponent in its constructor. Moving that call, removing resources, or changing packages is unsupported by this evidence. No production patch was applied or proposed as a confirmed fix. |

| Hypothesis | Confidence / limits |
| --- | --- |
| Constructor-time direct Resources access explains the baseline getter failure | **High**: reproducible in both code-only baseline and generated-XAML control; same control executable succeeds after constructor completion. Native implementation mechanism remains unknown. |
| Lack of the normal XAML/metadata setup contributes to the code-only resource-construction failure | **Medium-high**: delayed code-only constructor fails while compiled control succeeds; this experiment changes compiled resources and metadata together, not metadata alone. |
| Host or app-local SDK payload universally cannot initialize Fluent resources | **Low**: directly contradicted for this control by 3 successful launches with identical runtime/PRI hashes. A host interaction specific to production is still possible. |
| Production omits standard generated startup/metadata | **Low**: inspected artifacts contain them. Structural presence does not establish successful runtime use. |
| Production-specific XAML/resource, generated-binding, composition or configuration interaction | **Plausible, unlocalized**: production fails and simpler compiled control works; no single application component has been isolated as defective. |
| A production SDK downgrade or Windows repair is warranted | **Unsupported** by this task. Neither is a next action. |

No new native dump analysis, debugger, symbol download, OS repair or cross-machine test was undertaken. The outer stowed-exception signature does not distinguish getter initialization from native resource parsing: three materially different failure paths shared it. Full native causes and production's throwing stage are unresolved.

## Acceptance boundaries and recommended decision

| Check | Status |
| --- | --- |
| Standard compiled XAML initializes required resources | **PASS**, diagnostic control 3/3 |
| Resources accessible in OnLaunched | **PASS**, compiled control and deferred getters 3/3 each |
| NavigationView constructed with required styles | **PASS**, compiled control 3/3 |
| Constructor-time direct Resources inspection | **FAIL**, compiled and code-only controls 3/3 each |
| Unchanged production startup | **FAIL**, 3/3 crash, no window observed |
| Production InitializeComponent completion / exact throwing stage | **NOT VERIFIED** |
| Production navigation/themes/persistence/resizing/keyboard acceptance | **NOT VERIFIED**, blocked by startup; the diagnostic window is not product acceptance |
| Production source/pins and earlier probes preserved | **PASS**, 56-file SHA256 comparison |
| Absence of unexpected writes anywhere on the host | **NOT VERIFIED**, no whole-machine I/O tracing; normal builds/restores/tests write their outputs and existing Windows crash reporting may write WER artifacts |

Recommend accepting LIFECYCLE-04 as a completed diagnostic task while keeping TASK-001 blocked. Retain SDK 2.5.1 and required Fluent resources. Correct the architectural diagnosis: the host can run this standard compiled WinUI control; the original code-only probe does not prove production's Application.Resources initialization fails at its getter.

The next lowest-risk **separately reviewed** step is a source-isolated, instrumented copy of production startup/views with the same package pins and deployment: checkpoint before/after App.InitializeComponent, DI construction, OnLaunched and MainWindow/SettingsView initialization, logging exception types/HRESULTs and non-private XAML diagnostics without suppression. Preserve this successful compiled control as the positive control. This would localize production before choosing any minimal patch or native debugging. It was **not performed** in this task. Any eventual production fix requires architectural review, a bounded patch, repeat actual-EXE launches, full GUI acceptance, and build/test reruns. No specific fix can honestly be prescribed yet.

## Files added and safety scope

Only these new source/report files were added:

- diagnostics/WinUiXamlLifecycleProbe/WinUiXamlLifecycleProbe.csproj — isolated standard XAML control, existing pin/deployment/manifest.
- diagnostics/WinUiXamlLifecycleProbe/App.xaml — required resources through compiled application XAML.
- diagnostics/WinUiXamlLifecycleProbe/App.xaml.cs — constructor/OnLaunched checkpoints and optional constructor resource inspection.
- diagnostics/WinUiXamlLifecycleProbe/MainWindow.xaml — minimal compiled NavigationView control.
- diagnostics/WinUiXamlLifecycleProbe/MainWindow.xaml.cs — generated-init and Loaded/template checkpoints.
- diagnostics/WinUiDeferredStartupProbe/WinUiDeferredStartupProbe.csproj — separate output, same original probe assembly identity and package pin.
- diagnostics/WinUiDeferredStartupProbe/Program.cs — baseline resource block deferred to OnLaunched.
- diagnostics/lifecycle-04/Observe-Lifecycle.ps1 — non-elevated EXE/module/exit/event observer.
- diagnostics/lifecycle-04/README.md — configurations, commands and evidence limitations.
- docs/TASK-001-LIFECYCLE-04-REPORT.md — this report.

Ignored local artifacts include preservation/property/runtime/manifest JSON, generated-source snapshots, PRI dumps, build/restore/test logs, TRX files, and 18 per-run JSON/stderr/checkpoint sets. No diagnostic application writes user settings. No scanning, deletion, cleanup, privileged helper, system component installation, registry/security modification, telemetry, or production feature was introduced. No global runtime was installed; no production package was upgraded/downgraded. Work stops here for architectural review; TASK-002 and further experiments were not started.
