param(
    [Parameter(Mandatory)]
    [ValidateSet('2.5.1', '2.4.0')]
    [string]$Sdk,
    [Parameter(Mandatory)]
    [ValidateSet('Window', 'Resources')]
    [string]$Variant,
    [Parameter(Mandatory)]
    [ValidateRange(1, 99)]
    [int]$Run
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifacts = Join-Path $repoRoot 'artifacts/compat-03'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$projectDirectory = if ($Sdk -eq '2.5.1') { 'diagnostics/WinUiStartupProbe' } else { 'diagnostics/WinUiStartupProbe240' }
$projectRoot = Join-Path $repoRoot $projectDirectory
$outputRoot = Join-Path $projectRoot 'bin/Debug/net10.0-windows10.0.19041.0/win-x64'
$exePath = Join-Path $outputRoot 'WinUiStartupProbe.exe'
$runId = "$Sdk-$Variant-$Run"
$jsonPath = Join-Path $artifacts "$runId.json"
if (Test-Path -LiteralPath $jsonPath) { throw 'Run ID already recorded; use a new run number.' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this experiment from a non-elevated user session.'
}

$assets = Get-Content -LiteralPath (Join-Path $projectRoot 'obj/project.assets.json') -Raw | ConvertFrom-Json
$packageNames = @($assets.libraries.PSObject.Properties.Name)
if ($packageNames -notcontains "Microsoft.WindowsAppSDK/$Sdk") { throw 'Resolved SDK differs from requested SDK.' }
$winuiPackage = @($packageNames | Where-Object { $_ -like 'Microsoft.WindowsAppSDK.WinUI/*' })
if ($winuiPackage.Count -ne 1) { throw 'Expected exactly one resolved WinUI package.' }
$cacheRoot = @($assets.packageFolders.PSObject.Properties.Name)[0]
$packageXamlPath = Join-Path $cacheRoot "$($winuiPackage[0].ToLowerInvariant())/runtimes-framework/win-x64/native/Microsoft.UI.Xaml.dll"
$expectedXamlPath = Join-Path $outputRoot 'Microsoft.UI.Xaml.dll'
$expectedXamlHash = (Get-FileHash -LiteralPath $packageXamlPath).Hash

function Get-CrashEvents {
    param([datetime]$Since, [int]$ProcessId)
    $found = @()
    $candidates = @(Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=$Since;Id=1000,1026} -ErrorAction SilentlyContinue)
    foreach ($event in $candidates) {
        [xml]$xml = $event.ToXml()
        $fields = [ordered]@{}
        foreach ($datum in $xml.Event.EventData.Data) { $fields[[string]$datum.Name] = [string]$datum.'#text' }
        $eventPid = $fields['ProcessId']
        $matchesPid = $false
        if ($eventPid -match '^0x') { $matchesPid = [Convert]::ToInt32($eventPid.Substring(2),16) -eq $ProcessId }
        elseif ($eventPid -match '^\d+$') { $matchesPid = [int]$eventPid -eq $ProcessId }
        if ($matchesPid -or ($event.Id -eq 1026 -and $xml.Event.System.Execution.ProcessID -eq $ProcessId)) {
            $found += [ordered]@{Time=$event.TimeCreated.ToString('o');Id=$event.Id;RecordId=$event.RecordId;Fields=$fields;Message=$event.Message}
        }
    }
    return $found
}

$stderr = Join-Path $artifacts "$runId-stderr.log"
$started = Get-Date
$options = @{
    FilePath=$exePath;WorkingDirectory=$outputRoot;PassThru=$true
    WindowStyle='Normal';RedirectStandardError=$stderr
}
if ($Variant -eq 'Resources') { $options.ArgumentList = '--controls' }
$child = Start-Process @options
$retainedHandle = $child.Handle
$loadedPath = $null
$observedHandle = 0L
$observedTitle = ''
$activationMarker = $false
$watch = [Diagnostics.Stopwatch]::StartNew()
while ($watch.Elapsed.TotalSeconds -lt 5 -and -not $child.HasExited) {
    try {
        $module = $child.Modules | Where-Object { $_.ModuleName -ieq 'Microsoft.UI.Xaml.dll' } | Select-Object -First 1
        if ($module) { $loadedPath = $module.FileName }
        if ($child.MainWindowHandle -ne [IntPtr]::Zero) {
            $observedHandle = $child.MainWindowHandle.ToInt64()
            $observedTitle = $child.MainWindowTitle
        }
        $activationMarker = @(Get-Content -LiteralPath $stderr -ErrorAction SilentlyContinue) -contains 'Probe: activated'
        if ($activationMarker -and $observedHandle -ne 0 -and $loadedPath) { break }
    } catch [InvalidOperationException] {
        # The process can exit while its window/modules are being observed.
    }
    Start-Sleep -Milliseconds 25
    $child.Refresh()
}

$checkpoint = [ordered]@{RunId=$runId;Pid=$child.Id;Exited=$child.HasExited;WindowHandle=$observedHandle;WindowTitle=$observedTitle;ActivationMarker=$activationMarker;LoadedXamlPath=$loadedPath}
$checkpoint | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts "$runId-checkpoint.json")
$checkpoint | ConvertTo-Json -Compress | Write-Output
if (-not $child.HasExited) {
    if ($activationMarker -and $observedHandle -ne 0) {
        Write-Output 'Window ready for normal UI closure; waiting up to 45 seconds. No process is killed.'
    } else {
        Write-Output 'Process has not exited; no activated window observed. Waiting up to 45 seconds without killing it.'
    }
    $null = $child.WaitForExit(45000)
}
$exited = $child.HasExited
$childExit = if ($exited) { $child.ExitCode } else { $null }
$events = @(Get-CrashEvents -Since $started -ProcessId $child.Id)
if ($exited -and $childExit -ne 0 -and $events.Count -eq 0) {
    Start-Sleep -Milliseconds 750
    $events = @(Get-CrashEvents -Since $started -ProcessId $child.Id)
}
$moduleEvidence = 'Process.Modules'
if (-not $loadedPath) {
    $fault = @($events | Where-Object { $_.Id -eq 1000 -and $_.Fields['ModuleName'] -ieq 'Microsoft.UI.Xaml.dll' })
    if ($fault.Count -gt 0) { $loadedPath=$fault[0].Fields['ModulePath'];$moduleEvidence='Event1000.ModulePath' }
}
$loadedHash = if ($loadedPath) { (Get-FileHash -LiteralPath $loadedPath).Hash } else { $null }
$loadedVersion = if ($loadedPath) { (Get-Item -LiteralPath $loadedPath).VersionInfo.FileVersion } else { $null }
$markers = @(Get-Content -LiteralPath $stderr)
$result = [ordered]@{
    RunId=$runId;Sdk=$Sdk;Variant=$Variant;Run=$Run;Started=$started.ToString('o');Pid=$child.Id;Elevated=$false
    Executable=$exePath;ExecutableSHA256=(Get-FileHash -LiteralPath $exePath).Hash
    ManagedAssemblySHA256=(Get-FileHash -LiteralPath (Join-Path $outputRoot 'WinUiStartupProbe.dll')).Hash
    ProgramSourceSHA256=(Get-FileHash -LiteralPath (Join-Path $repoRoot 'diagnostics/WinUiStartupProbe/Program.cs')).Hash
    ResolvedSdk="Microsoft.WindowsAppSDK/$Sdk";ResolvedWinUiPackage=$winuiPackage[0]
    ExpectedXamlPath=$expectedXamlPath;ExpectedPackageXamlSHA256=$expectedXamlHash
    LoadedXamlPath=$loadedPath;LoadedXamlFileVersion=$loadedVersion;LoadedXamlSHA256=$loadedHash;ModuleEvidence=$moduleEvidence
    RuntimePayloadMatches=($loadedPath -ieq $expectedXamlPath -and $loadedHash -eq $expectedXamlHash)
    WindowHandle=$observedHandle;WindowTitle=$observedTitle;WindowActivationMarker=$activationMarker
    Exited=$exited;ExitCode=$childExit;LastMarker=if($markers.Count){$markers[-1]}else{$null};Markers=$markers
    Events=$events
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath
$result | ConvertTo-Json -Depth 8
