[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DrawioPath,

    [string] $JsonOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Number([System.Xml.XmlElement] $Element, [string] $Name) {
    $value = $Element.GetAttribute($Name)
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    return [double]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
}

function New-Point([double] $X, [double] $Y) {
    return [pscustomobject] @{ X = $X; Y = $Y }
}

function Test-Between([double] $Value, [double] $Lower, [double] $Upper) {
    return $Value -ge ($Lower - 0.001) -and $Value -le ($Upper + 0.001)
}

function Test-InteriorIntersection($A, $B, $Node) {
    if ($A.X -eq $B.X) {
        if (-not (Test-Between $A.X $Node.Left $Node.Right)) { return $false }
        $low = [math]::Max([math]::Min($A.Y, $B.Y), $Node.Top)
        $high = [math]::Min([math]::Max($A.Y, $B.Y), $Node.Bottom)
        return $high -gt ($low + 0.001) -and $A.X -gt ($Node.Left + 0.001) -and $A.X -lt ($Node.Right - 0.001)
    }
    if ($A.Y -eq $B.Y) {
        if (-not (Test-Between $A.Y $Node.Top $Node.Bottom)) { return $false }
        $low = [math]::Max([math]::Min($A.X, $B.X), $Node.Left)
        $high = [math]::Min([math]::Max($A.X, $B.X), $Node.Right)
        return $high -gt ($low + 0.001) -and $A.Y -gt ($Node.Top + 0.001) -and $A.Y -lt ($Node.Bottom - 0.001)
    }
    return $true
}

$xml = [xml](Get-Content -LiteralPath $DrawioPath -Raw)
$cells = @($xml.mxfile.diagram.mxGraphModel.root.mxCell)
$vertices = @{}
$edges = @()

foreach ($cell in $cells) {
    if ($cell.GetAttribute('vertex') -eq '1') {
        # Project containers are vertices in Draw.io but are not architecture
        # nodes and must not participate in endpoint or route-through-node
        # checks.
        if (-not ([string]$cell.id).StartsWith('v7_node_', [StringComparison]::Ordinal)) { continue }
        $geometry = $cell.mxGeometry
        if ($null -eq $geometry) { continue }
        $vertices[$cell.id] = [pscustomobject]@{
            Id = [string]$cell.id
            Value = [string]$cell.value
            Left = Get-Number $geometry 'x'
            Top = Get-Number $geometry 'y'
            Width = Get-Number $geometry 'width'
            Height = Get-Number $geometry 'height'
            Right = (Get-Number $geometry 'x') + (Get-Number $geometry 'width')
            Bottom = (Get-Number $geometry 'y') + (Get-Number $geometry 'height')
        }
    }
    if ($cell.GetAttribute('edge') -eq '1') { $edges += $cell }
}

$routeRecords = @()
$orthogonalityFindings = @()
$nodeCrossings = @()
$endpointFindings = @()
$segmentRecords = @()
$seenPhysical = @{}

foreach ($edge in $edges) {
    $physical = [string]$edge.physicalLinkId
    if ([string]::IsNullOrWhiteSpace($physical)) { $physical = [string]$edge.id }
    if ($seenPhysical.ContainsKey($physical)) { $seenPhysical[$physical]++ } else { $seenPhysical[$physical] = 1 }

    $source = $vertices[[string]$edge.source]
    $target = $vertices[[string]$edge.target]
    $sx = Get-Number $edge 'v7SourceTerminalX'
    $sy = Get-Number $edge 'v7SourceTerminalY'
    $tx = Get-Number $edge 'v7TargetTerminalX'
    $ty = Get-Number $edge 'v7TargetTerminalY'
    $points = @()
    if ($null -ne $sx -and $null -ne $sy) { $points += New-Point $sx $sy }
    $array = $edge.SelectSingleNode('mxGeometry/Array')
    if ($null -ne $array) {
        foreach ($point in @($array.SelectNodes('mxPoint'))) {
            $px = Get-Number $point 'x'; $py = Get-Number $point 'y'
            if ($null -ne $px -and $null -ne $py) { $points += New-Point $px $py }
        }
    }
    if ($null -ne $tx -and $null -ne $ty) { $points += New-Point $tx $ty }

    $nonOrthogonal = @()
    for ($i = 1; $i -lt $points.Count; $i++) {
        $a = $points[$i - 1]; $b = $points[$i]
        if ($a.X -ne $b.X -and $a.Y -ne $b.Y) {
            $nonOrthogonal += [pscustomobject]@{ From = $a; To = $b }
        }
        if ($a.X -eq $b.X -or $a.Y -eq $b.Y) {
            $segmentRecords += [pscustomobject]@{
                PhysicalLinkId = $physical
                Orientation = if ($a.X -eq $b.X) { 'V' } else { 'H' }
                Coordinate = if ($a.X -eq $b.X) { $a.X } else { $a.Y }
                Start = if ($a.X -eq $b.X) { [math]::Min($a.Y, $b.Y) } else { [math]::Min($a.X, $b.X) }
                End = if ($a.X -eq $b.X) { [math]::Max($a.Y, $b.Y) } else { [math]::Max($a.X, $b.X) }
            }
        }
        foreach ($node in $vertices.Values) {
            if ($node.Id -eq [string]$edge.source -or $node.Id -eq [string]$edge.target) { continue }
            if (Test-InteriorIntersection $a $b $node) {
                $nodeCrossings += [pscustomobject]@{
                    PhysicalLinkId = $physical
                    SemanticLinkId = [string]$edge.semanticLinkId
                    SourceNode = if ($null -ne $source) { $source.Value } else { [string]$edge.source }
                    TargetNode = if ($null -ne $target) { $target.Value } else { [string]$edge.target }
                    NodeId = $node.Id
                    Node = $node.Value
                    NodeBounds = [pscustomobject]@{ Left = $node.Left; Top = $node.Top; Right = $node.Right; Bottom = $node.Bottom }
                    From = $a
                    To = $b
                }
            }
        }
    }
    if ($nonOrthogonal.Count -gt 0) {
        $orthogonalityFindings += [pscustomobject]@{ PhysicalLinkId = $physical; Segments = $nonOrthogonal }
    }

    if ($null -ne $source -and $null -ne $sx -and $null -ne $sy) {
        if (-not (Test-Between $sx $source.Left $source.Right) -or [math]::Abs($sy - $source.Bottom) -gt 0.001) {
            $endpointFindings += [pscustomobject]@{ PhysicalLinkId = $physical; Endpoint = 'source'; X = $sx; Y = $sy; Node = $source.Value; Expected = 'bottom edge' }
        }
    }
    if ($null -ne $target -and $null -ne $tx -and $null -ne $ty) {
        if (-not (Test-Between $tx $target.Left $target.Right) -or [math]::Abs($ty - $target.Top) -gt 0.001) {
            $endpointFindings += [pscustomobject]@{ PhysicalLinkId = $physical; Endpoint = 'target'; X = $tx; Y = $ty; Node = $target.Value; Expected = 'top edge' }
        }
    }
    $routeRecords += [pscustomobject]@{ PhysicalLinkId = $physical; Source = $source.Value; Target = $target.Value; PointCount = $points.Count; Points = $points }
}

$duplicateSegments = @()
$segmentKeys = @{}
foreach ($route in $routeRecords) {
    for ($i = 1; $i -lt $route.Points.Count; $i++) {
        $a = $route.Points[$i - 1]; $b = $route.Points[$i]
        $key = if ($a.X -eq $b.X) { "V:$($a.X):$([math]::Min($a.Y,$b.Y)):$([math]::Max($a.Y,$b.Y))" } elseif ($a.Y -eq $b.Y) { "H:$($a.Y):$([math]::Min($a.X,$b.X)):$([math]::Max($a.X,$b.X))" } else { "D:$($a.X),$($a.Y):$($b.X),$($b.Y)" }
        if ($segmentKeys.ContainsKey($key)) { $segmentKeys[$key] += @($route.PhysicalLinkId) } else { $segmentKeys[$key] = @($route.PhysicalLinkId) }
    }
}
foreach ($item in $segmentKeys.GetEnumerator()) {
    $links = @($item.Value | Select-Object -Unique)
    if ($links.Count -gt 1) { $duplicateSegments += [pscustomobject]@{ Segment = $item.Key; PhysicalLinkIds = $links } }
}

$overlappingSegments = @()
for ($leftIndex = 0; $leftIndex -lt $segmentRecords.Count; $leftIndex++) {
    $left = $segmentRecords[$leftIndex]
    for ($rightIndex = $leftIndex + 1; $rightIndex -lt $segmentRecords.Count; $rightIndex++) {
        $right = $segmentRecords[$rightIndex]
        if ($left.PhysicalLinkId -eq $right.PhysicalLinkId -or $left.Orientation -ne $right.Orientation -or
            $left.Coordinate -ne $right.Coordinate) { continue }
        $overlapStart = [math]::Max($left.Start, $right.Start)
        $overlapEnd = [math]::Min($left.End, $right.End)
        if ($overlapEnd -le ($overlapStart + 0.001)) { continue }
        $overlappingSegments += [pscustomobject]@{
            Orientation = $left.Orientation
            Coordinate = $left.Coordinate
            Start = $overlapStart
            End = $overlapEnd
            PhysicalLinkIds = @($left.PhysicalLinkId, $right.PhysicalLinkId)
        }
    }
}

$duplicates = @($seenPhysical.GetEnumerator() | Where-Object Value -gt 1 | ForEach-Object { [pscustomobject]@{ PhysicalLinkId = $_.Key; Count = $_.Value } })
$result = [pscustomobject]@{
    file = (Resolve-Path -LiteralPath $DrawioPath).Path
    vertexCount = $vertices.Count
    edgeCellCount = $edges.Count
    distinctPhysicalRelationshipCount = $seenPhysical.Count
    duplicatePhysicalRelationshipIds = $duplicates
    nonOrthogonalRouteCount = $orthogonalityFindings.Count
    endpointContractFindingCount = $endpointFindings.Count
    routeThroughUnrelatedNodeCount = $nodeCrossings.Count
    sharedPhysicalSegmentCount = $duplicateSegments.Count
    collinearOverlappingSegmentCount = $overlappingSegments.Count
    findings = [pscustomobject]@{
        nonOrthogonalRoutes = $orthogonalityFindings
        endpointContract = $endpointFindings
        routeThroughUnrelatedNodes = $nodeCrossings
        sharedPhysicalSegments = $duplicateSegments
        collinearOverlappingSegments = $overlappingSegments
    }
}

$json = $result | ConvertTo-Json -Depth 12
if ($JsonOutputPath) { $json | Set-Content -LiteralPath $JsonOutputPath -Encoding UTF8 }
$result | Select-Object file,vertexCount,edgeCellCount,distinctPhysicalRelationshipCount,nonOrthogonalRouteCount,endpointContractFindingCount,routeThroughUnrelatedNodeCount,sharedPhysicalSegmentCount,collinearOverlappingSegmentCount | Format-List
if ($duplicates.Count -gt 0) { 'Duplicate physical relationship IDs:'; $duplicates | Format-Table -AutoSize }
if ($endpointFindings.Count -gt 0) { 'Endpoint contract findings (first 20):'; $endpointFindings | Select-Object -First 20 | Format-Table -AutoSize }
if ($nodeCrossings.Count -gt 0) { 'Route-through-node findings (first 20):'; $nodeCrossings | Select-Object -First 20 | Format-Table -AutoSize }
if ($duplicateSegments.Count -gt 0) { 'Shared physical segments (first 20):'; $duplicateSegments | Select-Object -First 20 | Format-Table -AutoSize }
if ($overlappingSegments.Count -gt 0) { 'Collinear overlapping segments (first 20):'; $overlappingSegments | Select-Object -First 20 | Format-Table -AutoSize }
