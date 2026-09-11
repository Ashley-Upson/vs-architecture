param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('normal', 'diagnostic', 'strict')]
    [string]$Mode,

    [Parameter(Mandatory = $true)]
    [string]$InputPath,

    [Parameter(Mandatory = $true)]
    [string]$SettingsPath,

    [Parameter(Mandatory = $true)]
    [string]$Name,

    [int]$WarmupRuns = 1,

    [int]$MeasuredRuns = 5,

    [string]$ArtifactRoot = 'artifacts\performance-audit\benchmarks'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$cliPath = Join-Path $repositoryRoot 'src\StandardIo.ArchitectureDiagram.Cli\bin\Release\net8.0\StandardIo.ArchitectureDiagram.Cli.dll'
function Resolve-BenchmarkPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

$resolvedInput = Resolve-BenchmarkPath $InputPath
$resolvedSettings = Resolve-BenchmarkPath $SettingsPath
$resolvedArtifacts = Resolve-BenchmarkPath $ArtifactRoot
$modeDirectory = Join-Path $resolvedArtifacts "$Name-$Mode"
[System.IO.Directory]::CreateDirectory($modeDirectory) | Out-Null

if (-not [System.IO.File]::Exists($cliPath)) {
    throw "Release CLI not found: $cliPath"
}

$results = [System.Collections.Generic.List[object]]::new()
$totalRuns = $WarmupRuns + $MeasuredRuns
for ($index = 0; $index -lt $totalRuns; $index++) {
    $isWarmup = $index -lt $WarmupRuns
    $kind = if ($isWarmup) { 'warmup' } else { 'measured' }
    $number = if ($isWarmup) { $index + 1 } else { $index - $WarmupRuns + 1 }
    $stem = "$kind-$number"
    $outputPath = Join-Path $modeDirectory "$stem.drawio"
    $arguments = @(
        $cliPath,
        $resolvedInput,
        '--settings', $resolvedSettings,
        '--renderer', 'drawio',
        '--output', $outputPath
    )

    if ($Mode -eq 'diagnostic') {
        $arguments += @('--diagnostics-output', (Join-Path $modeDirectory "$stem.validation.json"))
    }
    elseif ($Mode -eq 'strict') {
        $arguments += '--strict-validation'
    }

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $ErrorActionPreference = 'Continue'
    & dotnet @arguments *> (Join-Path $modeDirectory "$stem.log")
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $timer.Stop()

    if ($Mode -eq 'normal' -and $exitCode -ne 0) {
        throw "Normal benchmark run failed with exit code $exitCode."
    }
    if ($Mode -ne 'normal' -and $exitCode -notin @(0, 1)) {
        throw "$Mode benchmark run failed with exit code $exitCode."
    }

    $results.Add([pscustomobject]@{
        Name = $Name
        Mode = $Mode
        Kind = $kind
        Run = $number
        ElapsedMilliseconds = $timer.ElapsedMilliseconds
        ExitCode = $exitCode
        Sha256 = (Get-FileHash $outputPath -Algorithm SHA256).Hash
        OutputBytes = (Get-Item $outputPath).Length
        ProcessModel = 'new process per run'
        OutputDestination = 'local NTFS'
    })
}

$csvPath = Join-Path $modeDirectory 'results.csv'
$results | Export-Csv -Path $csvPath -NoTypeInformation
$measured = @($results | Where-Object Kind -eq 'measured' | Sort-Object ElapsedMilliseconds)
$median = if ($measured.Count % 2 -eq 1) {
    $measured[[int][Math]::Floor($measured.Count / 2)].ElapsedMilliseconds
}
else {
    ($measured[$measured.Count / 2 - 1].ElapsedMilliseconds + $measured[$measured.Count / 2].ElapsedMilliseconds) / 2
}

[pscustomobject]@{
    Name = $Name
    Mode = $Mode
    Runs = $measured.Count
    MinimumMilliseconds = ($measured | Measure-Object ElapsedMilliseconds -Minimum).Minimum
    MedianMilliseconds = $median
    MaximumMilliseconds = ($measured | Measure-Object ElapsedMilliseconds -Maximum).Maximum
    RepeatHashCount = @($measured.Sha256 | Sort-Object -Unique).Count
    ResultsPath = $csvPath
} | ConvertTo-Json
