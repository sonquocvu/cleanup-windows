# TASK-001-ISOLATE-05 — Production startup isolation

Date: 2026-10-17, UTC+07:00. **Diagnostic objective achieved. Production is not patched; TASK-001 still awaits architectural review and production acceptance.**

## Finding

The smallest identified application defect is **`Icon="Lock"` on the disabled Cleanup NavigationViewItem in MainWindow.xaml, line 14**. MainWindow.InitializeComponent throws **Microsoft.UI.Xaml.Markup.XamlParseException, HRESULT 0x802B000A**:

```text
Failed to create a 'Microsoft.UI.Xaml.Controls.IconElement' from the text 'Lock'.
[Line: 14 Position: 87]
```

App.InitializeComponent, required application resources, DI setup, and App construction complete before this failure. The crash occurs during **MainWindow XAML parsing**, before window activation or settings loading. It is not the constructor-time Application.Resources getter failure of the earlier code-only probe.

Changing only that icon literal to **`ProtectedDocument`** in the isolated application resolves startup: the traced candidate passed **3/3 launches with exit 0**; the same correction without tracing passed **three further launches with captured exit 0**. A fourth uninstrumented session verified themes but exceeded its exit observation interval; its recorded exit remains **NOT VERIFIED**. The accepted compiled-XAML positive control passed **3/3 launches with exit 0**.

Microsoft's [Symbol enumeration](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.symbol?view=windows-app-sdk-2.0) includes ProtectedDocument and does not define a Lock member. [NavigationViewItem.Icon](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.navigationviewitem.icon?view=windows-app-sdk-2.0) is an IconElement property. The runtime exception, one-variable comparison, and successful replacement provide the decisive evidence; this conclusion does not depend on an outer native exception-code match.

## Preservation and reproduction validity

Read AGENTS.md, PRODUCT, ARCHITECTURE, SECURITY, ROADMAP, the foundation task, TASK-001-REPORT, DIAG-02, COMPAT-03, LIFECYCLE-04 and the relevant probe documentation/source. Initial git status and SHA256 for **66 existing tracked/nonignored files** were recorded. Final preservation check: **66 unchanged, 0 missing/modified**. No production source, central package configuration, manifest, resource source, solution entry, previous diagnostic source/report, or test was edited.

The initial index contained pre-existing staged foundation files. Repository history subsequently showed commit **a1c7293**, timestamp **2026-10-17T16:50:02+07:00**, made outside this task's actions. This task issued no staging, commit or push command. The content-preservation comparison remained clean. Final production/copy EXEs both report FileVersion 1.0.0.0 and ProductVersion 1.0.0+a1c7293c65c572da11cd789c15e3792bd12e2138; build metadata is therefore also recorded rather than assuming the repository index stayed unchanged.

`Prepare-Copy.ps1` created a separate four-project tree beneath:

```text
E:\01 - Work\cleanup-windows\artifacts\isolate-05\sources\Baseline\
```

It copied the original src hierarchy and Directory.Build.props, Directory.Packages.props, global.json and NuGet.Config. Output/intermediate directories are local to each copied project; no production bin/obj path is shared. Diagnostic copies are outside CleanSpace.slnx. Preparation scripts and source overlays are reviewable under diagnostics/isolate-05; generated copies/build outputs remain ignored artifacts.

**The App startup/XAML baseline is unmodified, but the entire program is not byte-identical to production.** One required safety substitution changes the copied Infrastructure adapter's default settings-directory resolver to require `CLEANSPACE_ISOLATE05_SETTINGS`. It never resolves Local AppData and has no fallback to existing user preferences. The observer supplies only an absolute directory beneath artifacts/isolate-05. This is the explicitly isolated settings-store behavior required by the task. Of 23 baseline source/configuration files, **22 were byte-identical**, and only that adapter resolver differed.

The copied App retains its original DI registration, constructor ordering, views, view models, settings service, compiled XAML, project names and references. In the traced reproduction, fixture-path construction completes before MainWindow.InitializeComponent fails. **Settings.ReadAsync is not reached:** settings initialization follows activation, which is not reached. Thus the safety-only path difference does not explain the observed icon parsing failure. No existing user settings were read, hashed, enumerated, copied, or modified.

| Comparison | Production versus baseline evidence |
| --- | --- |
| AssemblyName / RootNamespace | CleanSpace.App / CleanSpace.App, equal |
| TargetFramework / minimum OS | net10.0-windows10.0.19041.0 / 10.0.19041.0, equal |
| Platform / RID | x64 / win-x64, equal |
| UseWinUI / WindowsPackageType | true / None, equal |
| WindowsAppSDKSelfContained / .NET SelfContained | true / false, equal |
| UndockedRegFreeWinRTInitialize / DeploymentManagerInitialize | true / false, equal |
| DefineConstants and queried startup properties | Equal across production, Baseline, Instrumented, FixedIcon and MinimalFix |
| Package pins | Original central file copied unchanged; Windows App SDK [2.5.1]; no version substitution |
| Source manifest / extracted EXE manifest | Byte-identical; asInvoker, uiAccess=false, original OS compatibility/DPI declarations retained |
| App.xaml / MainWindow.xaml / SettingsView.xaml | Byte-identical before component isolation |
| App.xbf | Equal SHA256: 8EC1B607A13EDFD82645A83C94DD6F784E5B9EC67004782AEEAB933810D9B776 |
| MainWindow.xbf | Equal SHA256: E6BC76D395F6638DD2960AB59FF74A3BB1FCD7E1AEA7CC15E1AA6F13568B191E |
| SettingsView.xbf | Equal SHA256: 5F68AD3983236E3237E647487F192F2BEE01B462B3F74BFF27E275EDEDA77628 |
| CleanSpace.App.pri | Equal SHA256: 199838BE5430E6A897AF420E0AF3071A40BC2E5AD195104173F869D418511346 |
| Generated XamlTypeInfo.g.cs | Byte-identical; SHA256 D541323C703E816AB0591319249DD6E7D69EAB94FCEE382532C37EFDD4542B29 |
| Generated App startup | XAML-generated Main, Application.Start, dispatcher context and LoadComponent retained. No hand-edited generated code or alternate entry point. |
| Rebuilt DLLs | Different identities recorded below; different build/source paths and the settings resolver prevent claiming whole-program binary identity. |

At initial comparison, the earlier production EXE hash was F323F1BC4EA85FB338027D0AB9545B7701A2B18DB3D5A7358D227BBD9006EE50, while the copied EXE was E012D241826000E4A2513D420068E25445B8DEEB5366227880A8A682B58D5D66. After the unchanged solution build, production's apphost matched that copied EXE byte-for-byte (299520 bytes, 0 byte differences); its managed DLL remained separately identified. The earlier apphost hash difference was not independently diagnosed. **Apphost equality alone is not application equality:** the managed DLL and external compiled XAML matter. The reproduction claim rests on the matching failing XBF/resources, effective configuration, native payload, repeated failures, and localized managed exception.

Phase A produced **3/3 failures with the original outer crash signature**. Therefore component isolation proceeded. Had the baseline launched or materially changed the failure, it would not have been treated as a validated reproduction. Production was not launched again in this task, so its real settings could not be accessed. Its unchanged failing source and compiled MainWindow XAML were the comparison inputs.

## Startup instrumentation and first failing boundary

Only after the baseline's three failures, Instrumented was cloned from Baseline. It added process-specific tracing around App/DI/OnLaunched, MainWindow initialization/activation, settings loading, theme application, and SettingsView construction/property registration. It changed **no XAML, manifest, project properties or package references**. All three instrumented runs reproduced the failure and managed message.

`StartupTrace` opens `startup-<PID>.log` with CreateNew, write-through and automatic flushing in the observer-created artifacts run directory. Stderr is also redirected there. Exception type, HRESULT, message and local stack are recorded; caught exceptions are rethrown and WinUI UnhandledException is never marked handled. No direct Application.Resources access is added to the App constructor. No log/dump/private data was uploaded.

| Checkpoint | Instrumented failing runs, 3/3 | FixedIcon traced runs, 3/3 |
| --- | --- | --- |
| App constructor entry | Reached | Reached |
| App.InitializeComponent entry/return | Both reached | Both reached |
| DI setup entry/return | Both reached | Both reached |
| App constructor return | Reached | Reached |
| OnLaunched entry | Reached | Reached |
| MainWindow resolution entry | Reached | Reached |
| Fixture settings-path resolution | Returned | Returned |
| MainWindow constructor entry | Reached | Reached |
| MainWindow.InitializeComponent entry | Reached | Reached |
| MainWindow.InitializeComponent return | **Not reached; XamlParseException** | Returned |
| SettingsView.InitializeComponent | Not reached before failing icon | Returned |
| Theme application | Not reached | Returned |
| MainWindow resolution return | Not reached | Returned |
| Window.Activate | Not reached | Returned |
| Settings.InitializeAsync | Not reached | Returned using fixture |
| Normal window closure | Not reached | Reached; child exit 0 |

The exception's local managed stack points through Application.LoadComponent, generated MainWindow.InitializeComponent and MainWindow's constructor. DI resolution passes that exception back to OnLaunched; the async void launch handler then reaches WinUI's unhandled-exception path. This explains why the Application event ultimately names Microsoft.UI.Xaml.dll although the actionable fault is an authored XAML icon value.

## Component isolation matrix

| Variant | Controlled change from parent | Result |
| --- | --- | --- |
| Baseline | Original App startup/XAML; mandatory fixture-path resolver only | 3/3 crashes, original outer signature, no observed window |
| Instrumented | From Baseline: checkpoints/exception logging only | 3/3 same outer signature; MainWindow.xaml Lock conversion failure, 0x802B000A |
| FixedIcon | From Instrumented: **one literal**, Icon="Lock" → Icon="ProtectedDocument" | 3/3 launches and normal exits 0; all startup phases complete |
| MinimalFix | From Baseline: **same one-literal change**, no tracing | Four windows observed; runs 2/3/4 exited 0. Run 1's longer GUI session exceeded the exit observation deadline; exit NOT VERIFIED. |
| PositiveControl | Unchanged accepted WinUiXamlLifecycleProbe, compiled App.xaml/resources/NavigationView | 3/3 launches, normal exits 0; expected resource/template markers |

For FixedIcon versus Instrumented and MinimalFix versus Baseline, source comparison found **only MainWindow.xaml changed**. App.xaml, SettingsView.xaml, all project files, manifest, package pins and required resources remained equal. All four copied variants have identical generated XamlTypeInfo.g.cs. Their App.xbf, SettingsView.xbf, PRI and native WinUI resource files remain identical; the corrected **MainWindow.xbf** changes to SHA256 **EFE221154F1F83807C378CB0EFC05E14BBE6D5161D05EEF26645D2432EB86048**. The PRI indexes external XBF files, so its unchanged hash does not imply MainWindow.xbf is unchanged.

No components were removed. SettingsView, generated bindings, DI validation, theme handling and XamlControlsResources remain present. The invalid icon was replaced, not hidden by omitting the disabled navigation item. No arbitrary lifecycle delay, exception suppression, resource bypass, package change or Windows repair was needed.

## Exact commands and execution results

Commands ran from `E:\01 - Work\cleanup-windows` on Windows x64 10.0.26300.0, PowerShell 7.6.5, .NET SDK 10.0.302, MSBuild 18.6.11+35b593beb. Tests used .NET 10.0.10. Normal-user execution outside the restricted process sandbox supplied NuGet access, GUI/process/event access and test-host IPC; no UAC/elevated token was used. The observer refuses elevated tokens.

Preparation:

```powershell
./diagnostics/isolate-05/Prepare-Copy.ps1 -Name Baseline
# Only after Baseline runs 1–3 reproduced:
./diagnostics/isolate-05/Apply-Instrumentation.ps1
./diagnostics/isolate-05/Prepare-Copy.ps1 -Name FixedIcon -From Instrumented
$path = 'artifacts/isolate-05/sources/FixedIcon/src/CleanSpace.App/MainWindow.xaml'
$xaml = Get-Content -LiteralPath $path -Raw
[IO.File]::WriteAllText((Join-Path (Get-Location) $path), $xaml.Replace('Icon="Lock"','Icon="ProtectedDocument"'))
./diagnostics/isolate-05/Prepare-Fix.ps1 -Name MinimalFix -From Baseline
```

Prepare-Fix.ps1 packages the same validated one-literal replacement for reproducible future FixedIcon creation. An initial instrumentation-wrapper variable validation collision failed before overlays/launch; it was corrected, and the already-created tree was verified equal to Baseline before instrumentation was applied. No baseline/production source was changed by that tooling correction.

For each V = Baseline, Instrumented, FixedIcon, MinimalFix, these exact restore/build commands were executed with V substituted literally:

```powershell
dotnet restore artifacts/isolate-05/sources/V/src/CleanSpace.App/CleanSpace.App.csproj --disable-parallel --verbosity minimal -m:1
dotnet build artifacts/isolate-05/sources/V/src/CleanSpace.App/CleanSpace.App.csproj --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
```

| Command | Exit / result |
| --- | --- |
| Baseline restore / build | 0 / 0; build 18.05 s, 0 warnings/errors |
| Instrumented restore / build | 0 / 0; build 18.01 s, 0 warnings/errors |
| FixedIcon restore / build | 0 / 0; build 18.44 s, 0 warnings/errors |
| MinimalFix restore / build | 0 / 0; build 17.60 s, 0 warnings/errors |
| Solution build below | 0; 20.58 s, 0 warnings/errors |
| Solution tests below | 0; **39 passed, 0 failed, 0 skipped**, MSBuild elapsed 3.67 s |
| Final git diff --check | 0; existing README line-ending notice only |
| Final git apply --check proposed-production.patch | 0; applicability check only, **not applied** |

```powershell
dotnet build CleanSpace.slnx --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet test CleanSpace.slnx --no-build --no-restore --verbosity normal -m:1 --logger 'trx;LogFilePrefix=isolate-05' --results-directory artifacts/isolate-05/test-results --blame-hang-timeout 30s
git diff --check
git apply --check diagnostics/isolate-05/proposed-production.patch
```

Tests: Application **13 passed** (0.7764 s), Core **7 passed** (0.7838 s), Integration **19 passed** (0.8967 s). These existing tests do not load MainWindow XAML; their green result does not catch this runtime conversion defect. No production test was changed. Restores reported no warnings; security auditing was not disabled and package versions were not upgraded. A new comprehensive vulnerability assessment was not part of this isolation task.

Exact launch invocations (console output generally redirected to the corresponding artifacts/isolate-05/*-console.log):

```powershell
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant Baseline -Run 1
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant Baseline -Run 2
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant Baseline -Run 3
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant Instrumented -Run 1
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant Instrumented -Run 2
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant Instrumented -Run 3
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant FixedIcon -Run 1
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant FixedIcon -Run 2 -SettingsSession ThemePersistence
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant FixedIcon -Run 3 -SettingsSession ThemePersistence
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant MinimalFix -Run 1 -SettingsSession ThemePersistence
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant MinimalFix -Run 2
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant MinimalFix -Run 3
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant MinimalFix -Run 4
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant PositiveControl -Run 1
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant PositiveControl -Run 2
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant PositiveControl -Run 3
```

The observer launches the **actual EXE** through Start-Process, uses its output directory as WorkingDirectory, supplies the two controlled diagnostic environment variables, redirects stderr, retains the process handle, samples modules/window handles and waits for exit. No `dotnet app.dll` launch or process termination is used. Its own exit **0 in all 16 invocations** means recording succeeded, not application success. The initial exit interval was 45 seconds; from FixedIcon-2 onward it was 55 seconds. MinimalFix-1 was closed normally through UI after its interval expired; the original null-exit record is retained. Three additional uninstrumented runs obtained complete exit evidence.

Read-only configuration queries used `dotnet msbuild <project> -getProperty:AssemblyName,RootNamespace,TargetFramework,RuntimeIdentifier,PlatformTarget,UseWinUI,WindowsPackageType,WindowsAppSDKSelfContained,SelfContained,WindowsAppSdkUndockedRegFreeWinRTInitialize,WindowsAppSdkDeploymentManagerInitialize,DefineConstants`; all exited 0 and comparisons were equal. Existing NuGet SDK mt.exe extracted production/baseline EXE manifests with `-nologo "-inputresource:<actual-exe>;#1" "-out:<artifact-manifest>"`; both exited 0 and extracted content matched. SHA256, signature and compiled-file comparisons are saved locally. No native tool was installed.

## Per-run process and Event Log evidence

| Run | PID | Child exit | Observed HWND | Correlated Event 1000 |
| --- | ---: | --- | ---: | ---: |
| Baseline-1 | 30368 | -1073741189 | 0 | 73184 |
| Baseline-2 | 22924 | -1073741189 | 0 | 73186 |
| Baseline-3 | 29844 | -1073741189 | 0 | 73188 |
| Instrumented-1 | 32744 | -1073741189 | 0 | 73190 |
| Instrumented-2 | 30344 | -1073741189 | 0 | 73192 |
| Instrumented-3 | 13324 | -1073741189 | 0 | 73194 |
| FixedIcon-1 | 30508 | 0 | 4195902 | none |
| FixedIcon-2 | 29620 | 0 | 2427594 | none |
| FixedIcon-3 | 20620 | 0 | 723692 | none |
| MinimalFix-1 | 2920 | **NOT VERIFIED (null)** | 1378882 | none during observation |
| MinimalFix-2 | 4408 | 0 | 1313418 | none |
| MinimalFix-3 | 30032 | 0 | 2491840 | none |
| MinimalFix-4 | 464 | 0 | 3016264 | none |
| PositiveControl-1 | 31952 | 0 | 4130262 | none |
| PositiveControl-2 | 22988 | 0 | 2886068 | none |
| PositiveControl-3 | 1536 | 0 | 2820616 | none |

All six failing Event 1000 records identify **Microsoft.UI.Xaml.dll, version 3.2.3.0, exception c000027b, offset 00000000003a9dbd**, with each copy's app-local module path. Child exit -1073741189 is **0xC000027B**. There were no correlated Event 1026 records. Event queries were correlated by PID and launch time. Matching successful-run Event 1000/1026 records were absent within the captured intervals. These are bounded observations, not whole-machine crash monitoring.

All three Instrumented logs contain the same specific IconElement/Lock parse message at line 14/position 87. Last failure marker is WinUI UnhandledException/XamlParseException/0x802B000A. FixedIcon's final marker is Window closed normally. The positive control reports successful OnLaunched resource inspection, XamlControlsResources present=True and NavigationView loaded with TemplatePresent=True/VisualChildren=1. Baseline/MinimalFix intentionally have no source markers; their UI and process evidence are distinct from the traced runs.

## Actual executable and runtime identities

Let R = `E:\01 - Work\cleanup-windows`, O = `bin\Debug\net10.0-windows10.0.19041.0\win-x64`.

For each copied V (Baseline, Instrumented, FixedIcon, MinimalFix), the actual executable was:

```text
R\artifacts\isolate-05\sources\V\src\CleanSpace.App\O\CleanSpace.App.exe
```

PositiveControl used `R\diagnostics\WinUiXamlLifecycleProbe\O\WinUiXamlLifecycleProbe.exe`.

| Variant | EXE SHA256 | Managed DLL SHA256 |
| --- | --- | --- |
| Baseline | E012D241826000E4A2513D420068E25445B8DEEB5366227880A8A682B58D5D66 | CDC133D89454B54269134D5A5D4DBF1DEB683B08ED19A858A38AED6BECF30B5A |
| Instrumented | E012D241826000E4A2513D420068E25445B8DEEB5366227880A8A682B58D5D66 | 57F1A50A20CFD8100BFDB6C6D898EAD6F45837ED2FF12B21C8D50D3E68C4260C |
| FixedIcon | E012D241826000E4A2513D420068E25445B8DEEB5366227880A8A682B58D5D66 | 77BEB318A5420D384E742296A814710FB0EAFA1F09B9DE52FFA04B03C4D60445 |
| MinimalFix | E012D241826000E4A2513D420068E25445B8DEEB5366227880A8A682B58D5D66 | CDD59548BB449C7DC7F781965361893798A1BE16465A07D6A575FDB138D47CD8 |
| PositiveControl | FE9F7518DA02EA8DBACD0B1F3C60DB849CFBB6F3468363823324468DB9DADE35 | 9DC2C715C14A0E05490D86C14412423BBEFBB9C0F817901A5929223D1059A971 |

All **16** runs observed Microsoft.UI.Xaml.dll directly through **Process.Modules** in the corresponding EXE's own directory. Each was file version **3.2.3.2609**, SHA256 **AAD12524765E6FB63F0AE26A45A9BA3104F24FDE66413D8A3036FBED74A1E990**, with **Valid** signature. Each matched the resolved Windows App SDK **2.5.1 → WinUI/2.3.9** package payload. No framework/shared runtime was substituted. Required Microsoft.UI.Xaml.Controls.pri remained SHA256 **5EDB56C0E4688A74CECC4297CB21BD3AAF9F4FBBD2EFD18A1C49464744EF8CF3**. Final EXE/managed-DLL recheck against all 16 run records found **0 mismatches**.

## Candidate UI verification

Computer Use observed all surviving diagnostic/control windows and closed them normally through Alt+F4. One initial index-click attempt lacked geometry; a fresh screenshot-backed observation resolved that automation issue. Computer Use was not interrupted. No Windows setting was changed.

| Check | Result and scope |
| --- | --- |
| Corrected startup / normal closure | **PASS** in three traced and three fully observed uninstrumented runs |
| Dashboard / Scan Results / Settings navigation | **PASS**, corrected diagnostic; screenshots/content showed each page |
| Cleanup disabled | **PASS**, disabled accessibility state and visible unavailable item, with replacement protected-document icon |
| Quick Scan disabled | **PASS**, disabled accessibility state on Dashboard |
| Light theme | **PASS**, visibly applied after Apply theme |
| Dark theme | **PASS**, visibly applied after Apply theme |
| System theme | **PASS**, changing from explicit Light to System restored the host's dark appearance; no OS preference change |
| Theme persistence after restart | **PASS in fixture**: FixedIcon-2 saved Light, file contained {"Theme":"Light"}; FixedIcon-3 restarted with the same fixture and visibly restored Light/selection. It then saved Dark. MinimalFix-1 loaded Dark, and later saved System. |
| Production default settings/profile behavior | **NOT VERIFIED**, intentionally never accessed |
| Full resizing, keyboard traversal and accessibility acceptance | **NOT VERIFIED** in this diagnostic; normal Alt+F4 closure is not full keyboard acceptance |
| Corrected production EXE acceptance | **NOT VERIFIED**, patch not applied |

The shared theme session is only artifacts/isolate-05/settings/ThemePersistence. Snapshots theme-after-save-run2.json, theme-after-save-run3.json and theme-after-MinimalFix1.json contain Light, Dark and System respectively. No real preference file was inspected. This confirms the existing UI/settings path works in the bounded candidate; it is not full TASK-001 acceptance.

## Root-cause assessment and proposed production patch

| Finding/hypothesis | Confidence and evidence |
| --- | --- |
| Invalid Lock icon literal causes the reproduced CleanSpace startup crash | **High**: specific managed parse message/line, identical production failing XBF, three original/traced failures, and one-token passing comparisons with/without tracing |
| Application.Resources or normal App initialization is the production-specific boundary | **Contradicted for this reproduction**: App.InitializeComponent and DI return before MainWindow parsing fails |
| DI, JSON reads, SettingsView registration or theme initialization is the triggering defect | **Low for this crash**: DI/setup/path resolution finish; parsing fails before reads/theme/SettingsView; all complete after changing only the icon |
| WinUI payload mismatch, global runtime absence or broken Fluent resources is required to explain this failure | **Low**: same signed app-local payload and required resources succeed in both positive control and complete corrected copy |
| Native exception code proves the old code-only probe shares this root cause | **Unsupported**: earlier probe failures occurred at different operations and lack this production MainWindow XAML |

The justified proposed production change is one line in **src/CleanSpace.App/MainWindow.xaml:14**:

```diff
-                <NavigationViewItem Content="Cleanup (unavailable)" IsEnabled="False" Icon="Lock"
+                <NavigationViewItem Content="Cleanup (unavailable)" IsEnabled="False" Icon="ProtectedDocument"
```

The review artifact is **diagnostics/isolate-05/proposed-production.patch**. Its final `git apply --check` exited **0**; no apply occurred. An earlier zero-context draft failed applicability checking and was replaced with a normal context-bearing patch before this final check. No resource removal, startup reorder, package change, DI refactor, settings change or new feature belongs in the proposed production correction.

Recommend accepting ISOLATE-05 as complete and approving a **separate narrowly scoped production patch/acceptance task** for this icon correction. Keep Windows App SDK 2.5.1. Native debugging, package experiments and Windows repairs are not justified prerequisites for addressing this identified XAML defect. They remain unnecessary unless a distinct failure persists after the reviewed patch.

## Regression requirements and remaining blockers

For the separately approved production correction:

1. Apply only the reviewed icon change; preserve disabled actions and required Fluent resources. Confirm source diff and generated MainWindow XAML identity.
2. Rebuild the production solution and run all existing tests. A build alone is insufficient: the invalid original literal compiled successfully.
3. Add/retain a meaningful Windows runtime startup smoke check that actually loads MainWindow XAML with the pinned payload, verifies window activation and normal closure, and captures managed XAML errors/Event Log failures. Cover the disabled Cleanup item so this conversion cannot silently regress.
4. Repeat actual production EXE launches at least three times and rerun full foundation GUI acceptance: navigation, all themes/persistence, disabled Quick Scan/Cleanup, ordinary resizing, keyboard traversal and accessibility. Use an approved isolated profile/settings harness for preference checks.
5. Confirm no production package/configuration drift, no scan/cleanup path, and normal-user operation. Reassess only any new, specifically observed failure.

Remaining limitations: the production patch is not yet authorized/applied; corrected production startup/full acceptance is therefore unverified. No cross-machine compatibility claim is made. One uninstrumented GUI session's child exit is unknown after its bounded recorder ended, explicitly recorded above; the extra three complete runs supply repeat exit evidence. Full native stowed-exception decoding was not performed and is not needed to identify this managed XAML conversion error. The cause of the historical apphost-hash difference was not isolated; the unchanged final rebuild matches the diagnostic apphost and the relevant XBF/configuration comparisons are explicit.

## Added files and safety scope

New reviewable files only:

- diagnostics/isolate-05/Prepare-Copy.ps1 — isolated copies, preservation comparison and fixture-only settings resolver.
- diagnostics/isolate-05/Apply-Instrumentation.ps1 — guarded overlay application to the validated baseline copy.
- diagnostics/isolate-05/Prepare-Fix.ps1 — exact one-variable icon correction in a new copy.
- diagnostics/isolate-05/Observe-Isolation.ps1 — actual EXE/process/module/event evidence with controlled fixtures.
- diagnostics/isolate-05/instrumentation/App.xaml.cs — startup/DI/activation/settings checkpoints and rethrows.
- diagnostics/isolate-05/instrumentation/MainWindow.xaml.cs — window/XAML/theme checkpoints and rethrows.
- diagnostics/isolate-05/instrumentation/SettingsView.xaml.cs — view/property-registration checkpoints and rethrows.
- diagnostics/isolate-05/instrumentation/StartupTrace.cs — process-specific local diagnostic writer.
- diagnostics/isolate-05/proposed-production.patch — unapplied review candidate.
- diagnostics/isolate-05/README.md — reproduction, configurations and limitations.
- docs/TASK-001-ISOLATE-05-REPORT.md — this report.

Ignored artifacts contain copy source/configuration trees, separate bin/obj, preservation/property/payload comparisons, manifests, sixteen run JSON/stderr/checkpoint sets, process logs, theme fixtures/snapshots, build/restore logs and TRX files. They are local and were not uploaded. No diagnostic/test process remains running.

No production source or pins changed. This task performed no scanning, cleanup, deletion, privileged helper, global runtime/tool installation, Windows/registry/security modification, service, scheduled task, external diagnostic upload, staging, commit or push. Existing Windows crash reporting may write WER artifacts under its existing configuration; no whole-machine absence-of-writes claim is made. Work stops here for architectural review. TASK-002 and further experiments were not started.
