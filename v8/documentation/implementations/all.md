# All diagrams

[Implementations](readme.md)

## Dependency path

AllDiagramRenderer -> DiagramDocumentOrchestrationService -> DiagramTabService / DocumentCompilationService

## Conditions

Fan out to four diagram builders from the same compiler evidence. Each receives its own RenderModel and shared configuration. Assemble the selected format into one document. All is orchestration, not a fifth contextual diagram.
