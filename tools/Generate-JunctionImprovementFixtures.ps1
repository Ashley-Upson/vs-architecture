param(
    [string]$OutputPath = "artifacts\junction-improvement\junction-improvement-fixtures.drawio"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Xml.Linq

function Element([string]$name) {
    [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get($name))
}

function Save-Drawio($document, [string]$path) {
    $utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText(
        $path,
        $document.Root.ToString([System.Xml.Linq.SaveOptions]::DisableFormatting),
        $utf8WithoutBom)
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
        (Route "A" "S0 to H0" "20,20 60,70 180,70" "20,20 60,70 180,70" "UNSUPPORTED_JUNCTION_TOPOLOGY"),
        (Route "B" "S1 to H1" "40,20 60,70 180,80" "40,20 60,70 180,80" "UNSUPPORTED_JUNCTION_TOPOLOGY")) },
    @{ Name = "07 neighbouring targets after shared turn"; Routes = @(
        (Route "left-target" "H0 to V0" "20,60 100,60 100,160" "20,60 110,60 110,160"),
        (Route "middle-target" "H1 to V1" "20,70 100,60 120,160" "20,70 120,70 120,160"),
        (Route "right-target" "H2 to V2" "20,80 100,60 130,160" "20,80 130,80 130,160")) },
    @{ Name = "08 reused legacy bend"; Routes = @(
        (Route "A" "H0 to V0" "20,60 100,80 120,150" "20,60 120,60 120,150"),
        (Route "B" "H1 to V1" "20,70 100,80 110,150" "20,70 110,70 110,150")) },
    @{ Name = "09 avoidable lane-transition crossing"; Routes = @(
        (Route "A" "H0 to V1" "20,60 120,60 120,150" "20,60 120,60 120,150" "JUNCTION_LANE_ORDER_INVERSION"),
        (Route "B" "H1 to V0" "20,70 110,70 110,150" "20,70 110,70 110,150" "JUNCTION_LANE_ORDER_INVERSION")) },
    @{ Name = "10 explicit required lane-order change"; Routes = @(
        (Route "A" "H0 to V1" "20,60 110,60 110,150" "20,60 110,60 110,150" "JUNCTION_LANE_ORDER_INVERSION"),
        (Route "B" "H1 to V0" "20,70 120,70 120,150" "20,70 120,70 120,150" "JUNCTION_LANE_ORDER_INVERSION")) }
)

function Points([string]$value, [int]$xOffset) {
    @($value.Split(' ') | ForEach-Object {
        $parts = $_.Split(',')
        [pscustomobject]@{ X = [int]$parts[0] + $xOffset; Y = [int]$parts[1] }
    })
}

function Add-Route($root, $route, [string]$geometry, [int]$xOffset, [string]$colour, [int]$index) {
    $points = Points $geometry $xOffset
    $sourceId = "route-$index-source"
    $targetId = "route-$index-target"
    foreach ($endpoint in @(
        @{ Id = $sourceId; Point = $points[0] },
        @{ Id = $targetId; Point = $points[-1] })) {
        $vertex = Element "mxCell"
        $vertex.SetAttributeValue("id", $endpoint.Id)
        $vertex.SetAttributeValue("value", "")
        $vertex.SetAttributeValue("style", "opacity=0;fillOpacity=0;strokeOpacity=0;movable=0;resizable=0;deletable=0;")
        $vertex.SetAttributeValue("vertex", "1")
        $vertex.SetAttributeValue("parent", "1")
        $vertexGeometry = Element "mxGeometry"
        $vertexGeometry.SetAttributeValue("x", $endpoint.Point.X)
        $vertexGeometry.SetAttributeValue("y", $endpoint.Point.Y)
        $vertexGeometry.SetAttributeValue("width", "0")
        $vertexGeometry.SetAttributeValue("height", "0")
        $vertexGeometry.SetAttributeValue("as", "geometry")
        $vertex.Add($vertexGeometry)
        $root.Add($vertex)
    }
    $cell = Element "mxCell"
    $cell.SetAttributeValue("id", "route-$index")
    $cell.SetAttributeValue("value", $route.Id)
    $cell.SetAttributeValue("routeId", $route.Id)
    $cell.SetAttributeValue("laneAssignment", $route.Lane)
    $cell.SetAttributeValue("diagnostic", $route.Diagnostic)
    $cell.SetAttributeValue("pointSequence", ($points | ForEach-Object { "$($_.X),$($_.Y)" }) -join " ")
    $cell.SetAttributeValue("style", "edgeStyle=none;orthogonalLoop=0;jettySize=auto;html=1;strokeWidth=2;strokeColor=$colour;endArrow=block;endFill=1;")
    $cell.SetAttributeValue("edge", "1")
    $cell.SetAttributeValue("parent", "1")
    $cell.SetAttributeValue("source", $sourceId)
    $cell.SetAttributeValue("target", $targetId)
    $geometryElement = Element "mxGeometry"
    $geometryElement.SetAttributeValue("relative", "1")
    $geometryElement.SetAttributeValue("as", "geometry")
    $array = Element "Array"; $array.SetAttributeValue("as", "points")
    for ($pointIndex = 1; $pointIndex -lt $points.Count - 1; $pointIndex++) {
        $point = $points[$pointIndex]
        $item = Element "mxPoint"; $item.SetAttributeValue("x", $point.X); $item.SetAttributeValue("y", $point.Y); $array.Add($item)
    }
    $geometryElement.Add($array); $cell.Add($geometryElement); $root.Add($cell)
}

$mxfile = Element "mxfile"
$mxfile.SetAttributeValue("host", "app.diagrams.net")
$diagramIndex = 0
foreach ($case in $cases) {
    $diagramIndex++
    $diagram = Element "diagram"; $diagram.SetAttributeValue("name", $case.Name)
    $diagram.SetAttributeValue("id", "junction-fixture-$diagramIndex")
    $model = Element "mxGraphModel"; $root = Element "root"
    $model.SetAttributeValue("dx", "1200"); $model.SetAttributeValue("dy", "900"); $model.SetAttributeValue("grid", "0"); $model.SetAttributeValue("page", "0")
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
$fixtureDirectory = Join-Path ([System.IO.Path]::GetDirectoryName($resolved)) "junction-fixtures"
[System.IO.Directory]::CreateDirectory($fixtureDirectory) | Out-Null
$diagrams = @($mxfile.Elements())
for ($index = 0; $index -lt $diagrams.Count; $index++) {
    $singleFile = Element "mxfile"
    $singleFile.SetAttributeValue("host", "app.diagrams.net")
    $singleFile.Add([System.Xml.Linq.XElement]::Parse($diagrams[$index].ToString()))
    $singleDocument = [System.Xml.Linq.XDocument]::new($singleFile)
    $casePath = Join-Path $fixtureDirectory ("{0:D2}-junction-fixture.drawio" -f ($index + 1))
    Save-Drawio $singleDocument $casePath
    if ($index -eq 0) {
        Save-Drawio $singleDocument $resolved
    }
}
$catalogPath = Join-Path ([System.IO.Path]::GetDirectoryName($resolved)) "junction-improvement-catalog.drawio"
Save-Drawio $document $catalogPath
$resolved
