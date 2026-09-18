# Render-model rule catalogue

[Gateway](../../README.md) | [Execution contract](../rule-contract.md)

Registration order below is executable order. Every service is registered once. Architecture rules declare their diagram applicability through a common base; the engine does not choose geometry policies.

1. [Depth](Depth.md) - `DepthLayoutRuleProcessingService`
2. [ArchitecturalLayer](ArchitecturalLayer.md) - `ArchitecturalLayerRuleProcessingService`
3. [CategoryRow](CategoryRow.md) - `CategoryRowLayoutRuleProcessingService`
4. [FiniteCoordinates](FiniteCoordinates.md) - `FiniteCoordinatesLayoutRuleProcessingService`
5. [ConnectionEndpoints](ConnectionEndpoints.md) - `ConnectionEndpointsLayoutRuleProcessingService`
6. [ContextualCanvas](ContextualCanvas.md) - `ContextualCanvasLayoutRuleProcessingService`
7. [NodeSpacing](NodeSpacing.md) - `NodeSpacingLayoutRuleProcessingService`
8. [BranchSpacing](BranchSpacing.md) - `BranchSpacingLayoutRuleProcessingService`
9. [ParentCentring](ParentCentring.md) - `ParentCentringLayoutRuleProcessingService`
10. [LayoutCleanup](LayoutCleanup.md) - `LayoutCleanupRuleProcessingService`
11. [SharedChainAlignment](SharedChainAlignment.md) - `SharedChainAlignmentLayoutRuleProcessingService`
12. [VerticalPassage](VerticalPassage.md) - `VerticalPassageLayoutRuleProcessingService`
13. [NodeLocality](NodeLocality.md) - `NodeLocalityLayoutRuleProcessingService`
14. [GutterSpacing](GutterSpacing.md) - `GutterSpacingLayoutRuleProcessingService`
15. [Bounds](Bounds.md) - `BoundsLayoutRuleProcessingService`
16. [LabelPosition](LabelPosition.md) - `LabelPositionLayoutRuleProcessingService`
17. [ProjectPositioning](ProjectPositioning.md) - `ProjectPositioningLayoutRuleProcessingService`
18. [ProjectBranchSpacing](ProjectBranchSpacing.md) - `ProjectBranchSpacingLayoutRuleProcessingService`
19. [ProjectParentCentring](ProjectParentCentring.md) - `ProjectParentCentringLayoutRuleProcessingService`
20. [ProjectSharedCentring](ProjectSharedCentring.md) - `ProjectSharedCentringLayoutRuleProcessingService`
21. [CanvasBounds](CanvasBounds.md) - `CanvasBoundsLayoutRuleProcessingService`
22. [Routing](Routing.md) - `RoutingLayoutRuleProcessingService`
23. [CrossProjectRouting](CrossProjectRouting.md) - `CrossProjectRoutingLayoutRuleProcessingService`
24. [UnclassifiedLink](UnclassifiedLink.md) - `UnclassifiedLinkLayoutRuleProcessingService`
25. [SameLayerLink](SameLayerLink.md) - `SameLayerLinkLayoutRuleProcessingService`
26. [ExposureHandoffLink](ExposureHandoffLink.md) - `ExposureHandoffLinkLayoutRuleProcessingService`
27. [AdjacentLayerLink](AdjacentLayerLink.md) - `AdjacentLayerLinkLayoutRuleProcessingService`
28. [DestinationLinkColour](DestinationLinkColour.md) - `DestinationLinkColourLayoutRuleProcessingService`
29. [LayerLinkReview](LayerLinkReview.md) - `LayerLinkReviewRuleProcessingService`

[Initial construction](initialization.md) is outside the repeated rule sequence. `GetViolations` reports unmet hard conditions after all adjustments. Rules without hard violations express optimisation or derived-output conditions; stability is their termination check.

## Initial placement services

- [Initial tree placement](TreeSpacing.md)
- [Initial shared-parent alignment](SharedParentCentring.md)
