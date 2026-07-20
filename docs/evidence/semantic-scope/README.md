# Renderer-independent semantic scope evidence

## Architecture correction

Semantic selection now runs once in `SemanticScopeSelector`, after Roslyn discovery and before any renderer. `DiagramModel.Metadata.SemanticSelection` is the renderer-independent contract: it records the scope policy, ordered pattern definitions, matched roots and provenance, selected and omitted node IDs, selected and omitted link IDs, and unmatched pattern indexes. The JSON renderer serializes this same pre-Draw.io model.

Patterns match the analyser's fully qualified semantic name (`TypeNode.FullName` / `ExternalDependencyNode.FullName`). The value includes its namespace and Roslyn's stable nested/generic notation as supplied by the analyser; it is not project-qualified and is never a display label. Matching is case-sensitive, culture-invariant, and bounded by a 250 ms timeout. Internal types, external nodes, and data-model types are matchable. Data models are retained for the existing shared data-model presentation even when they are not reachable architecture nodes.

Empty pattern text means `FullSelectedInput`. Configured roots use outgoing dependency reachability. Root order is pattern declaration order followed by canonical-name and ID ordering; duplicate matches retain first-pattern provenance. Links and retained nodes have ordinal stable ordering. Renderer visual ordering remains a separate concern.

## Removed implicit policy audit

| Former behavior | Former owner/caller | Input/effect | Disposition |
| --- | --- | --- | --- |
| `.Exposures.` and `*Controller` preferred roots | `RenderGraph.BuildExposureTreeGraph` and `BuildCanonicalExposureGraph`, called by `RenderGraph.From` | Replaced ordinary zero-incoming roots after renderer graph construction | Deleted; available only as explicit regex configuration |
| zero-incoming fallback | same methods | Substituted roots when no preferred names existed | Deleted; empty configuration retains full analyser input |
| exposure layout threshold as semantic switch | `RenderGraph.From` | Large graphs were reduced while small graphs retained all nodes | Deleted from semantic selection; threshold remains a visual/layout policy only |
| renderer-side root ordering | same methods | Re-derived semantic priority from names and render-node order | Deleted; renderer consumes metadata root order |

Repository guard tests assert that the Draw.io semantic-preparation source contains no exposure/controller convention, regex use, or parser dependency.

## Input/source model

- Single project: direct `.csproj` input.
- Solution: `.sln` input, with all selected projects represented.
- Multiple projects: solution or folder discovery; analyser accepts the resulting ordered project list.
- Folder/subtree: recursive `.csproj` discovery when the folder has no single top-level solution.
- Project filter: selects one project by name/path from a solution/folder. A first-class arbitrary multi-project filter expression is not yet exposed by the CLI.

## StandardIo Core accounting

Command:

```text
dotnet run --project src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-build -- src\StandardIo.ArchitectureDiagram.Core\StandardIo.ArchitectureDiagram.Core.csproj --settings docs\evidence\semantic-scope\explicit-exposure-roots.settings.json --renderer json --output artifacts\semantic-scope\standardio-core-selection.json
```

- Discovered semantic nodes: 322.
- Selected architecture nodes: 12 (explicit root reachability).
- Rendered data-model nodes: 18.
- Explicitly excluded nodes: 292.
- Unsupported/dropped/unaccounted nodes: 0.
- Discovered semantic links: 366.
- Selected architecture links: 12.
- Explicitly excluded links: 354.
- Unsupported/dropped/unaccounted links: 0.

The explicit compatibility patterns are `\.Exposures\.` followed by `Controller$`. They matched `DiagramGenerationExposure` and `IDiagramGenerationExposure`. The canonical renderer still produced 11 render nodes, 12 render links, and the same 8 adjacent/4 long topology-family split. The Draw.io SHA changed from `537E0A40...` to `BF7E520A...` because the explicit analyser scope now supplies 18 rather than the former full-input data-model population; this is a declared semantic-input/data-model presentation delta, not a route-selection regression. The project-region result remained eligible.

## Bounded cCoder generation

Input: `C:\Users\Ash\Documents\ccoder\ccoder.ContentManagement\src\cCoder.ContentManagement\cCoder.ContentManagement.csproj`

Root pattern:

```text
^cCoder\.ContentManagement\.Exposures\.Controllers\.TemplateController$
```

This root was chosen because it is a coherent Template rendering entry point, not to manufacture a target count. It matched one root (`type_4cf34ced23f2fb3b`) and selected 63 nodes/45 links by outgoing reachability. It omitted 330 nodes/295 links explicitly. The renderer-independent snapshot is `artifacts/semantic-scope/ccoder-template-selection.json`.

Canonical project-region command:

```text
dotnet run --project src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-build -- C:\Users\Ash\Documents\ccoder\ccoder.ContentManagement\src\cCoder.ContentManagement\cCoder.ContentManagement.csproj --settings docs\evidence\semantic-scope\ccoder-template-region.settings.json --renderer drawio --development-project-region artifacts\semantic-scope\ccoder-template-region --performance-output artifacts\semantic-scope\ccoder-template-performance.json
```

Results:

- Render nodes/links: 31/42 (interfaces and shared presentation classification account for the semantic/render delta).
- Topologies: 38 adjacent-downward, 4 long-downward.
- InterLayers: 8; slot demands/assignments: 46/46; expanded InterLayers: 2.
- Destination/return columns: 4/0.
- Logical routes: 42; logical interior waypoints: 89; maximum bends: 4.
- Project bounds: 4,956 x 1,386 px; serialized cells: 341; `mxPoint` elements: 104.
- Canonical authority: topology selector, deterministic slot allocator, vertical/return column allocators, and project InterLayer compiler. Legacy candidate selection was not invoked by the project-region path and no topology replacement/repair mutation remained.
- Eligibility: false. Findings are one 12 px shared segment, one two-point perpendicular crossing on the same route pair, and one parallel-spacing deficit of 6 px. These project-region renderer findings are preserved as the next concrete visual/canonical-owner targets; semantic patterns were not changed to hide them.
- Physical findings mirror those three logical findings. No separate unowned-gap finding was reported.
- End-to-end timed run: 5,481 ms.
- SHA-256: `05BCD3D28452120ED1312B45B21BE5F53916431A3532FD5AF4E83B358A215CF9`, identical on a second complete analysis/render run.

Artifacts are under `artifacts/semantic-scope/` (ignored generated evidence); the reusable settings and this report are checked in under `docs/evidence/semantic-scope/`.

## Compatibility and limitations

Old settings files load with an empty root-pattern value and therefore select the full input—there is no migrated hidden convention. Invalid patterns fail on import and export with source line and text. The VSIX exposes a multiline editor. No VSIX was packaged.

The current omission report is exact by ID and policy, but does not yet assign every omitted ID to finer categories such as interface versus unconnected in the serialized contract. Those categories can be derived from the full discovery snapshot but would require a richer omission-reason model. Arbitrary multiple-project selection is representable by the analyser but lacks a dedicated CLI list syntax.
