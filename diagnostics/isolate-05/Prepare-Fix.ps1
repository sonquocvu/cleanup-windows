param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+$')][string]$Name,
    [ValidatePattern('^[A-Za-z0-9-]+$')][string]$From='Instrumented'
)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Prepare-Copy.ps1') -Name $Name -From $From
$repoRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$path=Join-Path $repoRoot "artifacts/isolate-05/sources/$Name/src/CleanSpace.App/MainWindow.xaml"
$xaml=Get-Content -LiteralPath $path -Raw
if ([regex]::Matches($xaml,'Icon="Lock"').Count -ne 1) { throw 'Expected exactly one failing icon literal.' }
[IO.File]::WriteAllText($path,$xaml.Replace('Icon="Lock"','Icon="ProtectedDocument"'))
