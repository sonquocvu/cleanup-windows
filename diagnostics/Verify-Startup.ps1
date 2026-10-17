param(
    [ValidateSet('App', 'Probe', 'ProbeControls')]
    [string]$Target = 'App'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $repoRoot 'artifacts/runtime'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$targetProject = if ($Target -eq 'App') { 'src/CleanSpace.App' } else { 'diagnostics/WinUiStartupProbe' }
$targetName = if ($Target -eq 'App') { 'CleanSpace.App' } else { 'WinUiStartupProbe' }
$exePath = Join-Path $repoRoot "$targetProject/bin/Debug/net10.0-windows10.0.19041.0/win-x64/$targetName.exe"
$localSettingsDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CleanSpace'
$localSettingsPath = Join-Path $localSettingsDirectory 'settings.json'

function Get-OwnedSettingsSnapshot {
    $directory = Get-Item -LiteralPath $localSettingsDirectory -Force -ErrorAction SilentlyContinue
    $file = Get-Item -LiteralPath $localSettingsPath -Force -ErrorAction SilentlyContinue
    $redirected = ($directory -and ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) -or
        ($file -and ($file.Attributes -band [IO.FileAttributes]::ReparsePoint))
    [ordered]@{
        DirectoryExists = $null -ne $directory
        FileExists = $null -ne $file
        Redirected = [bool]$redirected
        SHA256 = if ($file -and -not $redirected -and -not $file.PSIsContainer) {
            (Get-FileHash -LiteralPath $localSettingsPath).Hash
        } else { $null }
    }
}

$settingsBefore = Get-OwnedSettingsSnapshot
$launchStart = Get-Date
$runName = "$Target-$($launchStart.ToString('yyyyMMdd-HHmmss-fff'))"
$startOptions = @{
    FilePath = $exePath
    WorkingDirectory = Split-Path -Parent $exePath
    PassThru = $true
    WindowStyle = 'Normal'
    RedirectStandardError = Join-Path $outputDirectory "$runName-stderr.log"
}
if ($Target -eq 'ProbeControls') { $startOptions.ArgumentList = '--controls' }
$launchedProcess = Start-Process @startOptions
$loadedXaml = $null
for ($attempt = 0; $attempt -lt 30 -and -not $launchedProcess.HasExited; $attempt++) {
    try {
        $loadedXaml = $launchedProcess.Modules |
            Where-Object { $_.ModuleName -ieq 'Microsoft.UI.Xaml.dll' } | Select-Object -First 1
        if ($loadedXaml) { break }
    } catch [InvalidOperationException] {
        # The process may exit while its modules are queried.
    }
    Start-Sleep -Milliseconds 100
    $launchedProcess.Refresh()
}
$exited = $launchedProcess.WaitForExit(10000)
$launchedProcess.Refresh()
$events = @(Get-WinEvent -FilterHashtable @{
    LogName = 'Application'
    StartTime = $launchStart
    Id = 1000, 1026
} -ErrorAction SilentlyContinue | Where-Object {
    $_.Message -match [regex]::Escape("$targetName.exe")
} | Select-Object TimeCreated, Id, RecordId, Message)
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$result = [ordered]@{
    Target = $Target
    Start = $launchStart.ToString('o')
    Executable = $exePath
    SHA256 = (Get-FileHash -LiteralPath $exePath).Hash
    Pid = $launchedProcess.Id
    Elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Exited = $exited
    ExitCode = if ($exited) { $launchedProcess.ExitCode } else { $null }
    WindowHandle = $launchedProcess.MainWindowHandle.ToInt64()
    WindowTitle = $launchedProcess.MainWindowTitle
    LoadedXamlPath = if ($loadedXaml) { $loadedXaml.FileName } else { $null }
    SettingsBefore = $settingsBefore
    SettingsAfter = Get-OwnedSettingsSnapshot
    Events = $events
}
$result | ConvertTo-Json -Depth 6 | Tee-Object -FilePath (Join-Path $outputDirectory "$runName.json")
Get-Content -LiteralPath $startOptions.RedirectStandardError
# A surviving process remains open for interactive checks. The script never kills a process.
