param(
    [Parameter(Mandatory = $true)]
    [string[]]$DiagnosticPaths,
    [string]$OutputPath = 'docs\evidence\routing-consolidation-evidence.csv'
)

$ErrorActionPreference = 'Stop'

function New-DisjointSet([string[]]$ids) {
    $parent = @{}
    foreach ($id in $ids) { $parent[$id] = $id }
    return $parent
}

function Find-Root($parent, [string]$id) {
    $cursor = $id
    while ($parent[$cursor] -ne $cursor) { $cursor = $parent[$cursor] }
    $root = $cursor
    $cursor = $id
    while ($parent[$cursor] -ne $cursor) {
        $next = $parent[$cursor]
        $parent[$cursor] = $root
        $cursor = $next
    }
    return $root
}

function Join-Items($parent, [string]$left, [string]$right) {
    if (-not $parent.ContainsKey($left) -or -not $parent.ContainsKey($right)) { return }
    $leftRoot = Find-Root $parent $left
    $rightRoot = Find-Root $parent $right
    if ($leftRoot -eq $rightRoot) { return }
    if ([string]::CompareOrdinal($leftRoot, $rightRoot) -lt 0) { $parent[$rightRoot] = $leftRoot }
    else { $parent[$leftRoot] = $rightRoot }
}

function Copy-DisjointSet($source) {
    $copy = @{}
    foreach ($key in $source.Keys) { $copy[$key] = $source[$key] }
    return $copy
}

function Get-Components($parent, $routeById, $bandByRoute) {
    $groups = @{}
    foreach ($id in $routeById.Keys) {
        $root = Find-Root $parent $id
        if (-not $groups.ContainsKey($root)) { $groups[$root] = [System.Collections.Generic.List[string]]::new() }
        $groups[$root].Add($id)
    }
    $details = foreach ($group in $groups.Values) {
        $nodes = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $projects = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $bands = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($id in $group) {
            $route = $routeById[$id]
            [void]$nodes.Add($route.SourceId); [void]$nodes.Add($route.TargetId)
            if ($route.SourceProjectId) { [void]$projects.Add($route.SourceProjectId) }
            if ($route.TargetProjectId) { [void]$projects.Add($route.TargetProjectId) }
            foreach ($band in $bandByRoute[$id]) { [void]$bands.Add($band) }
        }
        [pscustomobject]@{ Routes = $group.Count; Nodes = $nodes.Count; Bands = $bands.Count; Projects = $projects.Count }
    }
    return @($details | Sort-Object Routes, Nodes -Descending)
}

function Get-Count($findings, [string]$code) {
    return @($findings | Where-Object { $_.validatorCode -eq $code }).Count
}

$rows = foreach ($diagnosticPath in $DiagnosticPaths) {
    $resolved = (Resolve-Path $diagnosticPath).Path
    $report = Get-Content $resolved -Raw | ConvertFrom-Json
    $focusedById = @{}; foreach ($route in @($report.routes)) { $focusedById[$route.logicalRouteId] = $route }
    $routes = foreach ($geometry in @($report.routeGeometry)) {
        $focused = $focusedById[$geometry.logicalRouteId]
        [pscustomobject]@{
            LogicalRouteId = $geometry.logicalRouteId
            SourceId = $geometry.sourceId
            TargetId = $geometry.targetId
            SourceProjectId = if ($focused) { $focused.sourceNode.projectId } else { $null }
            TargetProjectId = if ($focused) { $focused.targetNode.projectId } else { $null }
        }
    }
    $routeIds = @($routes | ForEach-Object LogicalRouteId | Sort-Object -Unique)
    $routeById = @{}; foreach ($route in $routes) { $routeById[$route.LogicalRouteId] = $route }
    $direct = New-DisjointSet $routeIds
    $bandByRoute = @{}; foreach ($id in $routeIds) { $bandByRoute[$id] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }

    # Semantic terminal sharing is a direct interaction relation.
    $terminalGroups = @{}
    foreach ($route in $routes) {
        foreach ($terminal in @($route.SourceId, $route.TargetId)) {
            if (-not $terminalGroups.ContainsKey($terminal)) { $terminalGroups[$terminal] = [System.Collections.Generic.List[string]]::new() }
            $terminalGroups[$terminal].Add($route.LogicalRouteId)
        }
    }
    foreach ($group in $terminalGroups.Values) {
        for ($index = 1; $index -lt $group.Count; $index++) { Join-Items $direct $group[0] $group[$index] }
    }

    $findings = @($report.repair.postRepairFindings)
    foreach ($finding in $findings) {
        if ($finding.otherRouteId) { Join-Items $direct $finding.logicalRouteId $finding.otherRouteId }
        if ($finding.otherNodeId -and $terminalGroups.ContainsKey($finding.otherNodeId)) {
            foreach ($incident in $terminalGroups[$finding.otherNodeId]) { Join-Items $direct $finding.logicalRouteId $incident }
        }
    }

    # A band conflict group is the interval-overlap connected component of demands in that band.
    $intervalComparisons = 0
    foreach ($band in @($report.interLayerBands.bands)) {
        $bandId = [string]$band.id
        foreach ($membership in @($band.memberships)) {
            if ($bandByRoute.ContainsKey($membership.logicalEdgeIdentity)) { [void]$bandByRoute[$membership.logicalEdgeIdentity].Add($bandId) }
        }
        $demands = @($band.demands | Sort-Object xStart, xEnd, logicalEdgeIdentity)
        for ($leftIndex = 0; $leftIndex -lt $demands.Count; $leftIndex++) {
            $left = $demands[$leftIndex]
            for ($rightIndex = $leftIndex + 1; $rightIndex -lt $demands.Count; $rightIndex++) {
                $right = $demands[$rightIndex]
                if ([double]$right.xStart -gt [double]$left.xEnd) { break }
                $intervalComparisons++
                if ([double]$right.xEnd -ge [double]$left.xStart) {
                    Join-Items $direct $left.logicalEdgeIdentity $right.logicalEdgeIdentity
                }
            }
        }
    }

    $movement = Copy-DisjointSet $direct
    # A deficient band moves its lower layer and all deeper nodes. Close over every route
    # incident to that movement scope and every route already interacting with the band.
    foreach ($band in @($report.interLayerBands.bands | Where-Object { $_.missingExtent -gt 0 })) {
        $affected = [System.Collections.Generic.List[string]]::new()
        $affectedBands = @($report.interLayerBands.bands | Where-Object {
            [int]$_.upperLayer -ge [int]$band.lowerLayer -or [int]$_.lowerLayer -ge [int]$band.lowerLayer
        } | ForEach-Object { [string]$_.id })
        foreach ($route in $routes) {
            $inBand = $bandByRoute[$route.logicalRouteId].Contains([string]$band.id)
            $touchesMovedLayer = @($bandByRoute[$route.LogicalRouteId] | Where-Object { $affectedBands -contains $_ }).Count -gt 0
            if ($inBand -or $touchesMovedLayer) { $affected.Add($route.LogicalRouteId) }
        }
        for ($index = 1; $index -lt $affected.Count; $index++) { Join-Items $movement $affected[0] $affected[$index] }
    }

    $directComponents = @(Get-Components $direct $routeById $bandByRoute)
    $movementComponents = @(Get-Components $movement $routeById $bandByRoute)
    $telemetry = $report.interLayerBands.telemetry
    $drawioPath = [IO.Path]::ChangeExtension($resolved, '.drawio')
    $projects = @()
    if (Test-Path $drawioPath) {
        $projectMatches = [regex]::Matches((Get-Content $drawioPath -Raw), 'id=&quot;(project_[^&]+)&quot;|id="(project_[^"]+)"')
        $projects = @($projectMatches | ForEach-Object {
            if ($_.Groups[1].Success) { $_.Groups[1].Value } else { $_.Groups[2].Value }
        } | Sort-Object -Unique)
    }
    $largestProjects = if ($movementComponents.Count -and
        $movementComponents[0].Routes -eq $routeIds.Count -and $projects.Count -eq 1) {
        1
    } elseif ($movementComponents.Count) {
        $movementComponents[0].Projects
    } else { 0 }
    $candidateCrossings = Get-Count $findings 'PerpendicularCrossing'
    $hardDemandCount = (Get-Count $findings 'NodeCollision') + (Get-Count $findings 'SharedSegment') +
        (Get-Count $findings 'ReusedBend') + (Get-Count $findings 'ImmediateReversal') +
        [int]$telemetry.unsupportedShapeCount
    [pscustomobject]@{
        Name = [IO.Path]::GetFileNameWithoutExtension($resolved)
        Diagnostic = $diagnosticPath.Replace('/', '\')
        Nodes = [int]$telemetry.nodeCount
        Routes = [int]$telemetry.routeCount
        Segments = [int]$telemetry.segmentCount
        Bands = [int]$telemetry.bandCount
        Projects = $projects.Count
        LegacyInteractionRegions = 'not exposed'
        GroupedConflictGroups = (@($report.interLayerBands.bands | Measure-Object overlapGroupCount -Sum).Sum)
        DirectComponents = $directComponents.Count
        DirectLargestRoutes = if ($directComponents.Count) { $directComponents[0].Routes } else { 0 }
        MovementClosedComponents = $movementComponents.Count
        LargestRoutes = if ($movementComponents.Count) { $movementComponents[0].Routes } else { 0 }
        LargestNodes = if ($movementComponents.Count) { $movementComponents[0].Nodes } else { 0 }
        LargestBands = if ($movementComponents.Count) { $movementComponents[0].Bands } else { 0 }
        LargestProjects = $largestProjects
        HardDefectRailDemands = $hardDemandCount
        MinimumXOpportunities = (Get-Count $findings 'ParallelSpacing')
        MinimumYOpportunities = @($report.interLayerBands.bands | Where-Object { $_.missingExtent -gt 0 }).Count
        CrossoverAdvisories = $candidateCrossings
        AmbiguousBends = (Get-Count $findings 'ReusedBend')
        UnsupportedShapes = [int]$telemetry.unsupportedShapeCount
        IntervalComparisons = $intervalComparisons
        CandidateParallelRegions = $movementComponents.Count
    }
}

$fullOutput = [IO.Path]::GetFullPath($OutputPath)
$directory = [IO.Path]::GetDirectoryName($fullOutput)
if ($directory) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
$rows | Export-Csv $fullOutput -NoTypeInformation
$rows | Format-Table -AutoSize
Write-Output "Evidence: $fullOutput"
