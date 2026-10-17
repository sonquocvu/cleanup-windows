# Production WinUI startup smoke test

Run this interactive check on Windows after building the production solution. It complements the 39 unit/integration tests: XAML compilation does not catch every native XAML conversion error.

From the repository root, in a **non-elevated** PowerShell session:

```powershell
dotnet build CleanSpace.slnx --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
pwsh -NoProfile -File diagnostics/Verify-ProductionStartup.ps1 -RunId Review-1
```

Use a new run ID for every launch. Close existing CleanSpace windows before starting. The observer launches only `src/CleanSpace.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/CleanSpace.App.exe`, with its output directory as the working directory. It does not substitute a diagnostic application or invoke the managed DLL directly.

While the observer waits, confirm that the Dashboard content is visible, navigate to Scan Results and Settings, and inspect the disabled Quick Scan and Cleanup controls. Close the app normally with its Close button or Alt+F4. Repeat three times for release acceptance. For appearance acceptance, apply Light on the first launch, verify it on the second, apply Dark, and verify it on the third. Use only CleanSpace's own settings, with authorization to change that preference; restore the original selection through the app afterward.

## What the observer verifies

- Refuses an elevated token, an already running CleanSpace process, an existing result run ID, or a resolved SDK other than the pinned 2.5.1.
- Observes the production process's `Microsoft.UI.Xaml.dll` module path. Requires the app-local path and a SHA256 matching the resolved WinUI package payload.
- Requires a CleanSpace window to remain present for at least two seconds, then waits for a normal process exit with code 0.
- Queries the Windows Application log for event IDs 1000 and 1026, correlating by launch time and PID. An unreadable log prevents a PASS.
- Records executable, managed assembly, compiled MainWindow XBF and loaded runtime identities, window handle, exit code and event evidence under `artifacts/fix-06/launches/<RunId>.json`. Standard error and a startup checkpoint are retained beside it.

Exit codes are **0 = PASS**, **1 = FAIL**, **2 = NOT VERIFIED**. Precondition/observer errors terminate with a PowerShell error and must not be interpreted as a passing application test. The default overall timeout is 600 seconds (`-TimeoutSeconds 30..1800`). On timeout the observer reports NOT VERIFIED and leaves the app running for the operator to close normally. It never kills a process, writes settings, changes the registry, elevates, installs a runtime, or performs scanning/cleanup.

## Regression coverage and limits

The unmodified production startup resolves `MainWindow`, calls its generated `InitializeComponent()`, and only then activates it. Thus this test actually executes MainWindow XAML loading. A startup conversion failure such as the former `NavigationViewItem.Icon="Lock"` prevents the stable window and/or produces a nonzero process exit; either condition fails this smoke test. The accepted ISOLATE-05 negative control established that failure boundary. FIX-06 ran the corrected production executable three times; it did not deliberately reintroduce the defect into production.

The observer does not assert page content, take screenshots, inspect accessibility, or drive the UI. Those checks require an operator or approved GUI automation in an interactive desktop session. A window-handle check alone is insufficient for UI acceptance. This script is not an unattended CI test and does not count toward the 39 existing tests. Any future unattended version must retain actual MainWindow XAML initialization, normal-user execution, isolated/application-owned settings, and non-destructive normal window closure. Successful compilation alone must never replace it.

TASK-001-FIX-06 results and UI evidence are recorded in `docs/TASK-001-FIX-06-REPORT.md`.
