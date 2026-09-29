param([string]$ApplicationPath, [string]$OutputDirectory, [switch]$Ui)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ProjectConfig.ps1')
$config = Get-ProjectConfiguration
$application = Resolve-ProjectPath -Path $ApplicationPath -DefaultPath $config.ApplicationPath
$output = Resolve-ProjectPath -Path $OutputDirectory -DefaultPath (Join-Path $config.ProjectRoot 'build\checks')
$reports = New-Object 'System.Collections.Generic.List[object]'
$summary = $null
$summaryPath = Join-Path $output 'test-summary.json'

function Save-Summary {
    Write-ProjectUtf8 -Path $summaryPath -Content ($summary | ConvertTo-Json -Depth 6)
}
function Run-HiddenCheck([string]$Executable, [string[]]$Arguments, [string]$ReportPath, [string]$Suite) {
    # Paths are passed as quoted native arguments; no shell evaluation is used.
    $quoted = @($Arguments | ForEach-Object { '"' + $_ + '"' })
    $process = Start-Process -FilePath $Executable -ArgumentList $quoted -WorkingDirectory (Split-Path -Parent $Executable) -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw ($Suite + ' failed with exit code ' + $process.ExitCode + '. Report: ' + $ReportPath) }
    if (!(Test-Path -LiteralPath $ReportPath -PathType Leaf)) { throw ($Suite + ' did not create its report.') }
    $relative = $ReportPath.Substring($output.TrimEnd('\').Length + 1)
    $reports.Add([PSCustomObject]@{ Suite = $Suite; Path = $relative; SHA256 = (Get-FileHash -LiteralPath $ReportPath -Algorithm SHA256).Hash })
    Get-Content -LiteralPath $ReportPath -Encoding UTF8 | Select-Object -Last 3
}

try {
    if (!(Test-Path -LiteralPath $application -PathType Leaf)) { throw 'Build the current application first.' }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $summary = [PSCustomObject]@{
        Status = 'Running'
        Version = $config.Version
        ApplicationSHA256 = (Get-FileHash -LiteralPath $application -Algorithm SHA256).Hash
        StartedUtc = [DateTime]::UtcNow.ToString('o')
        CompletedUtc = $null
        Ui = [bool]$Ui
        Reports = @()
        Error = $null
    }
    Save-Summary
    foreach ($suite in @('self-test', 'snp-self-test', 'highlight-self-test', 'lamp-self-test', 'lamp-region-tm-self-test', 'lamp-thermodynamics-self-test', 'pa-lamp-self-test', 'mlamp-self-test', 'unlimited-rpa-self-test', 'unlimited-lamp-self-test', 'blast-online-self-test', 'blast-analysis-self-test')) {
        Run-HiddenCheck -Executable $application -Arguments @(('--' + $suite), (Join-Path $output ($suite + '-report.txt'))) -ReportPath (Join-Path $output ($suite + '-report.txt')) -Suite $suite
    }
    if ($Ui) {
        if (!(Test-Path -LiteralPath $config.CompilerPath -PathType Leaf)) { throw 'UI checks require 64-bit Windows with .NET Framework 4.x.' }
        $uiSources = @(Get-ChildItem -LiteralPath (Join-Path $config.ProjectRoot 'tests\ui') -Filter '*.cs' -File | Sort-Object Name)
        if ($uiSources.Count -eq 0) { throw 'No current UI checks found in tests/ui.' }
        foreach ($source in $uiSources) {
            $suiteOutput = Join-Path $output ('ui\' + $source.BaseName)
            New-Item -ItemType Directory -Path $suiteOutput -Force | Out-Null
            $runner = Join-Path $suiteOutput ($source.BaseName + '.exe')
            Copy-Item -LiteralPath $application -Destination (Join-Path $suiteOutput 'Primer Designer.exe') -Force
            & $config.CompilerPath /nologo /target:exe /platform:x64 /optimize+ /utf8output /codepage:65001 "/out:$runner" "/win32manifest:$($config.ProjectRoot)\app.manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/reference:$application" $source.FullName
            if ($LASTEXITCODE -ne 0) { throw ($source.BaseName + ' compilation failed.') }
            $reportName = switch ($source.BaseName) {
                'LampUiCheck' { 'lamp-ui-test-report.txt' }
                'MLampUiCheck' { 'mlamp-ui-test-report.txt' }
                'SnpHighlightUiCheck' { 'snp-highlight-ui-test-report.txt' }
                'UnlimitedUiCheck' { 'unlimited-ui-test-report.txt' }
                'LampPositionMapCheck' { 'lamp-position-map-test-report.txt' }
                'SequenceMapCheck' { 'sequence-map-test-report.txt' }
                'BlastUiCheck' { 'blast-ui-test-report.txt' }
                'LampRegionTmUiCheck' { 'lamp-region-tm-ui-test-report.txt' }
                default { throw ('Unknown UI report name for ' + $source.BaseName + '. Register it in scripts/Test.ps1.') }
            }
            Run-HiddenCheck -Executable $runner -Arguments @($suiteOutput) -ReportPath (Join-Path $suiteOutput $reportName) -Suite $source.BaseName
        }
    }
    if ((Get-FileHash -LiteralPath $application -Algorithm SHA256).Hash -ne $summary.ApplicationSHA256) { throw 'Application changed while checks were running.' }
    $summary.Status = 'Passed'
    $summary.CompletedUtc = [DateTime]::UtcNow.ToString('o')
    $summary.Reports = @($reports.ToArray())
    Save-Summary
    Write-Output ('All ' + $reports.Count + ' suites passed. Reports: ' + $output)
    exit 0
}
catch {
    if ($null -ne $summary) {
        $summary.Status = 'Failed'
        $summary.CompletedUtc = [DateTime]::UtcNow.ToString('o')
        $summary.Reports = @($reports.ToArray())
        $summary.Error = $_.Exception.Message
        Save-Summary
    }
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
