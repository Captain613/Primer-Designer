param([string]$ShortcutPath, [string]$TestOutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ProjectConfig.ps1')
$config = Get-ProjectConfiguration
$application = $config.ApplicationPath
if (!(Test-Path -LiteralPath $application -PathType Leaf)) { throw 'The current release executable is missing.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($application).FileVersion
if ($version -ne $config.AssemblyVersion) { throw 'The executable version differs from Program.cs.' }
$checks = Resolve-ProjectPath -Path $TestOutputDirectory -DefaultPath (Join-Path $config.ProjectRoot 'build\checks')
$summary = Get-Content -LiteralPath (Join-Path $checks 'test-summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$hash = (Get-FileHash -LiteralPath $application -Algorithm SHA256).Hash
if ($summary.Status -ne 'Passed' -or $summary.ApplicationSHA256 -ne $hash) { throw 'Shortcut update requires passing checks for this executable.' }
if ([String]::IsNullOrWhiteSpace($ShortcutPath)) {
    $ShortcutPath = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Primer Designer.lnk'
}
$ShortcutPath = [IO.Path]::GetFullPath($ShortcutPath)
if ([IO.Path]::GetExtension($ShortcutPath) -ine '.lnk') { throw 'Expected a Windows shortcut path.' }
$backup = $null
if (Test-Path -LiteralPath $ShortcutPath -PathType Leaf) {
    $backupDirectory = Join-Path $config.ProjectRoot 'build\shortcut-backup'
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    $backup = Join-Path $backupDirectory ('Primer Designer-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.lnk')
    Copy-Item -LiteralPath $ShortcutPath -Destination $backup
}
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($ShortcutPath)
$shortcut.TargetPath = $application
$shortcut.WorkingDirectory = $config.ReleaseDirectory
$shortcut.Arguments = ''
$shortcut.IconLocation = (Join-Path $config.ProjectRoot 'assets\PrimerDesigner-v1.ico') + ',0'
$shortcut.Description = 'Primer Designer v' + $config.Version + ' | RPA / LAMP | Manual BLAST review'
$shortcut.Save()
$verified = $shell.CreateShortcut($ShortcutPath)
if ($verified.TargetPath -ne $application -or $verified.WorkingDirectory -ne $config.ReleaseDirectory -or $verified.Arguments -ne '') {
    throw 'Shortcut verification failed.'
}
[PSCustomObject]@{ Shortcut=$ShortcutPath; Target=$verified.TargetPath; Version=$version; ApplicationSHA256=$hash; Backup=$backup } | ConvertTo-Json
