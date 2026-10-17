param([ValidatePattern('^[A-Za-z0-9-]+$')][string]$Name='Instrumented')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$copyRoot=Join-Path $repoRoot "artifacts/isolate-05/sources/$Name/src"
if (-not (Test-Path -LiteralPath $copyRoot)) {
    & (Join-Path $PSScriptRoot 'Prepare-Copy.ps1') -Name $Name -From Baseline
} else {
    $baseline = Get-Content (Join-Path $repoRoot 'artifacts/isolate-05/copy-Baseline.json') -Raw | ConvertFrom-Json
    foreach($entry in $baseline) {
        $existing = Join-Path (Split-Path -Parent $copyRoot) $entry.Path
        if ((Get-FileHash -LiteralPath $existing).Hash -ne $entry.CopySHA256) { throw 'Existing copy differs from baseline.' }
    }
}
foreach($sourceFile in 'App.xaml.cs','MainWindow.xaml.cs','StartupTrace.cs') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "instrumentation/$sourceFile") -Destination (Join-Path $copyRoot "CleanSpace.App/$sourceFile")
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'instrumentation/SettingsView.xaml.cs') -Destination (Join-Path $copyRoot 'CleanSpace.App/Views/SettingsView.xaml.cs')
$adapter=Join-Path $copyRoot 'CleanSpace.Infrastructure/Settings/JsonThemeSettingsStore.cs'
$text=Get-Content -LiteralPath $adapter -Raw
$text=$text.Replace('        var fixture = Environment.GetEnvironmentVariable("CLEANSPACE_ISOLATE05_SETTINGS");', '        Console.Error.WriteLine("Isolate: settings fixture path resolution entry");' + [Environment]::NewLine + '        var fixture = Environment.GetEnvironmentVariable("CLEANSPACE_ISOLATE05_SETTINGS");')
$text=$text.Replace('        return fixture;', '        Console.Error.WriteLine("Isolate: settings fixture path resolution returned");' + [Environment]::NewLine + '        return fixture;')
[IO.File]::WriteAllText($adapter,$text)
