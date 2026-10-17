# WinUI startup isolation

This diagnostic executable uses the same pinned Windows App SDK and unpackaged/app-local deployment as CleanSpace. It references no CleanSpace assemblies, has no application XAML, and does not read or write settings. It is intentionally outside the solution; it is not shipped as part of the product.

From a normal Windows PowerShell session at the repository root:

```powershell
dotnet restore diagnostics/WinUiStartupProbe/WinUiStartupProbe.csproj --disable-parallel --verbosity minimal -m:1
dotnet build diagnostics/WinUiStartupProbe/WinUiStartupProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
./diagnostics/Verify-Startup.ps1 -Target Probe
```

The baseline constructs Application, Window, and TextBlock programmatically and activates a window. Close it normally before rebuilding. To add only the controls resource dictionary:

```powershell
./diagnostics/Verify-Startup.ps1 -Target ProbeControls
```

The `--controls` variant evaluates Application.Resources, its MergedDictionaries, constructs XamlControlsResources, and inserts the dictionary before window construction. Separate stage markers preserve the original combined expression's receiver-before-argument evaluation order. They distinguish failure in a property getter from failure in the constructor or insertion. Markers go to standard error. The verification script also launches the actual CleanSpace executable with `-Target App`, records the loaded XAML path when observable, waits up to ten seconds for exit, and reads matching Application event entries. A process still running at that point has a null exit code; that alone is not GUI acceptance. The script does not close or kill surviving windows.

Results stay beneath `artifacts/runtime`. Settings snapshots inspect only CleanSpace's known settings path, with no directory enumeration. They do not prove the absence of writes elsewhere on the machine.

On the verification machine the baseline activated a window. DIAG-02's finer markers show that the controls variant crashes while accessing Application.Resources, **before the XamlControlsResources constructor is reached**. The earlier combined-expression marker could not distinguish those operations. See `docs/TASK-001-DIAG-02-REPORT.md` for current findings and `docs/TASK-001-REPORT.md` for the preceding review. Do not replace packages or change OS settings based on an exception-code match alone.

## Offline metadata inspection

`diagnostics/inspect_native.py` uses only the installed Python standard library. It opens an explicitly named existing file read-only. It can inspect PE32+ imports without loading a DLL, or AMD64 minidump exception/stowed-exception metadata and recorded module-relative trace entries. It performs no network requests, symbol downloads, stack unwinding, dump generation, registry access, or process mutation. Raw memory, error text, and module paths are omitted from its JSON output. A missing captured memory range is reported instead of guessing its contents.

```powershell
python -B -m unittest discover -s diagnostics -p test_inspect_native.py -v
python -B diagnostics/inspect_native.py pe './diagnostics/WinUiStartupProbe/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Microsoft.UI.Xaml.dll'
# Supply only an already existing CleanSpace/probe dump; keep it local.
python -B diagnostics/inspect_native.py dump '<existing-local-probe-dump>'
```

The reader supports the documented AMD64 SE01/SE02 structure layouts; it is not a debugger. Its synthetic tests validate the metadata interpretation and missing-data behavior, not the correctness of Windows' captured data. Private nested exception objects and symbolic native call stacks still require suitable local diagnostic tooling.
