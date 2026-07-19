# Pre-assignment positional movement authority

The follow-up implementation and real closure result for the selected ownership-local return and destination-column
difference semantics is recorded in [selected-semantics-result.md](selected-semantics-result.md).

This evidence records the opt-in development-authority run after introducing explicit bidirectional positional
constraints, complete project-owned inter-layer observation, immutable placement materialisation, and atomic movement
closure. Normal production does not execute this path.

## Reproduction

```powershell
dotnet build src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-restore
dotnet run --project src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-build -- "C:\Users\Ash\Documents\ccoder\ccoder.ContentManagement\src\cCoder.ContentManagement\cCoder.ContentManagement.csproj" --settings "C:\Users\Ash\Documents\standard-io\artifacts\node-duplication\real-project-deduplicated-settings.json" --renderer drawio --development-common-authority-trial "C:\Users\Ash\Documents\standard-io\artifacts\pre-assignment-authority-trial"
```

The generated `before.drawio`, `after.drawio`, and `trial-report.json` are retained under
`artifacts/pre-assignment-authority-trial`. Atomic rollback makes the two rendered layouts equivalent when the movement
component cannot close.

## Result

The trial proposed 122 persistent constraints in six connected positional components. All six components found
coherent movement scopes, moving 30 nodes and invalidating 72 links. Complete inter-layer observation increased locally
supported common routes from 255 to 274 and removed the previous missing-inter-layer classification.

The complete movement was rejected before execution because 14 invalidated paths remained unavailable after two
monotonic passes:

- nine destination-aligned vertical columns remained obstructed;
- five exterior return stubs remained obstructed.

No partial placement or provisional path escaped the development trial. The complete graph rolled back to 294 legacy
links and retained its 1,658 strict findings.

This qualifies as the plan's `no coherent positional movement can satisfy the real component` blocker. The remaining
obstacles are not missing movement scopes: every positional component reports solved. They expose a semantic limitation
in the current constraints. Subtree separation alone cannot express where a fixed destination connection must sit
relative to every obstacle over its vertical range, and a horizontal return stub to a canvas-exterior column cannot be
cleared by adding gaps while preserving the ordering of intervening project subtrees. Repeating the same minimum would
not increase a persistent constraint and is therefore rejected as a defect rather than treated as a repair loop.

The smallest decision needed before implementation can continue is whether return columns are project-local exterior
resources or canvas-global exterior resources, and whether destination-column clearance is solved as a column-to-envelope
difference constraint or remains represented as subtree-to-subtree separation. Those choices produce materially different
valid movement semantics.

## Production and verification

- Production SHA-256: `08D70BBA59130F8D56EC4F411D3A5BB360B6FB1BBA800D5C43FE1A6386DAB7F6`.
- Focused pre-assignment, return, and movement tests: passing.
- Full test suite: 394/394 passing.
- Release CLI build: passing with no warnings.
- Fresh-process normal benchmark: 14,174 / 14,270 / 14,538 ms minimum/median/maximum over five measured runs.
- Benchmark repeat hashes: one.
- VSIX packaged: no.
