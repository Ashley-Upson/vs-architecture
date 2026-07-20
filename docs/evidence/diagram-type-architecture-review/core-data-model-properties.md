# StandardIo Core table property ledger

This is the exact property-row content captured for the 18 configured Core table nodes. Format is `displayed type: property name`.

- **WorkspacePathLoadOptions**: `string?: ProjectFilter`.
- **WorkspacePathLoadResult**: `string: Name`; `Project: Projects`.
- **CanvasSettings**: `string: BackgroundColor`; `string: DefaultFontColor`.
- **ConnectorStyle**: `bool: Rounded`; `string: StrokeColor`; `int: StrokeWidth`.
- **DiagramPathGenerationResult**: `string: Content`; `string: OutputPath`; `string: RendererId`; `string: TargetName`.
- **DiagramSettings**: `CanvasSettings: Canvas`; `ConnectorStyle: Connector`; `string: ExcludedNames`; `string: ExcludedNamespaces`; `NodeStyle: ExternalDependencyStyle`; `string: ExternalDependencyTag`; `LayoutSettings: Layout`; `NodeDuplicationSettings: NodeDuplication`; `string: OutputRenderer`; `StyleOverride: Overrides`; `NodeStyle: ProjectContainerStyle`; `string: RootDiscoveryPatternsText`; `bool: ShowProjectContainers`; `StyleRule: StyleRules`; `int: Version`.
- **DrawioDiagnosticExportResult**: `string: Content`; `int: EnforcedFindingCount`; `IReadOnlyDictionary<string, string>: FocusedOutputs`; `string: ReportJson`; `int: UniqueRejectedRouteCount`.
- **DrawioGenerationResult**: `int: DiagnosticMaterializationCount`; `DrawioDiagnosticExportResult: Diagnostics`; `string: Document`; `GeneratedRoute: FinalLogicalRoutes`; `PipelineStageMetric: GenerationTelemetry`; `int: PreparationCount`; `ValidationFinding: PreRepairFindings`; `RouteRepairAttempt: RepairAttempts`; `GeneratedRoute: Routes`; `bool: SerializationSucceeded`; `PipelineStageMetric: StageTimings`; `bool: StrictValidationPassed`; `ValidationFinding: ValidationFindings`.
- **GenerationPerformanceSession.Frame**: `long: ChildTicks`; `int: InputNodes`; `int: InputRoutes`; `int: InputSegments`; `int?: LayoutRevision`; `int: OutputObjects`; `Frame?: Parent`; `string: Path`; `string: Phase`; `int?: RouteRevision`; `long: StartTimestamp`.
- **LayoutSettings**: `string: BaselineAlignmentPattern`; `int: ContainerPadding`; `int: DataModelCanvasMargin`; `int: DataModelColumnWidth`; `int: DataModelComponentRowWidth`; `int: DataModelComponentSpacing`; `int: DataModelHeaderHeight`; `int: DataModelMinimumTableGap`; `int: DataModelPropertyRowHeight`; `int: DataModelRadialMinimumRadius`; `int: DataModelRadialRingSpacing`; `int: DataModelRelationshipLaneSpacing`; `int: DataModelRelationshipPortSpacing`; `int: DataModelRelationshipSideOffset`; `int: DataModelRelationshipStubLength`; `int: DataModelRowSpacing`; `int: DataModelTableWidth`; `string: DuplicateHighNoiseNodePatterns`; `int: EdgePortSpacing`; `int: ExposureTreeConnectorClearanceMultiplier`; `int: ExposureTreeConnectorDetourAttempts`; `int: ExposureTreeConnectorMinSegment`; `int: ExposureTreeDepthSpacingReduction`; `int: ExposureTreeHorizontalSpacingBonus`; `int: ExposureTreeLayoutThreshold`; `int: ExposureTreeMinHorizontalSpacing`; `int: ExposureTreeMinVerticalSpacing`; `int: HorizontalSpacing`; `int: LinkNodeWidthPadding`; `int: LinkPadding`; `int: NodeHeight`; `int: NodeWidth`; `int: ParallelLaneSpacing`; `int: ProjectHeaderHeight`; `int: StandaloneGroupSpacing`; `int: VerticalSpacing`.
- **NodeDuplicationSettings**: `bool: AllowDuplicateNodes`; `string: DuplicationExceptionPatterns`.
- **NodeStyle**: `string?: ExtraStyle`; `string: FillColor`; `string: FontColor`; `bool: Shadow`; `string: Shape`; `string: StrokeColor`.
- **StyleOverride**: `string: FullName`; `NodeStyle: Style`.
- **StyleRule**: `string: Match`; `NodeStyle: Style`.
- **NodeOwnership**: `IReadOnlyDictionary<string, string>: ExternalOwnerProjectByNode`; `IReadOnlyDictionary<string, string>: ProjectByNode`; `string: RootOwnedNodeIds`.
- **RenderGraph**: `RenderNode: DataModels`; `RenderLink: Links`; `RenderNode: Nodes`; `IReadOnlyDictionary<string, string>: PlacementParentByNode`; `RenderProject: Projects`.
- **RoutedEdgeGeometry**: `Point: Points`; `Segment: Segments`.
- **TraceabilityValidationResult**: `bool: IsValid`; `TraceabilityViolation: Violations`.

The displayed type names for whitelisted collections are their unwrapped element types, so collection cardinality is already lost in this ledger. The five resolved relationships are:

1. `DiagramSettings.Canvas -> CanvasSettings`
2. `DiagramSettings.Connector -> ConnectorStyle`
3. `DrawioGenerationResult.Diagnostics -> DrawioDiagnosticExportResult`
4. `StyleOverride.Style -> NodeStyle`
5. `StyleRule.Style -> NodeStyle`
