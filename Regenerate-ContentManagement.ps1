<#
.SYNOPSIS
Rebuilds the v8 CLI and regenerates the ContentManagement architecture diagrams.
.EXAMPLE
& 'C:\Data\Github\Ashley Upson\vs-architecture\Regenerate-ContentManagement.ps1'
.EXAMPLE
.\Regenerate-ContentManagement.ps1 -View Combined
.EXAMPLE
.\Regenerate-ContentManagement.ps1 -View Both -MaxLayoutIterations 1000
.NOTES
Run with PowerShell 7 (pwsh). Outputs are written beside this script.
The default 25-pass limit bounds layout retries. Failed outputs retain their
previous files, other outputs are still attempted, and the script exits 1 on any failure.
Logs are retained in the temporary directory printed at the start of the run.
#>
[CmdletBinding()]
param(
    [string[]] $ProjectPaths = @('C:\Data\Github\cCoder\cCoder.ContentManagement\src\cCoder.ContentManagement\cCoder.ContentManagement.csproj'),
    [ValidateSet('Combined', 'Split', 'Both')]
    [string] $View = 'Both',
    [ValidateRange(1, 10000)]
    [int] $MaxLayoutIterations = 25,
    [string] $ConfigPath = (Join-Path $PSScriptRoot 'v8/Architecture.InlineExternals.json'),
    [string] $BuildConfiguration = 'Debug',
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
# Native failures are collected below so one failed diagram does not skip others.
$PSNativeCommandUseErrorActionPreference = $false
$runDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('architecture-regeneration-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
Write-Host "Logs: $runDirectory"

try {
    $resolvedProjects = @($ProjectPaths | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
    $resolvedConfig = (Resolve-Path -LiteralPath $ConfigPath).Path
    $cliProject = Join-Path $PSScriptRoot 'v8/DiagramCLI/DiagramCLI.csproj'
    $cliAssembly = Join-Path $PSScriptRoot "v8/DiagramCLI/bin/$BuildConfiguration/net10.0/DiagramCLI.dll"

    if (-not $SkipBuild) {
        $buildLog = Join-Path $runDirectory 'build.log'
        Write-Host 'Building DiagramCLI...'
        & dotnet build $cliProject --configuration $BuildConfiguration --verbosity quiet *> $buildLog
        if ($LASTEXITCODE -ne 0) {
            Get-Content -LiteralPath $buildLog -Tail 30 | Write-Host
            throw "CLI build failed. See $buildLog"
        }
    }

    $views = if ($View -eq 'Both') { @('Combined', 'Split') } else { @($View) }
    $failures = 0
    foreach ($diagramView in $views) {
        $stem = if ($diagramView -eq 'Combined') { 'ContentManagement' } else { 'ContentManagement.WithDuplicates' }
        $generationLog = Join-Path $runDirectory "$stem.log"
        $commands = @()

        foreach ($format in @('Html', 'DrawIO')) {
            $extension = if ($format -eq 'Html') { 'html' } else { 'drawio' }
            $stagedOutput = Join-Path $runDirectory "$stem.$extension"
            $command = @('All') + $resolvedProjects + @(
                '--output', $stagedOutput, '--format', $format,
                '--config', $resolvedConfig, '--max-layout-iterations', "$MaxLayoutIterations")

            if ($diagramView -eq 'Combined') { $command += '--noduplicates' }
            if ($commands.Count -gt 0) { $commands += '--next' }
            $commands += $command
        }

        Write-Host "Generating $stem HTML and DrawIO from one project model..."
        & dotnet $cliAssembly @commands *> $generationLog
        if ($LASTEXITCODE -ne 0) {
            $failures += 2
            Write-Warning "$stem outputs failed; their existing files were preserved."
            Get-Content -LiteralPath $generationLog -Tail 10 | Write-Host
            continue
        }

        foreach ($extension in @('html', 'drawio')) {
            $filename = "$stem.$extension"
            $stagedOutput = Join-Path $runDirectory $filename
            $destination = Join-Path $PSScriptRoot $filename

            if (-not (Test-Path -LiteralPath $stagedOutput)) {
                $failures++
                Write-Warning "$filename was not produced; its existing file was preserved."
                continue
            }

            Move-Item -LiteralPath $stagedOutput -Destination $destination -Force
            Write-Host "Updated $destination"
        }
    }

    if ($failures -gt 0) {
        Write-Warning "$failures diagram(s) failed. Logs: $runDirectory"
        exit 1
    }
    Write-Host 'All requested diagrams regenerated. Refresh any open HTML tabs.'
    exit 0
}
catch {
    Write-Error -Message $_ -ErrorAction Continue
    exit 1
}
