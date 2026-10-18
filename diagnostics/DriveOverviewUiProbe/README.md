# Drive overview UI probe

This separate, non-shipping WinUI executable links the actual production shell, Dashboard/Settings XAML, code-behind and ViewModels. It supplies fake drive and in-memory theme ports to verify deterministic loading/error/quota/unknown states without changing production DI, introducing failure flags, altering volume state or writing user settings. It is intentionally outside `CleanSpace.slnx`.

```powershell
dotnet restore diagnostics/DriveOverviewUiProbe/DriveOverviewUiProbe.csproj --disable-parallel -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build diagnostics/DriveOverviewUiProbe/DriveOverviewUiProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
```

Launch `diagnostics/DriveOverviewUiProbe/bin/Debug/net10.0-windows10.0.19041.0/win-x64/DriveOverviewUiProbe.exe` as a standard user. Initial load waits 12 seconds then throws a private-path error (only the redacted Application error should appear). First Refresh waits 12 seconds then shows fake Q:\ capacity 100 GiB, used 75 GiB, volume free 25 GiB, user available 10 GiB and 75%. Subsequent Refresh waits 12 seconds then returns an identity with all metrics unavailable; no usage bar should appear. Verify navigation during pending reads, Light/Dark/System rendering, enabled Retry/Refresh, and normal window close. These fake-state observations complement, and do not replace, the real production startup smoke and live Windows provider verification.
