param(
    [Parameter(Mandatory = $true)][string]$ReportPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$report = Get-Content -Raw -LiteralPath $ReportPath | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$details = @{}
foreach ($item in $report.routeCapabilityDetails) { $details[$item.LogicalRouteId] = $item }

function Convert-Link([string]$id, [string]$family, [string]$invalidation, [bool]$geometryDemand) {
    $item = $details[$id]
    [pscustomobject][ordered]@{
        logicalLinkId = $id
        semanticFamily = $family
        commonSupported = [bool]$item.Eligible
        topologyOwner = if ($item.Eligible) { if ($item.Reason -eq 'CommonReturnTopology') { 'CommonReturn' } else { 'CommonDownward' } } else { 'Legacy' }
        connectionTopology = $item.connectionTopology
        projectRelationship = $item.projectRelationship
        sourceProjectId = $item.sourceProjectId
        targetProjectId = $item.targetProjectId
        sourcePositionalSubtree = $item.sourcePositionalRoot
        targetPositionalSubtree = $item.targetPositionalRoot
        invalidationReason = $invalidation
        primaryUnsupportedReason = if ($item.Eligible) { $null } else { $item.Reason }
        secondaryReasons = @($item.generalRejection) + @($item.generalDiagnostics) + @($item.assignmentDiagnostics) | Where-Object { $_ }
        contributesGeometryDemand = $geometryDemand
        closureOnly = -not $geometryDemand
        legacyValid = @($item.legacyHardFindings).Count -eq 0
        legacyHardFindings = @($item.legacyHardFindings)
        renderSourceId = $item.renderSourceId
        renderTargetId = $item.renderTargetId
        semanticSourceId = $item.semanticSourceId
        semanticTargetId = $item.semanticTargetId
    }
}

function Write-Boundary([string]$slug, [string]$title, $links, $metadata) {
    $ordered = @($links | Sort-Object logicalLinkId)
    $payload = [ordered]@{ boundary = $title; metadata = $metadata; links = $ordered }
    $payload | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 -LiteralPath (Join-Path $OutputDirectory "$slug.json")
    $reasonCounts = $ordered | Where-Object { -not $_.commonSupported } | Group-Object primaryUnsupportedReason | Sort-Object Name
    $familyCounts = $ordered | Group-Object semanticFamily | Sort-Object Name
    $lines = @("# $title", '', "Closure links: $($ordered.Count)", "Common-supported: $(@($ordered | Where-Object commonSupported).Count)", "Unsupported: $(@($ordered | Where-Object { -not $_.commonSupported }).Count)", '', '## Links by family', '')
    $lines += $familyCounts | ForEach-Object { "- $($_.Name): $($_.Count)" }
    $lines += @('', '## Unsupported reasons', '')
    $lines += $reasonCounts | ForEach-Object { "- $($_.Name): $($_.Count)" }
    $lines += @('', '## Link attribution', '', '| Link | Family | Owner | Supported | Primary reason | Geometry demand | Legacy findings |', '|---|---|---|---:|---|---:|---|')
    $lines += $ordered | ForEach-Object { "| $($_.logicalLinkId) | $($_.semanticFamily) | $($_.topologyOwner) | $($_.commonSupported) | $($_.primaryUnsupportedReason) | $($_.contributesGeometryDemand) | $(@($_.legacyHardFindings) -join ', ') |" }
    $lines | Set-Content -Encoding utf8 -LiteralPath (Join-Path $OutputDirectory "$slug.md")
}

foreach ($band in $report.mixedBoundaryAttribution.DeficientBands) {
    $slug = if ($band.BandId -match ':0:1:') { 'inter-layer-0-1' } elseif ($band.BandId -match ':4:5:') { 'inter-layer-4-5' } else { continue }
    $links = foreach ($familyProperty in $band.RoutesByFamily.PSObject.Properties) {
        foreach ($id in $familyProperty.Value) { Convert-Link $id $familyProperty.Name 'InterLayerExtentChanged' $true }
    }
    Write-Boundary $slug $band.BandId $links ([ordered]@{ availableExtent = $band.AvailableExtent; requiredExtent = $band.RequiredExtent; missingExtent = $band.MissingExtent })
}

$movement = @($report.movementClosures)[0]
$depthLinks = foreach ($id in $movement.invalidatedRouteIds) {
    $family = if ($details[$id].Reason -eq 'CommonReturnTopology') { 'Return' } elseif ($details[$id].Reason -eq 'CommonDownwardTopology') { 'Downward' } else { $details[$id].generalRejection }
    Convert-Link $id $family 'LayerAndLowerSuffixMoved' $false
}
Write-Boundary 'depth-2-movement' 'Depth-2 60px movement' $depthLinks ([ordered]@{ proposedMinimum = $movement.proposedMinimum; maximumDelta = $movement.MaximumDelta; fullySupported = $movement.fullySupported; disposition = $movement.disposition })

$all = @($depthLinks)
$summary = @('# Remaining authority boundaries', '', '## Closure summary', '', "Depth-2 closure links: $($all.Count)", "Common-supported: $(@($all | Where-Object commonSupported).Count)", "Unsupported: $(@($all | Where-Object { -not $_.commonSupported }).Count)", '', '## Unsupported links by exact reason', '')
$summary += $all | Where-Object { -not $_.commonSupported } | Group-Object primaryUnsupportedReason | Sort-Object Name | ForEach-Object { "- $($_.Name): $($_.Count)" }
$summary += @('', '## Unlock ranking', '', '| Rank | Capability | Unsupported links covered | Closed components unlocked | Real proposals unlocked |', '|---:|---|---:|---:|---:|', '| 1 | Coherent obstacle/subtree movement for return stubs | 20 | 1 jointly | 1 jointly |', '| 2 | Destination-column conflict movement and complete InterLayer observation | 19 | 1 jointly | 1 jointly |', '', 'Both capabilities are jointly required because every unsupported link belongs to the same movement and interaction closure. Solving either alone unlocks no real proposal.')
$summary | Set-Content -Encoding utf8 -LiteralPath (Join-Path $OutputDirectory 'summary.md')
