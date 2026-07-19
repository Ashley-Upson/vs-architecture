# StandardIo whole-project canonical assessment

Generated from `StandardIo.ArchitectureDiagram.sln` with the real deduplicated settings and an exact `--project` filter. No semantic graph was hand-edited.

| Project | Semantic nodes/links | Routed nodes/links | Families | Result | First blocker |
|---|---:|---:|---|---|---|
| StandardIo.ArchitectureDiagram.Cli | 2 / 0 | 1 / 0 | none | eligible, disconnected-only | none |
| StandardIo.ArchitectureDiagram.Core | 318 / 359 | 11 / 12 | 8 adjacent, 4 long downward | eligible | none |
| StandardIo.ArchitectureDiagram.Vsix | 14 / 3 | 12 / 2 | 2 adjacent downward | eligible | none |
| StandardIo.ArchitectureDiagram.Core.Tests, baseline | 25 / 12 | 25 / 12 | 12 adjacent downward | fallback | shared terminal segment |
| StandardIo.ArchitectureDiagram.Core.Tests, corrected | 25 / 12 | 25 / 12 | 12 adjacent downward | eligible | none |

The baseline Core.Tests defect involved:

- `edge_1cb953d5b1ed6e41`: `FixtureData -> GeneratedLogicalRoutes`;
- `edge_1a08e8a828c467d5`: `FixtureData -> DiagramSettings`.

Their horizontal intervals met at X=6088. Inverse horizontal-slot order made the first route's target entry overlap the second route's source exit between Y=152 and Y=164. The responsible owner was deterministic horizontal-slot allocation. The focused correction adds endpoint precedence without changing terminals, topology, columns, or final waypoints post hoc.

Final solution totals:

- projects assessed: 4;
- eligible projects: 4;
- fallback projects: 0;
- semantic nodes in eligible projects: 359;
- semantic links in eligible projects: 374;
- projects blocked by topology, placement, physical validation, or routing after correction: 0;
- disconnected-only projects: 1.

The next useful visual target is Core.Tests because it has 25 routed nodes in a broad single layer and exposes horizontal compactness and fan-out readability more strongly than Core. The next production decision is whether zero-link projects should count as selector-eligible or be reported as `NoRoutedRegion`; no fixture-specific selection is required.
