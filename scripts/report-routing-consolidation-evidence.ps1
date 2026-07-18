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
            SourceAxis = if (@($geometry.points).Count) { [int]$geometry.points[0].x } else { 0 }
            TargetAxis = if (@($geometry.points).Count) { [int]$geometry.points[-1].x } else { 0 }
        }
    }
    $routeIds = @($routes | ForEach-Object LogicalRouteId | Sort-Object -Unique)
    $routeById = @{}; foreach ($route in $routes) { $routeById[$route.LogicalRouteId] = $route }
    $base = New-DisjointSet $routeIds
    $mergeCauses = @{}
    $bandByRoute = @{}; foreach ($id in $routeIds) { $bandByRoute[$id] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }

    # Semantic endpoint identity is an index, not an interaction edge.
    $terminalGroups = @{}
    foreach ($route in $routes) {
        foreach ($terminal in @($route.SourceId, $route.TargetId)) {
            if (-not $terminalGroups.ContainsKey($terminal)) { $terminalGroups[$terminal] = [System.Collections.Generic.List[string]]::new() }
            $terminalGroups[$terminal].Add($route.LogicalRouteId)
        }
    }
    $findings = @($report.repair.postRepairFindings)
    foreach ($finding in $findings) {
        if ($finding.otherRouteId) {
            Join-Items $base $finding.logicalRouteId $finding.otherRouteId
            $cause = "Contact:$($finding.validatorCode)"
            $mergeCauses[$cause] = 1 + [int]($mergeCauses[$cause])
        }
        if ($finding.otherNodeId -and $terminalGroups.ContainsKey($finding.otherNodeId)) {
            foreach ($incident in $terminalGroups[$finding.otherNodeId]) {
                Join-Items $base $finding.logicalRouteId $incident
                $mergeCauses['ObstacleBypass'] = 1 + [int]($mergeCauses['ObstacleBypass'])
            }
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
                    Join-Items $base $left.logicalEdgeIdentity $right.logicalEdgeIdentity
                    $mergeCauses['RailIntervalConflict'] = 1 + [int]($mergeCauses['RailIntervalConflict'])
                }
            }
        }
    }

    $unresolved = Copy-DisjointSet $base
    $resolvedTerminals = Copy-DisjointSet $base
    $unresolvedTerminalEdges = 0
    foreach ($group in @($routes | ForEach-Object {
        [pscustomobject]@{ Key = "$($_.SourceId):OutgoingBottom:bottom"; Route = $_.LogicalRouteId }
        [pscustomobject]@{ Key = "$($_.TargetId):IncomingTop:top"; Route = $_.LogicalRouteId }
    } | Group-Object Key)) {
        $members = @($group.Group.Route | Sort-Object -Unique)
        for ($index = 1; $index -lt $members.Count; $index++) {
            Join-Items $unresolved $members[0] $members[$index]
            $unresolvedTerminalEdges++
        }
    }
    $resolvedTerminalEdges = 0
    foreach ($group in @($routes | ForEach-Object {
        [pscustomobject]@{ Key = "$($_.SourceId):OutgoingBottom:bottom:$($_.SourceAxis)"; Route = $_.LogicalRouteId }
        [pscustomobject]@{ Key = "$($_.TargetId):IncomingTop:top:$($_.TargetAxis)"; Route = $_.LogicalRouteId }
    } | Group-Object Key)) {
        $members = @($group.Group.Route | Sort-Object -Unique)
        for ($index = 1; $index -lt $members.Count; $index++) {
            Join-Items $resolvedTerminals $members[0] $members[$index]
            $resolvedTerminalEdges++
        }
    }

    $unresolvedMovement = Copy-DisjointSet $unresolved
    $movement = Copy-DisjointSet $resolvedTerminals
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
        for ($index = 1; $index -lt $affected.Count; $index++) {
            Join-Items $unresolvedMovement $affected[0] $affected[$index]
            Join-Items $movement $affected[0] $affected[$index]
        }
    }

    $unresolvedComponents = @(Get-Components $unresolvedMovement $routeById $bandByRoute)
    $resolvedComponents = @(Get-Components $movement $routeById $bandByRoute)
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
        TerminalUnresolvedComponents = $unresolvedComponents.Count
        TerminalUnresolvedLargestRoutes = if ($unresolvedComponents.Count) { $unresolvedComponents[0].Routes } else { 0 }
        TerminalUnresolvedMedianRoutes = if ($unresolvedComponents.Count) {
            $sizes = @($unresolvedComponents.Routes | Sort-Object); $sizes[[int][Math]::Floor(($sizes.Count - 1) / 2)]
        } else { 0 }
        TerminalUnresolvedSingletons = @($unresolvedComponents | Where-Object Routes -eq 1).Count
        TerminalResolvedComponents = $resolvedComponents.Count
        TerminalResolvedLargestRoutes = if ($resolvedComponents.Count) { $resolvedComponents[0].Routes } else { 0 }
        TerminalResolvedMedianRoutes = if ($resolvedComponents.Count) {
            $sizes = @($resolvedComponents.Routes | Sort-Object); $sizes[[int][Math]::Floor(($sizes.Count - 1) / 2)]
        } else { 0 }
        TerminalResolvedSingletons = @($resolvedComponents | Where-Object Routes -eq 1).Count
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
        UnresolvedTerminalCompetitionEdges = $unresolvedTerminalEdges
        ResolvedTerminalContactEdges = $resolvedTerminalEdges
        TopMergeCauses = (@($mergeCauses.GetEnumerator() | Sort-Object -Property `
            @{ Expression = 'Value'; Descending = $true }, @{ Expression = 'Name'; Ascending = $true } |
            Select-Object -First 5 | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ';')
        CanonicalCleanCrossovers = [int]$report.consolidatedFoundation.contacts.counts.CleanPerpendicularCrossover
        CanonicalBendInvolvedContacts = [int]$report.consolidatedFoundation.contacts.counts.BendInvolvedPerpendicularContact
        NodeTerminalDemandMicroseconds = [long]$report.consolidatedFoundation.timings.nodeAndTerminalDemandMicroseconds
        CanonicalContactMicroseconds = [long]$report.consolidatedFoundation.timings.canonicalContactClassificationMicroseconds
        ConstraintMaterializationMicroseconds = [long]$report.consolidatedFoundation.timings.constraintMergeAndMaterializationMicroseconds
        InvalidationMicroseconds = [long]$report.consolidatedFoundation.timings.invalidationMicroseconds
        TerminalComponentMicroseconds = [long]$report.consolidatedFoundation.timings.terminalComponentConstructionMicroseconds
        NodesChangedFromPreviousFormula = [int]$report.consolidatedFoundation.nodeWidths.changedFromPreviousFormula
        MaximumNodeWidthChange = [int]$report.consolidatedFoundation.nodeWidths.maximumAbsoluteChangeFromPreviousFormula
        ObservedIncidentInvalidations = [int]$report.consolidatedFoundation.nodeWidths.observationalIncidentRouteInvalidations
    }
}

$fullOutput = [IO.Path]::GetFullPath($OutputPath)
$directory = [IO.Path]::GetDirectoryName($fullOutput)
if ($directory) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
$rows | Export-Csv $fullOutput -NoTypeInformation
$rows | Format-Table -AutoSize
Write-Output "Evidence: $fullOutput"
