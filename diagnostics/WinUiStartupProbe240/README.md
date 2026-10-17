# Isolated Windows App SDK 2.4.0 comparison

TASK-001-COMPAT-03 only. This project is outside the product solution and does not change production dependencies. Its local `Directory.Packages.props` pins exactly stable Microsoft.WindowsAppSDK 2.4.0 without importing or editing the root package pins.

The project links the preserved `../WinUiStartupProbe/Program.cs` and uses the same production executable manifest. Explicit AssemblyName/RootNamespace values preserve the baseline's executable name, namespace, window title and PRI map identity despite the separate project filename. Target framework, x64 RID, unpackaged app-local Windows App SDK deployment, .NET deployment and initializer settings match the baseline. Build outputs remain inside this project directory.

From a normal Windows PowerShell session at the repository root:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
dotnet restore diagnostics/WinUiStartupProbe240/WinUiStartupProbe240.csproj --disable-parallel --verbosity minimal -m:1 -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build diagnostics/WinUiStartupProbe240/WinUiStartupProbe240.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.4.0 -Variant Window -Run 1
./diagnostics/compat-03/Observe-Probe.ps1 -Sdk 2.4.0 -Variant Resources -Run 1
```

`Window` has no resource-access branch; `Resources` passes the preserved `--controls` argument. The capture script rejects elevated sessions, verifies the resolved SDK, records the actual loaded XAML file's version/hash/path, correlates crash events by PID, and preserves stage output. It waits up to 45 seconds for a surviving window to be closed normally; it never kills a process or sends UI input. Null exit codes or missing runtime identity are incomplete observations, not successful tests. A script exit code 0 only means recording completed; inspect the child ExitCode and validity fields.

Run IDs are retained under `artifacts/compat-03` and cannot be overwritten. For a later review use an unused Run number; do not delete existing evidence to repeat a command. The existing experiment contains runs 1–3 of both variants and SDKs. Use `-Sdk 2.5.1` to observe the preserved baseline with the same script.

See `docs/TASK-001-COMPAT-03-REPORT.md` for the twelve-run result, exact payload identities, controls and limitations. A production SDK change is not authorized by these diagnostic files.
