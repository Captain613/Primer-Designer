[CmdletBinding(SupportsShouldProcess=$true)]
param([switch]$BuildArtifacts)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ProjectConfig.ps1')
$config = Get-ProjectConfiguration
$root = (Resolve-Path -LiteralPath $config.ProjectRoot).ProviderPath.TrimEnd('\')
$keepVersion = [Version]$config.Version
$targets = New-Object 'System.Collections.Generic.List[object]'
foreach ($item in (Get-ChildItem -LiteralPath $root -Force)) {
    $oldRelease = $false
    if ($item.Name -eq 'dist') { $oldRelease = $true }
    elseif ($item.Name -match '^(?:dist-v|RPA-Designer-v)(?<version>\d+\.\d+(?:\.\d+){0,2})(?:-Windows-x64(?:\.zip(?:\.sha256\.txt)?)?)?$') {
        $oldRelease = ([Version]$Matches['version'] -lt $keepVersion)
    }
    if ($oldRelease -or ($BuildArtifacts -and $item.Name -in @('build','test-output','self-test-report.txt'))) {
        $targets.Add($item)
    }
}
# Validate the entire deletion set before touching any path. Never traverse
# junctions/symlinks, delete the workspace root, or include the current release.
$runningPaths = @(Get-Process -ErrorAction SilentlyContinue | ForEach-Object { $_.Path } | Where-Object { $_ })
foreach ($item in $targets) {
    $resolved = (Resolve-Path -LiteralPath $item.FullName).ProviderPath.TrimEnd('\')
    if ((Split-Path -Parent $resolved) -ne $root -or !$resolved.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) {
        throw ('Cleanup target is not a direct child of this workspace: '+$resolved)
    }
    if ($resolved -eq $config.ReleaseDirectory -or $resolved -eq $config.ArchivePath -or $item.Name -eq '.git') {
        throw ('Refusing to delete protected path: '+$resolved)
    }
    $entries = @($item)
    if ($item.PSIsContainer) { $entries += @(Get-ChildItem -LiteralPath $resolved -Force -Recurse) }
    foreach ($entry in $entries) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw ('Cleanup will not traverse a reparse point: '+$entry.FullName)
        }
    }
    $running = @($runningPaths | Where-Object {
        $_ -eq $resolved -or $_.StartsWith($resolved+'\',[StringComparison]::OrdinalIgnoreCase)
    })
    if ($running.Count -gt 0) { throw ('A process is using this cleanup directory; close it first: '+$resolved) }
}
$bytes = [long]0
$count = 0
foreach ($item in $targets) {
    $size = if ($item.PSIsContainer) { (Get-ChildItem -LiteralPath $item.FullName -Force -File -Recurse | Measure-Object Length -Sum).Sum } else { $item.Length }
    if ($PSCmdlet.ShouldProcess($item.FullName,'Remove obsolete release or build output')) {
        Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction Stop
        $bytes += [long]$size
        $count++
    }
}
[PSCustomObject]@{ LatestVersion=$config.Version;RemovedTargets=$count;RemovedBytes=$bytes;CurrentRelease=$config.ReleaseDirectory }
