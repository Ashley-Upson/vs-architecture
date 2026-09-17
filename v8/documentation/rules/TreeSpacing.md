# Initial tree placement

[Initial construction](initialization.md) | [Catalogue](readme.md)

Seed horizontal positions from dependency trees and descendant widths. This constructs the starting model once; it must not reset settled positions during iteration.

Implementation: [TreeSpacingLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/TreeSpacingLayoutRuleProcessingService.cs). The historical Rule suffix is retained, but this service is not registered in the repeated rule collection.
