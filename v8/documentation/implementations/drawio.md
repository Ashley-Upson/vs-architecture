# DrawIO

[Implementations](readme.md)

## Dependency path

DrawIODiagramRenderer -> DrawIOModelPreparationService -> DrawIOModelPreparationBroker -> RenderModelBuilderFactory; DrawIODocumentService

## Conditions

Produce mxfile/diagram geometry and routes from the same RenderModel. Disable grid and page view. Preserve editable nodes and edges. No layout rules in the writer.
