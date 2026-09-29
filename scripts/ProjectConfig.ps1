# Shared release paths. Program.cs is the single source of version information.
function Get-ProjectConfiguration {
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $program = [IO.File]::ReadAllText((Join-Path $projectRoot 'Program.cs'))
    $match = [regex]::Match($program, 'AssemblyFileVersion\("(?<version>\d+\.\d+\.\d+\.\d+)"\)')
    if (!$match.Success) { throw 'Program.cs must declare a four-part AssemblyFileVersion.' }
    $assemblyVersion = [Version]$match.Groups['version'].Value
    $version = '{0}.{1}' -f $assemblyVersion.Major, $assemblyVersion.Minor
    if ($assemblyVersion.Build -gt 0 -or $assemblyVersion.Revision -gt 0) { $version += '.' + $assemblyVersion.Build }
    if ($assemblyVersion.Revision -gt 0) { $version += '.' + $assemblyVersion.Revision }
    $releaseDirectory = Join-Path $projectRoot ('dist-v' + $version)
    [PSCustomObject]@{
        ProjectRoot = $projectRoot
        Version = $version
        AssemblyVersion = $assemblyVersion.ToString()
        ReleaseDirectory = $releaseDirectory
        ApplicationPath = Join-Path $releaseDirectory 'Primer Designer.exe'
        ArchivePath = Join-Path $projectRoot 'Primer Designer.zip'
        CompilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
        PowerShellPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    }
}

function Resolve-ProjectPath {
    param([string]$Path, [Parameter(Mandatory=$true)][string]$DefaultPath)
    if ([String]::IsNullOrWhiteSpace($Path)) { return [IO.Path]::GetFullPath($DefaultPath) }
    if (![IO.Path]::IsPathRooted($Path)) { $Path = Join-Path (Get-ProjectConfiguration).ProjectRoot $Path }
    return [IO.Path]::GetFullPath($Path)
}

function Write-ProjectUtf8 {
    param([Parameter(Mandatory=$true)][string]$Path, [Parameter(Mandatory=$true)][AllowEmptyString()][string]$Content)
    [IO.File]::WriteAllText($Path, $Content, (New-Object Text.UTF8Encoding($true)))
}
