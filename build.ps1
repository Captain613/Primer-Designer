param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\ProjectConfig.ps1')
$config = Get-ProjectConfiguration
$output = Resolve-ProjectPath -Path $OutputDirectory -DefaultPath $config.ReleaseDirectory
if (!(Test-Path -LiteralPath $config.CompilerPath -PathType Leaf)) {
    throw 'Requires 64-bit Windows with .NET Framework 4.x.'
}
$application = Join-Path $output 'Primer Designer.exe'
$running = Get-Process -Name 'Primer Designer' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $application }
if ($running) { throw 'Close the running designer before rebuilding it, or use -OutputDirectory build\verify-release.' }
$sources = @(Get-ChildItem -LiteralPath $config.ProjectRoot -File -Filter '*.cs' | Sort-Object Name | Select-Object -ExpandProperty FullName)
if ($sources.Count -eq 0) { throw 'No C# source files were found.' }
$manifest = Join-Path $config.ProjectRoot 'app.manifest'
[xml]$manifestXml = [IO.File]::ReadAllText($manifest)
$manifestVersion = $manifestXml.SelectSingleNode("/*[local-name()='assembly']/*[local-name()='assemblyIdentity']").GetAttribute('version')
if ($manifestVersion -ne $config.AssemblyVersion) { throw 'Program.cs and app.manifest versions do not match.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $config.CompilerPath /nologo /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 "/out:$application" "/win32manifest:$manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $config.ProjectRoot 'README.md') -Destination (Join-Path $output 'README.zh-CN.md') -Force
Write-Output ('Built v'+$config.Version+': '+$application)
