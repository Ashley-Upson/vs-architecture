[CmdletBinding(SupportsShouldProcess)]
param()

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$allowedDirectories = @('current', 'baselines', 'investigations')

foreach ($directoryName in $allowedDirectories) {
    $target = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot $directoryName))
    if (-not $target.StartsWith($artifactRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear an artifact path outside $artifactRoot."
    }

    if ((Test-Path -LiteralPath $target) -and $PSCmdlet.ShouldProcess($target, 'Remove generated artifacts')) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
