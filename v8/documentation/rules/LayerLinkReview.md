# Violation highlighting

[Catalogue](readme.md)

Only links with IsLayerViolation=true receive the red stroke. This rule makes no classification decision. The four classification conditions are separate services. Ordinary-link colouring applies only when this flag is false.

## Implementation

[LayerLinkReviewRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/LayerLinkReviewRuleProcessingService.cs)
