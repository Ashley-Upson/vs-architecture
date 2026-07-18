param(
    [Parameter(Mandatory = $true)] [string[]]$DiagnosticPaths,
    [string]$OutputPath = 'docs\evidence\common-rail-assignment-evidence.csv'
)

$ErrorActionPreference = 'Stop'
function Encode($value) {
    if ($null -eq $value) { return '' }
    @($value.PSObject.Properties | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ';'
}

$rows = foreach ($path in $DiagnosticPaths) {
    $report = Get-Content (Resolve-Path $path) -Raw | ConvertFrom-Json
    $adjacent = $report.consolidatedFoundation.adjacentDownward
    $common = $adjacent.commonAssignment
    [pscustomobject]@{
        Graph = [IO.Path]::GetFileNameWithoutExtension($path) -replace '-v2$',''
        MappableDemandSets = [int]$common.regions
        CommonConflictComponents = [int]$common.components
        DemandsAssigned = [int]$common.demandsAssigned
        LargestComponent = [int]$common.largestComponent
        ExistingAssignmentParity = Encode $common.existingParity
        ReconstructionParity = Encode $common.reconstructionParity
        AssignmentFailures = @($common.routes | Where-Object reconstructionParity -eq 3).Count
        RequiredExtentDifferences = @($common.requiredExtents | Where-Object missingExtent -gt 0).Count
        ConstraintProposals = [int]$common.constraintProposals
        HardFindingsIntroduced = @($common.routes | Where-Object reconstructionParity -eq 2).Count
        UnassignedComponents = [int]$adjacent.unassignedComponents
        ExistingAssignedComponents = [int]$adjacent.assignedComponents
        ExistingEdgesRemoved = [int]$adjacent.edgesRemovedAfterAssignment
        ExistingRemainingEdges = Encode $adjacent.assignedEdges
        ConflictDiscoveryMicroseconds = [long]$common.timings.conflictDiscoveryMicroseconds
        ComponentConstructionMicroseconds = [long]$common.timings.componentConstructionMicroseconds
        LaneAssignmentMicroseconds = [long]$common.timings.laneAssignmentMicroseconds
        ExtentCalculationMicroseconds = [long]$common.timings.extentCalculationMicroseconds
        ConstraintProjectionMicroseconds = [long]$common.timings.constraintProjectionMicroseconds
        ReconstructionMicroseconds = [long]$common.timings.reconstructionMicroseconds
        ParityComparisonMicroseconds = [long]$common.timings.parityComparisonMicroseconds
        DrawioSha256 = (Get-FileHash ([IO.Path]::ChangeExtension((Resolve-Path $path).Path, '.drawio')) -Algorithm SHA256).Hash
    }
}

$output = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$rows | Export-Csv $output -NoTypeInformation
$rows | Format-Table Graph,MappableDemandSets,CommonConflictComponents,DemandsAssigned,LargestComponent,
    AssignmentFailures,HardFindingsIntroduced -AutoSize
Write-Output "Evidence: $output"
