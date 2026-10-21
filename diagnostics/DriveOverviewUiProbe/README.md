# Drive overview UI probe

This separate, non-shipping WinUI executable links the actual production shell, Dashboard/Settings XAML, code-behind and ViewModels. It supplies fake drive and in-memory theme ports to verify deterministic loading/error/quota/unknown states without changing production DI, introducing failure flags, altering volume state or writing user settings. It is intentionally outside `CleanSpace.slnx`.

```powershell
dotnet restore diagnostics/DriveOverviewUiProbe/DriveOverviewUiProbe.csproj --disable-parallel -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build diagnostics/DriveOverviewUiProbe/DriveOverviewUiProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
```

Launch `diagnostics/DriveOverviewUiProbe/bin/Debug/net10.0-windows10.0.19041.0/win-x64/DriveOverviewUiProbe.exe` as a standard user. Each request waits 12 seconds for loading/navigation inspection. The deterministic sequence is:

1. Initial load throws a private-path error; only the redacted Application error should appear.
2. First Refresh shows fake Q:\ capacity 100 GiB, used 75 GiB, volume free 25 GiB, user available 10 GiB and 75%.
3. Second Refresh keeps identity with all metrics unavailable; no usage bar should appear.
4. Third Refresh returns zero total/free/available bytes; show actual `0 B` values but no percentage bar.
5. Fourth Refresh returns inconsistent metrics (100 total, 101 free); fail closed with a safe error and no stale data.
6. Fifth Refresh recovers to 100 bytes total/free/available, zero used, and a valid 0% bar.
7. Further Refresh requests return 100 bytes total, zero free/available, and a valid 100% bar.

Verify keyboard navigation during pending reads, Light/Dark/System rendering, labeled exact-byte accessibility names, enabled Retry/Refresh, and normal window close. Quick Scan remains disabled in this host. These fake-state observations complement, and do not replace, the real production startup smoke and live Windows provider verification. No production switch or real scanner is used.
