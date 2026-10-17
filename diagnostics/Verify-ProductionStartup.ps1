param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+$')][string]$RunId,
    [ValidateRange(30, 1800)][int]$TimeoutSeconds = 600
)

# Interactive Windows smoke test: launch the real production EXE; close its window normally.
# No UI input injection, termination, settings writes, diagnostic-copy substitution or elevation.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repoRoot 'artifacts/fix-06/launches'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$jsonPath = Join-Path $artifacts "$RunId.json"
if (Test-Path -LiteralPath $jsonPath) { throw 'Run ID already exists.' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Use a non-elevated Windows user session.'
}
$projectRoot = Join-Path $repoRoot 'src/CleanSpace.App'
$outputRoot = Join-Path $projectRoot 'bin/Debug/net10.0-windows10.0.19041.0/win-x64'
$exePath = Join-Path $outputRoot 'CleanSpace.App.exe'
if (@(Get-Process 'CleanSpace.App' -ErrorAction SilentlyContinue).Count) { throw 'Close existing CleanSpace windows first.' }
$assets = Get-Content -LiteralPath (Join-Path $projectRoot 'obj/project.assets.json') -Raw | ConvertFrom-Json
$packages = @($assets.libraries.PSObject.Properties.Name)
if ($packages -notcontains 'Microsoft.WindowsAppSDK/2.5.1') { throw 'Unexpected SDK version.' }
$winui = @($packages | Where-Object { $_ -like 'Microsoft.WindowsAppSDK.WinUI/*' })
if ($winui.Count -ne 1) { throw 'Expected one resolved WinUI package.' }
$cache = @($assets.packageFolders.PSObject.Properties.Name)[0]
$packageDll = Join-Path $cache "$($winui[0].ToLowerInvariant())/runtimes-framework/win-x64/native/Microsoft.UI.Xaml.dll"
$expectedHash = (Get-FileHash -LiteralPath $packageDll).Hash
$expectedPath = Join-Path $outputRoot 'Microsoft.UI.Xaml.dll'

function Read-CrashEvents {
    param([datetime]$Since, [int]$ProcessId)
    $queryErrors = @()
    $candidates = @(Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=$Since;Id=1000,1026} -ErrorAction SilentlyContinue -ErrorVariable queryErrors)
    $failures = @($queryErrors | Where-Object { $_.FullyQualifiedErrorId -notmatch 'NoMatchingEventsFound' })
    $matches = foreach ($event in $candidates) {
        [xml]$xml = $event.ToXml()
        $fields = [ordered]@{}
        foreach ($datum in $xml.Event.EventData.Data) { $fields[[string]$datum.Name] = [string]$datum.'#text' }
        $eventPid = $fields['ProcessId']
        $samePid = if ($eventPid -match '^0x') { [Convert]::ToInt32($eventPid.Substring(2),16) -eq $ProcessId }
            elseif ($eventPid -match '^\d+$') { [int]$eventPid -eq $ProcessId } else { $false }
        if ($samePid -or ($event.Id -eq 1026 -and $xml.Event.System.Execution.ProcessID -eq $ProcessId)) {
            [ordered]@{Time=$event.TimeCreated.ToString('o');Id=$event.Id;RecordId=$event.RecordId;Fields=$fields;Message=$event.Message}
        }
    }
    return [pscustomobject]@{Succeeded=($failures.Count -eq 0);ErrorIds=@($failures | ForEach-Object {$_.FullyQualifiedErrorId});Events=@($matches)}
}

$stderr = Join-Path $artifacts "$RunId-stderr.log"
$started = Get-Date
$child = Start-Process -FilePath $exePath -WorkingDirectory $outputRoot -PassThru -WindowStyle Normal -RedirectStandardError $stderr
$retainedHandle = $child.Handle
$loadedPath = $null
$observedWindow = 0L
$observedTitle = ''
$readySince = $null
$stableWindow = $false
$watch = [Diagnostics.Stopwatch]::StartNew()
while (-not $child.HasExited -and $watch.Elapsed.TotalSeconds -lt 10) {
    try {
        $module = $child.Modules | Where-Object { $_.ModuleName -ieq 'Microsoft.UI.Xaml.dll' } | Select-Object -First 1
        if ($module) { $loadedPath = $module.FileName }
        if ($child.MainWindowHandle -ne [IntPtr]::Zero -and $child.MainWindowTitle -eq 'CleanSpace') {
            $observedWindow = $child.MainWindowHandle.ToInt64()
            $observedTitle = $child.MainWindowTitle
            if ($null -eq $readySince) { $readySince = $watch.Elapsed.TotalSeconds }
            if ($watch.Elapsed.TotalSeconds - $readySince -ge 2 -and $loadedPath) { $stableWindow = $true; break }
        } else { $readySince = $null }
    } catch [InvalidOperationException] {
        # Exit may race read-only process/window/module observation.
    }
    Start-Sleep -Milliseconds 25
    $child.Refresh()
}
$checkpoint = [ordered]@{RunId=$RunId;Pid=$child.Id;Exited=$child.HasExited;WindowHandle=$observedWindow;WindowTitle=$observedTitle;StableWindowObserved=$stableWindow;LoadedXamlPath=$loadedPath}
$checkpoint | ConvertTo-Json | Set-Content (Join-Path $artifacts "$RunId-checkpoint.json")
$checkpoint | ConvertTo-Json -Compress | Write-Output
Write-Output 'Perform UI checks, then close CleanSpace normally. The observer never kills a process.'
while (-not $child.HasExited -and $watch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    $remainingMs = [int][Math]::Max(1,($TimeoutSeconds - $watch.Elapsed.TotalSeconds) * 1000)
    $null = $child.WaitForExit([Math]::Min(30000,$remainingMs))
}
$exited = $child.HasExited
$childExit = if ($exited) { $child.ExitCode } else { $null }
$eventResult = Read-CrashEvents -Since $started -ProcessId $child.Id
if ($exited -and $childExit -ne 0 -and $eventResult.Events.Count -eq 0) {
    Start-Sleep -Milliseconds 750
    $eventResult = Read-CrashEvents -Since $started -ProcessId $child.Id
}
$loadedHash = if ($loadedPath) { (Get-FileHash -LiteralPath $loadedPath).Hash } else { $null }
$runtimeMatches = $loadedPath -ieq $expectedPath -and $loadedHash -eq $expectedHash
$passed = $exited -and $childExit -eq 0 -and $stableWindow -and $runtimeMatches -and $eventResult.Succeeded -and $eventResult.Events.Count -eq 0
$status = if (-not $exited -or -not $eventResult.Succeeded) { 'NOT VERIFIED' } elseif ($passed) { 'PASS' } else { 'FAIL' }
$result = [ordered]@{
    RunId=$RunId;Started=$started.ToString('o');Finished=(Get-Date).ToString('o');Pid=$child.Id;Elevated=$false
    Executable=$exePath;ExecutableSHA256=(Get-FileHash -LiteralPath $exePath).Hash
    ManagedAssemblySHA256=(Get-FileHash -LiteralPath (Join-Path $outputRoot 'CleanSpace.App.dll')).Hash
    MainWindowXbfSHA256=(Get-FileHash -LiteralPath (Join-Path $outputRoot 'MainWindow.xbf')).Hash
    WorkingDirectory=$outputRoot;ResolvedSdk='Microsoft.WindowsAppSDK/2.5.1';ResolvedWinUiPackage=$winui[0]
    LoadedXamlPath=$loadedPath;LoadedXamlSHA256=$loadedHash;ModuleEvidence='Process.Modules'
    LoadedXamlFileVersion=if($loadedPath){(Get-Item -LiteralPath $loadedPath).VersionInfo.FileVersion}else{$null}
    ExpectedPackageXamlSHA256=$expectedHash;RuntimePayloadMatches=$runtimeMatches
    StableWindowObserved=$stableWindow;WindowHandle=$observedWindow;WindowTitle=$observedTitle
    Exited=$exited;ExitCode=$childExit;EventLogQuerySucceeded=$eventResult.Succeeded;EventLogErrorIds=$eventResult.ErrorIds
    Events=$eventResult.Events;Status=$status
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath
$result | ConvertTo-Json -Depth 8
if ($status -eq 'PASS') { exit 0 }
if ($status -eq 'NOT VERIFIED') { exit 2 }
exit 1
