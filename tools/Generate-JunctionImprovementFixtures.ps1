param(
    [string]$OutputPath = "artifacts\junction-improvement\junction-improvement-fixtures.drawio"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Xml.Linq

function Element([string]$name) {
    [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get($name))
}

function Route([string]$id, [string]$lane, [string]$legacy, [string]$allocated, [string]$diagnostic = "") {
    [pscustomobject]@{ Id = $id; Lane = $lane; Legacy = $legacy; Allocated = $allocated; Diagnostic = $diagnostic }
}

$cases = @(
    @{ Name = "01 three parallel right-down turns"; Routes = @(
        (Route "A" "H0 to V0" "20,60 100,60 120,140" "20,60 120,60 120,140"),
        (Route "B" "H1 to V1" "30,70 100,60 110,140" "30,70 110,70 110,140"),
        (Route "C" "H2 to V2" "40,80 100,60 100,140" "40,80 100,80 100,140")) },
    @{ Name = "02 four parallel right-down turns"; Routes = @(
        (Route "A" "H0 to V0" "20,60 100,60 130,150" "20,60 130,60 130,150"),
        (Route "B" "H1 to V1" "30,70 100,60 120,150" "30,70 120,70 120,150"),
        (Route "C" "H2 to V2" "40,80 100,60 110,150" "40,80 110,80 110,150"),
        (Route "D" "H3 to V3" "50,90 100,60 100,150" "50,90 100,90 100,150")) },
    @{ Name = "03 inner route departs"; Routes = @(
        (Route "inner" "H0 to V0" "20,60 100,60 100,140" "20,60 100,60 100,140"),
        (Route "middle" "H1" "20,70 160,70" "20,70 160,70"),
        (Route "outer" "H2" "20,80 160,80" "20,80 160,80")) },
    @{ Name = "04 outer route departs"; Routes = @(
        (Route "inner" "H0" "20,60 160,60" "20,60 160,60"),
        (Route "middle" "H1" "20,70 160,70" "20,70 160,70"),
        (Route "outer" "H2 to V2" "20,80 100,70 100,150" "20,80 120,80 120,150")) },
    @{ Name = "05 departures in different directions"; Routes = @(
        (Route "up" "H0 to V-up" "20,70 100,70 100,20" "20,70 100,70 100,20" "UNSUPPORTED_JUNCTION_TOPOLOGY"),
        (Route "down" "H1 to V-down" "20,80 100,80 100,150" "20,80 100,80 100,150" "UNSUPPORTED_JUNCTION_TOPOLOGY")) },
    @{ Name = "06 separate sources enter common corridor"; Routes = @(
        (Route "A" "S0 to H0" "20,20 60,70 180,70" "20,20 60,60 60,70 180,70"),
        (Route "B" "S1 to H1" "40,20 60,70 180,80" "40,20 70,20 70,80 180,80")) },
    @{ Name = "07 neighbouring targets after shared turn"; Routes = @(
        (Route "left-target" "H0 to V0" "20,60 100,60 100,160" "20,60 110,60 110,160"),
        (Route "middle-target" "H1 to V1" "20,70 100,60 120,160" "20,70 120,70 120,160"),
        (Route "right-target" "H2 to V2" "20,80 100,60 130,160" "20,80 130,80 130,160")) },
    @{ Name = "08 reused legacy bend"; Routes = @(
        (Route "A" "H0 to V0" "20,60 100,80 120,150" "20,60 120,60 120,150"),
        (Route "B" "H1 to V1" "20,70 100,80 110,150" "20,70 110,70 110,150")) },
    @{ Name = "09 avoidable lane-transition crossing"; Routes = @(
        (Route "A" "H0 to V1" "20,60 120,60 120,150" "20,60 110,60 110,150"),
        (Route "B" "H1 to V0" "20,70 110,70 110,150" "20,70 120,70 120,150")) },
    @{ Name = "10 explicit required lane-order change"; Routes = @(
        (Route "A" "H0 to V1" "20,60 110,60 110,150" "20,60 90,60 90,50 120,50 120,150" "LANE_ORDER_CHANGE_EXPLICIT"),
        (Route "B" "H1 to V0" "20,70 120,70 120,150" "20,70 100,70 100,90 110,90 110,150" "LANE_ORDER_CHANGE_EXPLICIT")) }
)

function Points([string]$value, [int]$xOffset) {
    @($value.Split(' ') | ForEach-Object {
        $parts = $_.Split(',')
        [pscustomobject]@{ X = [int]$parts[0] + $xOffset; Y = [int]$parts[1] }
    })
}

function Add-Route($root, $route, [string]$geometry, [int]$xOffset, [string]$colour, [int]$index) {
    $points = Points $geometry $xOffset
    $object = Element "object"
    $object.SetAttributeValue("id", "route-$index")
    $object.SetAttributeValue("label", $route.Id)
    $object.SetAttributeValue("routeId", $route.Id)
    $object.SetAttributeValue("laneAssignment", $route.Lane)
    $object.SetAttributeValue("diagnostic", $route.Diagnostic)
    $object.SetAttributeValue("pointSequence", ($points | ForEach-Object { "$($_.X),$($_.Y)" }) -join " ")
    $cell = Element "mxCell"
    $cell.SetAttributeValue("style", "edgeStyle=none;orthogonalLoop=0;jettySize=auto;html=1;strokeWidth=2;strokeColor=$colour;endArrow=block;endFill=1;")
    $cell.SetAttributeValue("edge", "1")
    $cell.SetAttributeValue("parent", "1")
    $geometryElement = Element "mxGeometry"
    $geometryElement.SetAttributeValue("relative", "1")
    $geometryElement.SetAttributeValue("as", "geometry")
    $source = Element "mxPoint"
    $source.SetAttributeValue("x", $points[0].X); $source.SetAttributeValue("y", $points[0].Y); $source.SetAttributeValue("as", "sourcePoint")
    $target = Element "mxPoint"
    $target.SetAttributeValue("x", $points[-1].X); $target.SetAttributeValue("y", $points[-1].Y); $target.SetAttributeValue("as", "targetPoint")
    $array = Element "Array"; $array.SetAttributeValue("as", "points")
    foreach ($point in $points[1..($points.Count - 2)]) {
        $item = Element "mxPoint"; $item.SetAttributeValue("x", $point.X); $item.SetAttributeValue("y", $point.Y); $array.Add($item)
    }
    $geometryElement.Add($source); $geometryElement.Add($target); $geometryElement.Add($array); $cell.Add($geometryElement); $object.Add($cell); $root.Add($object)
}

$mxfile = Element "mxfile"
$mxfile.SetAttributeValue("host", "app.diagrams.net")
foreach ($case in $cases) {
    $diagram = Element "diagram"; $diagram.SetAttributeValue("name", $case.Name)
    $model = Element "mxGraphModel"; $root = Element "root"
    $zero = Element "mxCell"; $zero.SetAttributeValue("id", "0")
    $one = Element "mxCell"; $one.SetAttributeValue("id", "1"); $one.SetAttributeValue("parent", "0")
    $root.Add($zero); $root.Add($one)
    $index = 0
    foreach ($route in $case.Routes) { Add-Route $root $route $route.Legacy 0 "#d79b00" $index; $index++ }
    foreach ($route in $case.Routes) { Add-Route $root $route $route.Allocated 240 "#00a65a" $index; $index++ }
    $model.Add($root); $diagram.Add($model); $mxfile.Add($diagram)
}

$document = [System.Xml.Linq.XDocument]::new($mxfile)
$resolved = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolved)) | Out-Null
$document.Save($resolved)
$resolved
