param(
    [string]$DiagnosticPath = 'artifacts\stage-b\ccoder-deduplicated.json',
    [string]$OutputPath = 'docs\evidence\stage-b-non-orthogonal-segments.json'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
function Resolve-ReportPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

$diagnostic = Get-Content (Resolve-ReportPath $DiagnosticPath) -Raw | ConvertFrom-Json
$segments = @($diagnostic.nonOrthogonalSegments |
    Where-Object { $_.bandMemberships.Count -gt 0 } |
    Sort-Object logicalEdgeId, segmentIndex |
    ForEach-Object {
        [pscustomobject][ordered]@{
            logicalEdge = $_.logicalEdgeId
            source = [ordered]@{ id = $_.sourceId; name = $_.sourceName }
            target = [ordered]@{ id = $_.targetId; name = $_.targetName }
            routeRevision = $_.routeRevision
            segmentIndex = $_.segmentIndex
            start = $_.start
            end = $_.end
            deltaX = $_.deltaX
            deltaY = $_.deltaY
            bandMemberships = $_.bandMemberships
            routeProducer = $_.routeProducer
            routeStage = $_.routeStage
            traversalFallback = $_.traversalFallback
            traversalDiagnostics = $_.traversalDiagnostics
            terminalRegion = $_.terminalRegion
            ownershipBoundary = $_.ownershipBoundary
            associatedValidationFindings = $_.associatedFindings
            routeHistory = $_.routeHistory
            classification = $_.classification
            laneDemandTreatment = 'No lane demand until invalid fallback geometry is corrected.'
            completeLogicalPoints = $_.completeLogicalPoints
            serializedPhysicalSegments = $_.physicalSegments
            reconstructedAbsoluteXmlPoints = $_.reconstructedAbsoluteXmlPoints
            finalXmlContainsDiagonal = $_.xmlContainsDiagonal
            diagramsNetDisplaysDiagonal = $_.xmlContainsDiagonal
            manualContainerMoveEffect = 'The owning physical segment translates with its owner; the diagonal shape remains unless a boundary transition stretches.'
            stageCRegenerationSafe = $false
            requiresCorrectionBeforeExpansion = $true
        }
    })

if ($segments.Count -ne 20) {
    throw "Expected 20 band-crossing non-orthogonal segments, found $($segments.Count)."
}

$result = [ordered]@{
    sourceDiagnostic = Resolve-ReportPath $DiagnosticPath
    segmentCount = $segments.Count
    classificationCounts = @($segments | Group-Object classification | Sort-Object Name | ForEach-Object {
        [ordered]@{ classification = $_.Name; count = $_.Count }
    })
    segments = $segments
}

$resolvedOutput = Resolve-ReportPath $OutputPath
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$result | ConvertTo-Json -Depth 20 | Set-Content $resolvedOutput -Encoding UTF8
Write-Output $resolvedOutput
