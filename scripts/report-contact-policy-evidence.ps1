param(
    [Parameter(Mandatory = $true)] [string[]]$DiagnosticPaths,
    [string]$OutputPath = 'docs\evidence\canonical-contact-policy-evidence.csv',
    [string]$ComponentMapPath = 'docs\evidence\ccoder-deduplicated-contact-component-map.csv'
)

$ErrorActionPreference = 'Stop'

function New-Set([string[]]$ids) { $p=@{}; foreach($id in $ids){$p[$id]=$id}; $p }
function Root($p,[string]$id){$x=$id;while($p[$x]-ne$x){$x=$p[$x]};$r=$x;$x=$id;while($p[$x]-ne$x){$n=$p[$x];$p[$x]=$r;$x=$n};$r}
function Join($p,[string]$a,[string]$b){
    if(!$p.ContainsKey($a)-or!$p.ContainsKey($b)-or$a-eq$b){return}
    $ra=Root $p $a;$rb=Root $p $b;if($ra-eq$rb){return}
    if([string]::CompareOrdinal($ra,$rb)-lt 0){$p[$rb]=$ra}else{$p[$ra]=$rb}
}
function Summary($p,[string[]]$ids){
    $g=@{};foreach($id in $ids){$r=Root $p $id;if(!$g.ContainsKey($r)){$g[$r]=0};$g[$r]++}
    $sizes=@($g.Values|Sort-Object);[pscustomobject]@{
        Count=$sizes.Count;Largest=if($sizes.Count){$sizes[-1]}else{0};
        Median=if($sizes.Count){$sizes[[int][math]::Floor(($sizes.Count-1)/2)]}else{0};
        Singletons=@($sizes|Where-Object{$_-eq 1}).Count
    }
}
function Add-Edges($p,$edges){foreach($edge in $edges){Join $p $edge.A $edge.B}}
function Encode-Summary($s){"$($s.Count)/$($s.Largest)/$($s.Median)/$($s.Singletons)"}
function Encode-Counts($items,[string]$property){
    @($items|Group-Object $property|Sort-Object -Property @{Expression='Count';Descending=$true},@{Expression='Name';Ascending=$true}|ForEach-Object{"$($_.Name)=$($_.Count)"})-join';'
}

$mapRows=@()
$rows=foreach($path in $DiagnosticPaths){
    $report=Get-Content (Resolve-Path $path) -Raw|ConvertFrom-Json
    $routes=@($report.routeGeometry);$ids=@($routes.logicalRouteId|Sort-Object -Unique)
    $byId=@{};foreach($r in $routes){$byId[$r.logicalRouteId]=$r}
    $facts=@($report.consolidatedFoundation.contacts.facts)
    $factEdges=@($facts|ForEach-Object{[pscustomobject]@{A=$_.firstRouteId;B=$_.secondRouteId;Reason="Contact:$($_.kind)";Kind=$_.kind;Policy=[bool]$_.createsFinalGeometryEdge}})
    $policyEdges=@($factEdges|Where-Object Policy)
    $cleanEdges=@($factEdges|Where-Object Kind -eq 'CleanPerpendicularCrossover')

    $bandByRoute=@{};foreach($id in $ids){$bandByRoute[$id]=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)}
    $railEdges=[Collections.Generic.List[object]]::new()
    foreach($band in @($report.interLayerBands.bands)){
        foreach($m in @($band.memberships)){if($bandByRoute.ContainsKey($m.logicalEdgeIdentity)){[void]$bandByRoute[$m.logicalEdgeIdentity].Add([string]$band.id)}}
        $demands=@($band.demands|Sort-Object xStart,xEnd,logicalEdgeIdentity)
        for($i=0;$i-lt$demands.Count;$i++){for($j=$i+1;$j-lt$demands.Count;$j++){
            if([double]$demands[$j].xStart-gt[double]$demands[$i].xEnd){break}
            if([double]$demands[$j].xEnd-gt[double]$demands[$i].xStart){
                $railEdges.Add([pscustomobject]@{A=$demands[$i].logicalEdgeIdentity;B=$demands[$j].logicalEdgeIdentity;Reason='UnassignedRailDemand';Kind='RailIntervalConflict';Policy=$true})
            }
        }}
    }

    $incident=@{};foreach($r in $routes){foreach($node in @($r.sourceId,$r.targetId)){if(!$incident.ContainsKey($node)){$incident[$node]=[Collections.Generic.List[string]]::new()};$incident[$node].Add($r.logicalRouteId)}}
    $obstacleEdges=[Collections.Generic.List[object]]::new()
    foreach($f in @($report.repair.postRepairFindings|Where-Object otherNodeId)){
        foreach($other in @($incident[$f.otherNodeId])){$obstacleEdges.Add([pscustomobject]@{A=$f.logicalRouteId;B=$other;Reason='ObstacleBypass';Kind='ObstacleBypass';Policy=$true})}
    }

    $unresolvedTerminal=[Collections.Generic.List[object]]::new();$resolvedTerminal=[Collections.Generic.List[object]]::new()
    $claims=@($routes|ForEach-Object{
        [pscustomobject]@{K="$($_.sourceId):OutgoingBottom:bottom";KR="$($_.sourceId):OutgoingBottom:bottom:$($_.points[0].x)";R=$_.logicalRouteId}
        [pscustomobject]@{K="$($_.targetId):IncomingTop:top";KR="$($_.targetId):IncomingTop:top:$($_.points[-1].x)";R=$_.logicalRouteId}
    })
    foreach($g in @($claims|Group-Object K)){ $m=@($g.Group.R|Sort-Object -Unique);for($i=1;$i-lt$m.Count;$i++){$unresolvedTerminal.Add([pscustomobject]@{A=$m[0];B=$m[$i];Reason='UnresolvedTerminalCompetition';Kind='TerminalContact';Policy=$true})}}
    foreach($g in @($claims|Group-Object KR)){ $m=@($g.Group.R|Sort-Object -Unique);for($i=1;$i-lt$m.Count;$i++){$resolvedTerminal.Add([pscustomobject]@{A=$m[0];B=$m[$i];Reason='ConflictingAssignedTerminal';Kind='TerminalContact';Policy=$true})}}

    $movementEdges=[Collections.Generic.List[object]]::new()
    foreach($band in @($report.interLayerBands.bands|Where-Object missingExtent -gt 0)){
        $affectedBands=@($report.interLayerBands.bands|Where-Object{[int]$_.upperLayer-ge[int]$band.lowerLayer-or[int]$_.lowerLayer-ge[int]$band.lowerLayer}|ForEach-Object{[string]$_.id})
        $affected=@($ids|Where-Object{$id=$_;@($bandByRoute[$id]|Where-Object{$affectedBands-contains$_}).Count-gt 0}|Sort-Object)
        for($i=1;$i-lt$affected.Count;$i++){$movementEdges.Add([pscustomobject]@{A=$affected[0];B=$affected[$i];Reason="MovementScope:$($band.id)";Kind='MovementScope';Policy=$true})}
    }

    $broad=New-Set $ids;Add-Edges $broad @($policyEdges+$cleanEdges+$railEdges+$obstacleEdges+$unresolvedTerminal+$movementEdges)
    $factual=New-Set $ids;Add-Edges $factual @($factEdges+$railEdges+$obstacleEdges+$unresolvedTerminal+$movementEdges)
    $policy=New-Set $ids;Add-Edges $policy @($policyEdges+$railEdges+$obstacleEdges+$unresolvedTerminal+$movementEdges)
    $resolved=New-Set $ids;Add-Edges $resolved @($policyEdges+$railEdges+$obstacleEdges+$resolvedTerminal+$movementEdges)
    $broadSummary=Summary $broad $ids;$factualSummary=Summary $factual $ids;$policySummary=Summary $policy $ids;$resolvedSummary=Summary $resolved $ids

    $name=[IO.Path]::GetFileNameWithoutExtension($path)
    $widthNodes=@($report.consolidatedFoundation.nodeWidths.nodes)
    if($name-eq'ccoder-deduplicated'){
        $allPolicy=@($policyEdges+$railEdges+$obstacleEdges+$resolvedTerminal+$movementEdges)
        $mapRows=@($allPolicy|Group-Object Reason|ForEach-Object{
            $routeSet=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);foreach($e in $_.Group){[void]$routeSet.Add($e.A);[void]$routeSet.Add($e.B)}
            [pscustomobject]@{Reason=$_.Name;Edges=$_.Count;DistinctRoutes=$routeSet.Count;ExampleA=$_.Group[0].A;ExampleB=$_.Group[0].B}
        }|Sort-Object -Property @{Expression='Edges';Descending=$true},@{Expression='Reason';Ascending=$true})
    }

    [pscustomobject]@{
        Name=$name;Routes=$ids.Count
        LegacyBroad=(Encode-Summary $broadSummary);CanonicalFactual=(Encode-Summary $factualSummary)
        PolicyFiltered=(Encode-Summary $policySummary);TerminalResolvedPolicy=(Encode-Summary $resolvedSummary)
        CanonicalFactEdges=$factEdges.Count;PolicyContactEdges=$policyEdges.Count
        ContactFacts=(Encode-Counts $factEdges 'Kind');PolicyReasons=(Encode-Counts @($policyEdges+$railEdges+$obstacleEdges+$unresolvedTerminal+$movementEdges) 'Reason')
        CleanCrossoverEdgesRemoved=$cleanEdges.Count;BendInvolvedEdgesRetained=@($policyEdges|Where-Object Kind -eq 'BendInvolvedPerpendicularContact').Count
        SharedSpacingEdgesRetained=@($policyEdges|Where-Object{$_.Kind-in@('PositiveCollinearOverlap','NearParallelSpacingConflict')}).Count
        RailIntervalEdgesBefore=$railEdges.Count;UnassignedRailEdgesAfter=$railEdges.Count;IndependentAssignedRailEdgesRemoved=0
        UnresolvedTerminalEdges=$unresolvedTerminal.Count;ResolvedConflictingTerminalEdges=$resolvedTerminal.Count
        WinningCurrent=@($widthNodes|Where-Object{$_.winningRequirements-contains'Current'}).Count
        WinningText=@($widthNodes|Where-Object{$_.winningRequirements-contains'Text'}).Count
        WinningIncoming=@($widthNodes|Where-Object{$_.winningRequirements-contains'Incoming'}).Count
        WinningOutgoing=@($widthNodes|Where-Object{$_.winningRequirements-contains'Outgoing'}).Count
        MultipleWinningRequirements=@($widthNodes|Where-Object{@($_.winningRequirements).Count-gt 1}).Count
        ActualResizes=@($widthNodes|Where-Object actualResize).Count
        CanonicalContactMicroseconds=[long]$report.consolidatedFoundation.timings.canonicalContactClassificationMicroseconds
        PolicyProjectionMicroseconds=[long]$report.consolidatedFoundation.contacts.policyProjectionMicroseconds
    }
}

$out=[IO.Path]::GetFullPath($OutputPath);[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($out))|Out-Null;$rows|Export-Csv $out -NoTypeInformation
$map=[IO.Path]::GetFullPath($ComponentMapPath);[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($map))|Out-Null;$mapRows|Export-Csv $map -NoTypeInformation
$rows|Format-Table Name,Routes,LegacyBroad,CanonicalFactual,PolicyFiltered,TerminalResolvedPolicy,CleanCrossoverEdgesRemoved,BendInvolvedEdgesRetained,RailIntervalEdgesBefore -AutoSize
Write-Output "Evidence: $out";Write-Output "Component map: $map"
