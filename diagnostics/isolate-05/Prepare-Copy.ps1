param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+$')][string]$Name,
    [ValidatePattern('^[A-Za-z0-9-]+$')][string]$From
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$evidence = Join-Path $repoRoot 'artifacts/isolate-05'
$destination = Join-Path $evidence "sources/$Name"
if (Test-Path -LiteralPath $destination) { throw 'Copy already exists; choose a new name.' }
$origin = if ($From) { Join-Path $evidence "sources/$From" } else { $repoRoot }
$files = @('Directory.Build.props','Directory.Packages.props','global.json','NuGet.Config')
if ($From) {
    $files += @(rg --files --no-ignore -g '!**/bin/**' -g '!**/obj/**' (Join-Path $origin 'src')) | ForEach-Object { [IO.Path]::GetRelativePath($origin, $_) }
} else {
    $files += @((git -C $repoRoot ls-files src) + (git -C $repoRoot ls-files --others --exclude-standard src)) | Sort-Object -Unique
}
$manifest = foreach ($relative in $files) {
    $sourcePath = Join-Path $origin $relative
    $target = Join-Path $destination $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $target
    [pscustomobject]@{Path=$relative;OriginalSHA256=(Get-FileHash -LiteralPath $sourcePath).Hash}
}
if (-not $From) {
    # The one required safety difference: never resolve/read the real user's settings.
    $adapter = Join-Path $destination 'src/CleanSpace.Infrastructure/Settings/JsonThemeSettingsStore.cs'
    $text = Get-Content -LiteralPath $adapter -Raw
    $pattern = '(?s)    private static string GetDefaultSettingsDirectory\(\)\s*\{.*?\r?\n    \}'
    $replacement = @'
    private static string GetDefaultSettingsDirectory()
    {
        var fixture = Environment.GetEnvironmentVariable("CLEANSPACE_ISOLATE05_SETTINGS");
        if (string.IsNullOrWhiteSpace(fixture) || !Path.IsPathFullyQualified(fixture))
        {
            throw new InvalidOperationException("The diagnostic settings fixture is required.");
        }
        return fixture;
    }
'@
    if ([regex]::Matches($text,$pattern).Count -ne 1) { throw 'Unexpected settings adapter structure.' }
    [IO.File]::WriteAllText($adapter,[regex]::Replace($text,$pattern,$replacement))
}
$comparison = foreach ($entry in $manifest) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $destination $entry.Path)).Hash
    [pscustomobject]@{Path=$entry.Path;OriginalSHA256=$entry.OriginalSHA256;CopySHA256=$hash;Equal=($hash -eq $entry.OriginalSHA256)}
}
$comparison | ConvertTo-Json | Set-Content (Join-Path $evidence "copy-$Name.json")
[pscustomobject]@{Name=$Name;Origin=$origin;Destination=$destination;Files=$comparison.Count;Differences=@($comparison | Where-Object {-not $_.Equal}).Path} | ConvertTo-Json
