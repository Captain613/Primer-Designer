param([string]$ApplicationPath, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ProjectConfig.ps1')
$config = Get-ProjectConfiguration
$application = Resolve-ProjectPath -Path $ApplicationPath -DefaultPath $config.ApplicationPath
$output = Resolve-ProjectPath -Path $OutputDirectory -DefaultPath $config.ReleaseDirectory
if (!(Test-Path -LiteralPath $application -PathType Leaf)) { throw 'Build the current application first.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null

# Run this script in its own Windows PowerShell process. LoadFrom keeps the EXE
# mapped until process exit, so Package.ps1 deliberately uses a child process.
[void][Reflection.Assembly]::LoadFrom($application)
function Write-Example([string]$Name, [string]$Content) {
    Write-ProjectUtf8 -Path (Join-Path $output $Name) -Content $Content
}
$token = [Threading.CancellationToken]::None
$rpa = [RpaDesigner.DesignEngine]::Design(
    [RpaDesigner.SequenceParser]::Parse([RpaDesigner.ReportWriter]::ExampleFasta()),
    (New-Object RpaDesigner.DesignSettings), $null, $token)
$rpaSnp = [RpaDesigner.SnpDesignEngine]::Design(
    [RpaDesigner.SnpParser]::Parse([RpaDesigner.SnpReportWriter]::ExampleFasta()),
    (New-Object RpaDesigner.SnpDesignSettings), $null, $token)
$rpaMismatchSettings = New-Object RpaDesigner.SnpDesignSettings
$rpaMismatchSettings.ExtraMismatchFromThreePrime = 3
$rpaMismatch = [RpaDesigner.SnpDesignEngine]::Design(
    [RpaDesigner.SnpParser]::Parse([RpaDesigner.SnpReportWriter]::ExampleFasta()),
    $rpaMismatchSettings, $null, $token)
if ($rpaMismatch.Sets.Count -eq 0) { throw 'RPA SNP mismatch demonstration produced no candidates.' }
$lampSettings = New-Object RpaDesigner.LampDesignSettings
$lamp = [RpaDesigner.LampDesignEngine]::Design(
    [RpaDesigner.SequenceParser]::Parse([RpaDesigner.LampReportWriter]::ExampleFasta()),
    $lampSettings, $null, $token)
$lampSnpInput = [RpaDesigner.SnpParser]::Parse([RpaDesigner.LampReportWriter]::ExampleSnpFasta())
$lampSnp = [RpaDesigner.LampDesignEngine]::DesignSnp($lampSnpInput, $lampSettings, $null, $token)
$mlamp = [RpaDesigner.LampDesignEngine]::DesignSnp($lampSnpInput, [RpaDesigner.LampDesignSettings]::MLampDefaults(), $null, $token)
if ($mlamp.Sets.Count -eq 0) { throw 'mLAMP demonstration produced no candidates.' }
$mismatchSettings = New-Object RpaDesigner.LampDesignSettings
$mismatchSettings.SnpOrientation = 'BIP'
$mismatchSettings.ExtraMismatchFromThreePrime = 3
$lampMismatch = [RpaDesigner.LampDesignEngine]::DesignSnp($lampSnpInput, $mismatchSettings, $null, $token)
$paSettings = New-Object RpaDesigner.LampDesignSettings
$paSettings.SnpMethod = 'PA-LAMP'
$paSettings.SnpOrientation = 'BIP'
$paLamp = [RpaDesigner.LampDesignEngine]::DesignSnp($lampSnpInput, $paSettings, $null, $token)
if ($paLamp.Sets.Count -eq 0) { throw 'PA-LAMP demonstration produced no candidates.' }
if ($rpa.Pairs.Count -eq 0 -or $rpaSnp.Sets.Count -eq 0 -or $lamp.Sets.Count -eq 0 -or $lampSnp.Sets.Count -eq 0 -or $lampMismatch.Sets.Count -eq 0) {
    throw 'At least one current demonstration produced no candidates.'
}
Write-Example 'example.fasta' ([RpaDesigner.ReportWriter]::ExampleFasta())
Write-Example 'demo-report.txt' ([RpaDesigner.ReportWriter]::TextReport($rpa))
Write-Example 'demo-results.csv' ([RpaDesigner.ReportWriter]::Csv($rpa))
Write-Example 'demo-primers.fasta' ([RpaDesigner.ReportWriter]::Fasta($rpa))
Write-Example 'snp-example.fasta' ([RpaDesigner.SnpReportWriter]::ExampleFasta())
Write-Example 'snp-demo-report.txt' ([RpaDesigner.SnpReportWriter]::TextReport($rpaSnp))
Write-Example 'snp-demo-report.html' ([RpaDesigner.SnpReportWriter]::Html($rpaSnp))
Write-Example 'snp-demo-results.csv' ([RpaDesigner.SnpReportWriter]::Csv($rpaSnp))
Write-Example 'snp-demo-primers.fasta' ([RpaDesigner.SnpReportWriter]::Fasta($rpaSnp))
Write-Example 'snp-mismatch3-demo-report.html' ([RpaDesigner.SnpReportWriter]::Html($rpaMismatch))
Write-Example 'lamp-example.fasta' ([RpaDesigner.LampReportWriter]::ExampleFasta())
Write-Example 'lamp-demo-report.txt' ([RpaDesigner.LampReportWriter]::TextReport($lamp))
Write-Example 'lamp-demo-report.html' ([RpaDesigner.LampReportWriter]::Html($lamp))
Write-Example 'lamp-demo-results.csv' ([RpaDesigner.LampReportWriter]::Csv($lamp))
Write-Example 'lamp-demo-primers.fasta' ([RpaDesigner.LampReportWriter]::Fasta($lamp))
Write-Example 'lamp-snp-example.fasta' ([RpaDesigner.LampReportWriter]::ExampleSnpFasta())
Write-Example 'lamp-snp-demo-report.txt' ([RpaDesigner.LampReportWriter]::TextReport($lampSnp))
Write-Example 'lamp-snp-demo-report.html' ([RpaDesigner.LampReportWriter]::Html($lampSnp))
Write-Example 'lamp-snp-demo-results.csv' ([RpaDesigner.LampReportWriter]::Csv($lampSnp))
Write-Example 'lamp-snp-demo-primers.fasta' ([RpaDesigner.LampReportWriter]::Fasta($lampSnp))
Write-Example 'lamp-snp-BIP-mismatch3-demo.html' ([RpaDesigner.LampReportWriter]::Html($lampMismatch))
Write-Example 'pa-lamp-snp-example.fasta' ([RpaDesigner.LampReportWriter]::ExampleSnpFasta())
Write-Example 'pa-lamp-demo-report.txt' ([RpaDesigner.LampReportWriter]::TextReport($paLamp))
Write-Example 'pa-lamp-demo-report.html' ([RpaDesigner.LampReportWriter]::Html($paLamp))
Write-Example 'pa-lamp-demo-results.csv' ([RpaDesigner.LampReportWriter]::Csv($paLamp))
Write-Example 'pa-lamp-demo-DNA-equivalent.fasta' ([RpaDesigner.LampReportWriter]::Fasta($paLamp))
Write-Example 'mlamp-snp-example.fasta' ([RpaDesigner.LampReportWriter]::ExampleSnpFasta())
Write-Example 'mlamp-demo-report.txt' ([RpaDesigner.LampReportWriter]::TextReport($mlamp))
Write-Example 'mlamp-demo-report.html' ([RpaDesigner.LampReportWriter]::Html($mlamp))
Write-Example 'mlamp-demo-results.csv' ([RpaDesigner.LampReportWriter]::Csv($mlamp))
Write-Example 'mlamp-demo-primers.fasta' ([RpaDesigner.LampReportWriter]::Fasta($mlamp))
Write-Output ('mLAMP FIP artificial-mismatch candidates: ' + $mlamp.Sets.Count)
Write-Output ('PA-LAMP modified BIP candidates: ' + $paLamp.Sets.Count)
Write-Output ('Generated current examples: RPA={0}; RPA SNP={1}; LAMP={2}; LAMP SNP={3}; BIP mismatch={4}' -f $rpa.Pairs.Count, $rpaSnp.Sets.Count, $lamp.Sets.Count, $lampSnp.Sets.Count, $lampMismatch.Sets.Count)
