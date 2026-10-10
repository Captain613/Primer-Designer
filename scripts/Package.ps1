param([string]$ApplicationPath, [string]$TestOutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ProjectConfig.ps1')
$config = Get-ProjectConfiguration
$application = Resolve-ProjectPath -Path $ApplicationPath -DefaultPath $config.ApplicationPath
$checks = Resolve-ProjectPath -Path $TestOutputDirectory -DefaultPath (Join-Path $config.ProjectRoot 'build\checks')
if (!(Test-Path -LiteralPath $application -PathType Leaf)) { throw 'Build the current application first.' }
$applicationHash = (Get-FileHash -LiteralPath $application -Algorithm SHA256).Hash
if (!(Test-Path -LiteralPath $config.ApplicationPath -PathType Leaf) -or (Get-FileHash -LiteralPath $config.ApplicationPath -Algorithm SHA256).Hash -ne $applicationHash) {
    throw 'Build the application into its current release directory before packaging. An alternate ApplicationPath must match the installed release EXE.'
}
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($application).FileVersion
if ($fileVersion -ne $config.AssemblyVersion) { throw ('Application version ' + $fileVersion + ' does not match Program.cs ' + $config.AssemblyVersion + '.') }
$summaryPath = Join-Path $checks 'test-summary.json'
if (!(Test-Path -LiteralPath $summaryPath -PathType Leaf)) { throw 'Run scripts/Test.ps1 before packaging.' }
$summary = Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($summary.Status -ne 'Passed' -or $summary.ApplicationSHA256 -ne $applicationHash) { throw 'Test results must pass and match the current application SHA256. Run scripts/Test.ps1 again.' }
foreach ($suite in @('self-test', 'snp-self-test', 'highlight-self-test', 'lamp-self-test', 'lamp-parts-self-test', 'lamp-region-tm-self-test', 'lamp-thermodynamics-self-test', 'pa-lamp-self-test', 'mlamp-self-test', 'unlimited-rpa-self-test', 'unlimited-lamp-self-test', 'blast-online-self-test', 'blast-analysis-self-test', 'blast-manual-self-test')) {
    if (@($summary.Reports | Where-Object { $_.Suite -eq $suite }).Count -ne 1) { throw ('Missing current passing test suite: ' + $suite) }
}
# A new staging directory contains only files from this release. No historical
# release, stale output or cached report is copied into the archive.
$stageParent = Join-Path $config.ProjectRoot ('build\package-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $stageParent ('dist-v' + $config.Version)
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -LiteralPath $application -Destination (Join-Path $stage 'Primer Designer.exe')
foreach ($report in $summary.Reports) {
    $reportSource = [IO.Path]::GetFullPath((Join-Path $checks $report.Path))
    if (!$reportSource.StartsWith($checks.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Report path must remain inside the test output directory.' }
    if (!(Test-Path -LiteralPath $reportSource -PathType Leaf) -or (Get-FileHash -LiteralPath $reportSource -Algorithm SHA256).Hash -ne $report.SHA256) { throw ('Test report missing or modified: ' + $report.Path) }
    $destination = Join-Path $stage $report.Path
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $reportSource -Destination $destination
}
Copy-Item -LiteralPath $summaryPath -Destination (Join-Path $stage 'test-summary.json')

# Assembly.LoadFrom remains confined to a child process which exits before
# Compress-Archive opens the staged EXE.
$generatorArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + (Join-Path $PSScriptRoot 'GenerateExamples.ps1') + '"'), '-ApplicationPath', ('"' + (Join-Path $stage 'Primer Designer.exe') + '"'), '-OutputDirectory', ('"' + $stage + '"'))
$generator = Start-Process -FilePath $config.PowerShellPath -ArgumentList $generatorArguments -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $stageParent 'examples.stdout.txt') -RedirectStandardError (Join-Path $stageParent 'examples.stderr.txt')
if ($generator.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $stageParent 'examples.stderr.txt')
    throw ('Example generation failed. Logs: ' + $stageParent)
}
Get-Content -LiteralPath (Join-Path $stageParent 'examples.stdout.txt')
Copy-Item -LiteralPath (Join-Path $config.ProjectRoot 'README.md') -Destination (Join-Path $stage 'README.zh-CN.md')

$source = Join-Path $stage 'source'
New-Item -ItemType Directory -Path $source -Force | Out-Null
Get-ChildItem -LiteralPath $config.ProjectRoot -Filter '*.cs' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $source $_.Name) }
foreach ($name in @('app.manifest', 'build.ps1', 'README.md', '.gitignore')) {
    $file = Join-Path $config.ProjectRoot $name
    if (Test-Path -LiteralPath $file -PathType Leaf) { Copy-Item -LiteralPath $file -Destination (Join-Path $source $name) }
}
foreach ($directory in @('scripts', 'tests', 'docs')) {
    $directoryPath = Join-Path $config.ProjectRoot $directory
    foreach ($file in (Get-ChildItem -LiteralPath $directoryPath -Recurse -File | Where-Object { $_.Extension -in @('.ps1', '.cs', '.md') })) {
        $relative = $file.FullName.Substring($config.ProjectRoot.Length + 1)
        $destination = Join-Path $source $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}
$assetDirectory = Join-Path $source 'assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $config.ProjectRoot 'assets') -File | Where-Object { $_.Extension -in @('.ico', '.png', '.svg', '.md') })) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $assetDirectory $file.Name)
    if ($file.Extension -eq '.ico') { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $stage $file.Name) }
}
Write-ProjectUtf8 -Path (Join-Path $stage 'SHA256.txt') -Content ($applicationHash + '  Primer Designer.exe' + [Environment]::NewLine)
Compress-Archive -LiteralPath $stage -DestinationPath $config.ArchivePath -Force
$archiveHash = (Get-FileHash -LiteralPath $config.ArchivePath -Algorithm SHA256).Hash
Write-ProjectUtf8 -Path ($config.ArchivePath + '.sha256.txt') -Content ($archiveHash + '  ' + [IO.Path]::GetFileName($config.ArchivePath) + [Environment]::NewLine)

# Preserve the installed EXE, which may be running. Publish only fresh supporting
# files; build.ps1 is responsible for replacing the EXE when explicitly rebuilt.
New-Item -ItemType Directory -Path $config.ReleaseDirectory -Force | Out-Null
foreach ($file in (Get-ChildItem -LiteralPath $stage -Recurse -File)) {
    $relative = $file.FullName.Substring($stage.Length + 1)
    if ($relative -eq 'Primer Designer.exe') { continue }
    $destination = Join-Path $config.ReleaseDirectory $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
}
Write-Output ('Packaged: ' + $config.ArchivePath)
Write-Output ('Application SHA256: ' + $applicationHash)
Write-Output ('Archive SHA256: ' + $archiveHash)
Write-Output ('Temporary staging: ' + $stageParent)
