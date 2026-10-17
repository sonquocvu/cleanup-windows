# TASK-001-ISOLATE-05

Reproducible production-startup isolation, outside CleanSpace.slnx. These scripts create separate copies beneath the ignored artifacts/isolate-05/sources directory. They never modify production. All copies retain CleanSpace.App assembly/resource identity, the four-project reference hierarchy, original project files, central package pins, manifest, and compiled application resources.

The baseline App and XAML are unchanged. One mandatory safety difference replaces only the copied JSON adapter's default-directory resolver: it requires CLEANSPACE_ISOLATE05_SETTINGS instead of accessing the real user's Local AppData. The observer supplies an explicit artifact-owned fixture. There is no fallback to user settings. Preserve this difference when reproducing; do not launch the copied app against real user preferences.

```powershell
./diagnostics/isolate-05/Prepare-Copy.ps1 -Name Baseline
dotnet restore artifacts/isolate-05/sources/Baseline/src/CleanSpace.App/CleanSpace.App.csproj --disable-parallel --verbosity minimal -m:1
dotnet build artifacts/isolate-05/sources/Baseline/src/CleanSpace.App/CleanSpace.App.csproj --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant Baseline -Run 1
```

Repeat Baseline runs 1–3 and verify reproduction/configuration equality before adding instrumentation. If it does not reproduce, stop component isolation and assess differences.

```powershell
./diagnostics/isolate-05/Apply-Instrumentation.ps1
./diagnostics/isolate-05/Prepare-Fix.ps1 -Name FixedIcon -From Instrumented
./diagnostics/isolate-05/Prepare-Fix.ps1 -Name MinimalFix -From Baseline
```

Restore/build each generated project with the same commands as Baseline, substituting the variant name. Instrumented adds startup checkpoints without changing any XAML. FixedIcon changes exactly Icon="Lock" to Icon="ProtectedDocument" from Instrumented. MinimalFix makes that same change from Baseline, without startup tracing. The candidate is supported by the captured XamlParseException, not by matching native exception codes alone. proposed-production.patch is for review; it has **not** been applied.

The observer runs the actual generated EXE with a non-elevated token, retains its process handle, samples the loaded WinUI path/hash, and correlates Application Event 1000/1026 records by PID/time. Results and stderr go to artifacts/isolate-05/runs. Instrumented variants also create startup-<PID>.log there with CreateNew/write-through flushing. Exceptions are logged/rethrown, never suppressed. No constructor-time Application.Resources inspection is added.

Use distinct Run numbers; existing copy/run directories are refused. Surviving windows must be closed normally. Initial window/module observation lasts up to five seconds; exit observation then lasts up to 55 seconds (the first FixedIcon run used the earlier 45-second interval). The observer never kills processes. Its own exit 0 means evidence collection completed. A null child exit is NOT VERIFIED and is preserved as such.

```powershell
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant FixedIcon -Run 2 -SettingsSession ThemePersistence
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant FixedIcon -Run 3 -SettingsSession ThemePersistence
./diagnostics/isolate-05/Observe-Isolation.ps1 -Variant PositiveControl -Run 1
```

SettingsSession intentionally shares only artifacts/isolate-05/settings/<session> across selected restarts. Without it, each run uses its own fixture. PositiveControl launches the existing accepted WinUiXamlLifecycleProbe and uses its unchanged package/runtime configuration. Uninstrumented copies have no activation marker; observe their window, UI and child exit separately. See docs/TASK-001-ISOLATE-05-REPORT.md for actual results and remaining acceptance requirements. No production patch, package change, Windows modification, scan, cleanup, deletion, commit or push is part of this task.
