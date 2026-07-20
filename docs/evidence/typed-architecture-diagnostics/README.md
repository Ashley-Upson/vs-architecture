# Typed Architecture diagnostics consolidation

## Execution-path audit

Normal Draw.io generation, strict validation, diagnostic export, manifest output,
serialization-repeat verification, imported Architecture snapshots, and development
project-region generation now enter `IArchitectureGenerationService`. Analysis and
rendering occur once. Serialization repeat composes the already-rendered `DrawioPage`
again and does not repeat Roslyn analysis or rendering.

The CLI and VSIX command handlers have no dependency on
`DeterministicDrawioExporter`, `IDeterministicDrawioExporter`, or
`DrawioGenerationResult`. `DrawioGenerationResult` and
`IDeterministicDrawioExporter` remain public compatibility contracts for
`IDiagramRenderer` and existing consumers. Their implementation is a thin projection
over `GenerateArchitectureResult`; they are not a second routing implementation.
Removing them is an externally visible API break and remains approval-gated.

The development project-region compatibility result remains a projection boundary
inside `DeterministicDrawioExporter`. It performs one project-region render and is
immediately projected into `ArchitectureRenderResult`; there is no repeated analysis,
rendering, or geometry compilation.

## Bounded cCoder evaluation

Scope is unchanged from the accepted semantic-scope evaluation:

```text
Project: C:\Users\Ash\Documents\ccoder\ccoder.ContentManagement\src\cCoder.ContentManagement\cCoder.ContentManagement.csproj
Settings: docs\evidence\semantic-scope\ccoder-template-region.settings.json
Root: ^cCoder\.ContentManagement\.Exposures\.Controllers\.TemplateController$
Selected semantic nodes: 63
Selected semantic links: 45
Node duplication: disabled
```

Reproduction command:

```powershell
dotnet run --project src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-build -- C:\Users\Ash\Documents\ccoder\ccoder.ContentManagement\src\cCoder.ContentManagement\cCoder.ContentManagement.csproj --settings docs\evidence\semantic-scope\ccoder-template-region.settings.json --renderer drawio --diagram-types architecture --development-project-region artifacts\typed-architecture-diagnostics\ccoder-cycle-2 --serialization-repeat 2
```

The typed baseline reproduced the accepted three findings exactly:

- 12 px shared segment and two perpendicular endpoint contacts between
  `edge_03570cad684f6255` and `edge_7417dd23897207dc`;
- 6 px parallel-spacing deficit between `edge_dacaf40202d15a07` and
  `edge_510ee36846fa7ff4`.

The first pair existed because normalization retained horizontal intervals but lost
whether their touching endpoint was a source departure or target arrival. The generic
touching-interval order put the arrival lane above the departure lane. Explicit endpoint
roles now make departure-before-arrival the canonical order.

The remaining deficit was the same ownership gap over a 6 px endpoint overlap rather
than exact contact. Endpoint precedence is now applied within configured lane clearance.
When both directions would impose a constraint, deterministic interval ordering remains
the fallback, avoiding a precedence cycle.

Final evidence:

```text
Eligible:                 yes
Rendered nodes:           32
Rendered links:           44
AdjacentDownward:         39
LongDownward:             5
InterLayers:              8
Slot demands/assignments: 49 / 49
Destination columns:      5
Return columns:           0
Logical findings:         0
Physical findings:        0
Node overlaps:            0
Project bounds:           x=540 y=6 width=5590 height=1386
Total route length:       52702 px
Maximum route length:     4081 px
Bends:                    95
Route points:             183
Draw.io SHA-256:          577c53bf92d888f1d40caca7a4121f17018f79bff0679e8b3814a022e2eefc5d
```

Generated artifacts are ignored and live under:

```text
artifacts\typed-architecture-diagnostics\ccoder-baseline
artifacts\typed-architecture-diagnostics\ccoder-cycle-1
artifacts\typed-architecture-diagnostics\ccoder-cycle-2
```

## VSIX commands

The VSIX now exposes:

- `Generate Architecture Diagram` -> `<target>.architecture.drawio`;
- `Generate Data Model Diagram` -> `<target>.data-model.drawio`;
- `Generate Diagrams` -> `<target>.diagrams.drawio`, Architecture then Data Model.

All three commands use `ITypedDiagramGenerationOrchestrator`, the existing solution or
project selection resolution, one composer invocation, and `FailAll`. Existing legacy
settings are explicitly adapted to their typed owners. Root-discovery settings affect
Architecture analysis only. Namespace/name exclusions still map to both analysers for
saved-settings compatibility.

A combined selection dialog is deferred. The VSIX has no existing dialog/view-model
infrastructure, while the approved explicit combined command provides both jobs without
adding a new UI dependency. Folder and solution-folder command placements are retained,
but target resolution remains limited to the solution or a resolvable selected C# project;
the current command infrastructure does not reliably enumerate arbitrary folder subsets.

No VSIX was packaged in this tranche.
