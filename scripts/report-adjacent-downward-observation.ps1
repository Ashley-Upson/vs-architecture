param(
    [Parameter(Mandatory = $true)] [string[]]$DiagnosticPaths,
    [string]$OutputPath = 'docs\evidence\adjacent-downward-rail-observation.csv'
)

$ErrorActionPreference = 'Stop'

function Encode-Object($value) {
    if ($null -eq $value) { return '' }
    @($value.PSObject.Properties | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ';'
}

$rows = foreach ($path in $DiagnosticPaths) {
    $resolved = Resolve-Path $path
    $report = Get-Content $resolved -Raw | ConvertFrom-Json
    $observation = $report.consolidatedFoundation.adjacentDownward
    $routes = @($observation.routes)
    $eligible = @($routes | Where-Object eligible)
    $metadataKeys = @($eligible | ForEach-Object { $_.laneMappings } | ForEach-Object {
        $_.specializedMetadata.PSObject.Properties.Name
    } | Sort-Object -Unique)
    [pscustomobject]@{
        Graph = [IO.Path]::GetFileNameWithoutExtension($path)
        Routes = @($report.routeGeometry).Count
        EligibleRoutes = [int]$observation.eligibleRoutes
        RejectedRoutes = [int]$observation.rejectedRoutes
        Rejections = Encode-Object $observation.rejections
        DemandsByRole = Encode-Object $observation.demandsByRole
        Transitions = [int]$observation.transitions
        AssignedRailSources = Encode-Object $observation.assignedSources
        SpecialisedMetadataKeys = $metadataKeys -join ';'
        Parity = Encode-Object $observation.parity
        UnassignedComponents = [int]$observation.unassignedComponents
        AssignedComponents = [int]$observation.assignedComponents
        LargestAssignedComponent = [int]$observation.largestAssignedComponent
        EdgesRemovedAfterAssignment = [int]$observation.edgesRemovedAfterAssignment
        RoutesInRemovedEdges = @($observation.routesInRemovedEdges).Count
        RemainingAssignedEdges = Encode-Object $observation.assignedEdges
        DemandProductionMicroseconds = [long]$observation.timings.demandProductionMicroseconds
        ExistingLaneAdaptationMicroseconds = [long]$observation.timings.existingLaneAdaptationMicroseconds
        ReconstructionMicroseconds = [long]$observation.timings.reconstructionMicroseconds
        ParityComparisonMicroseconds = [long]$observation.timings.parityComparisonMicroseconds
        ComponentProjectionMicroseconds = [long]$observation.timings.componentProjectionMicroseconds
        DrawioSha256 = (Get-FileHash ([IO.Path]::ChangeExtension($resolved.Path, '.drawio')) -Algorithm SHA256).Hash
    }
}

$output = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$rows | Export-Csv $output -NoTypeInformation
$rows | Format-Table Graph, Routes, EligibleRoutes, RejectedRoutes, UnassignedComponents,
    AssignedComponents, LargestAssignedComponent, EdgesRemovedAfterAssignment -AutoSize
Write-Output "Evidence: $output"
