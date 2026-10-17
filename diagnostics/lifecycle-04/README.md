# TASK-001-LIFECYCLE-04 controls

These diagnostic projects are outside CleanSpace.slnx and reference no CleanSpace assemblies. All use the unchanged central Windows App SDK 2.5.1 pin, net10.0-windows10.0.19041.0, win-x64, unpackaged app-local WinUI, and the existing production executable manifest. They do not implement product features or write settings.

| Observer configuration | Executable/project | Behavior |
| --- | --- | --- |
| BaselineWindow | Existing WinUiStartupProbe | Unmodified code-only Window/TextBlock control. |
| BaselineConstructor | Existing WinUiStartupProbe --controls | Unmodified resource access in the Application constructor. |
| DeferredLaunched | WinUiDeferredStartupProbe --controls | Baseline resource block moved into OnLaunched, before Window construction; still no application XAML or generated metadata. |
| XamlLaunched | WinUiXamlLifecycleProbe | Compiled App.xaml with XamlControlsResources, generated Main/InitializeComponent/metadata, resource inspection in OnLaunched, compiled NavigationView window. |
| XamlConstructor | WinUiXamlLifecycleProbe --constructor-resources | Same compiled executable, with an additional resource inspection immediately after InitializeComponent in the Application constructor. |
| Production | Unmodified CleanSpace.App | No additional production instrumentation. Absence of a marker cannot identify its failing lifecycle stage. |

Build from a normal Windows user session at the repository root:

```powershell
dotnet restore diagnostics/WinUiXamlLifecycleProbe/WinUiXamlLifecycleProbe.csproj --disable-parallel --verbosity minimal -m:1
dotnet restore diagnostics/WinUiDeferredStartupProbe/WinUiDeferredStartupProbe.csproj --disable-parallel --verbosity minimal -m:1
dotnet build diagnostics/WinUiXamlLifecycleProbe/WinUiXamlLifecycleProbe.csproj --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet build diagnostics/WinUiDeferredStartupProbe/WinUiDeferredStartupProbe.csproj --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
./diagnostics/lifecycle-04/Observe-Lifecycle.ps1 -Configuration XamlLaunched -Run 1
```

Repeat each configuration with distinct Run numbers. The observer launches the actual EXE, retains its process handle for exit status, samples the loaded XAML module, correlates Application Event 1000/1026 records by PID and start time, and writes JSON/stderr beneath artifacts/lifecycle-04. It refuses an elevated token and refuses an already recorded run ID. The observer's own exit 0 means evidence collection completed, not that the application passed.

For a surviving window, close it normally during the 45-second observation interval. The observer never kills a process. A null child exit is NOT VERIFIED. The five-second initial observation limit does not prove a never-observed window never existed. RuntimePayloadMatches requires the observed app-local path and SHA256 to match the resolved WinUI NuGet payload. Event module paths are a labeled fallback when live module sampling misses a fast crash.

Both XAML variants retain required Fluent resources. No generated source is hand-edited and no exception is marked handled. ApplyTemplate=False at Loaded can mean the template was applied earlier; assess TemplatePresent, visual children, and the observed UI together. See the report for the actual results and their limits. Do not infer a production patch from the code-only baseline.
