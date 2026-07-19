# Selected movement semantics: real closure result

This evidence follows the accepted return-column ownership and destination-column difference-constraint decisions.
The implementation remains available only through the opt-in development authority. Normal production does not execute
it and no provisional placement or route is emitted when the closure is rejected.

## Reproduction

```powershell
dotnet build src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-restore
dotnet run --project src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-build -- "C:\Users\Ash\Documents\ccoder\ccoder.ContentManagement\src\cCoder.ContentManagement\cCoder.ContentManagement.csproj" --settings "C:\Users\Ash\Documents\standard-io\artifacts\node-duplication\real-project-deduplicated-settings.json" --renderer drawio --development-common-authority-trial "C:\Users\Ash\Documents\standard-io\artifacts\pre-assignment-authority-trial"
```

The complete per-link ownership, candidate coordinates, blocker identities and solvability classifications are in
`artifacts/pre-assignment-authority-trial/trial-report.json` under
`preAssignmentMovement.returnColumnConstraintDetails`.

## Implemented semantics

- Return columns carry a stable ownership envelope derived from the smallest contiguous ordered project span containing
  both endpoints. Same-project, intervening-project, cross-project and root/canvas ownership fixtures are deterministic.
- Destination-aligned columns are represented by explicit column-to-envelope and column-to-column inequalities.
- The destination column remains attached to the destination midpoint. The solver compares coherent destination and
  blocker movement scopes rather than shifting a column independently.
- Difference constraints are persisted against immutable base placement. Reapplying an unchanged minimum cannot create
  an additional move.
- Generic subtree-separation proposals no longer stand in for destination-column or return-column constraints.

## Real result

The selected semantics remove every real destination-column obstacle:

| Measurement | Before | After |
| --- | ---: | ---: |
| Blocked destination columns | 9 | 0 |
| Persistent difference constraints | 0 | 29 |
| Persistent iterations | - | 7 |
| Provisionally moved nodes | 30 | 34 |
| Invalidated links | 72 | 84 |

The post-movement return evaluation finds 21 obstructed return demands. All 21 belong to the cCoder project's
`projects:0-0:layout-4` ownership envelope and all 21 are classified as
`OrderingInvariantInteriorBlocker`. Their left exteriors contain 66 blocker incidences and their right exteriors contain
463 blocker incidences. No left or right candidate is valid.

This count supersedes the earlier five-stub baseline. Clearing the destination columns and evaluating the complete
ownership-local return set exposes 21 affected links; it does not turn five previously valid return paths into invalid
production output because the development transaction is rejected atomically.

## Qualifying blocker

The remaining return problem has no horizontal order-preserving solution under the selected semantics.

For a same-project return, the valid column lies outside the complete project ownership envelope. A blocker lying
between an endpoint and the left exterior cannot be moved beyond the left exterior: moving that project-owned blocker
also expands or translates the ownership boundary. Moving it across the endpoint reverses positional order. The same
argument applies on the right. Every remaining real demand has at least one blocker on both sides.

Consequently, subtree, sibling-suffix, or whole-project horizontal movement can preserve ordering or clear one side, but
cannot make either ownership-local exterior reachable. Continuing through depth-2 allocation, regeneration, validation,
compaction, or obsolete-code deletion would consume provisional geometry from a closure that must roll back. Those
stages remain intentionally unexecuted.

The smallest remaining product decision is whether an exterior return may select a different vertical departure/arrival
stub slot before travelling horizontally, while preserving the chosen ownership-local column and positional ordering.
The alternative is to authorize positional reordering of owned subtrees. The vertical-slot option is recommended because
it preserves hierarchy and project ordering; it is a new route-topology semantic and is not implied by the accepted
horizontal movement rules.

## Production isolation and verification

- Atomic result: rolled back; zero routes regenerated into output.
- Production SHA-256: `08D70BBA59130F8D56EC4F411D3A5BB360B6FB1BBA800D5C43FE1A6386DAB7F6`.
- Focused ownership, difference-constraint, and solvability tests: 14/14 passing.
- Full suite: 402/402 passing.
- Release solution build: succeeded with the existing six VSIX dependency/analyzer warnings and no errors.
- Fresh-process normal benchmark: 14,549 / 14,887 / 15,023 ms minimum/median/maximum over five measured runs.
- Benchmark repeat hashes: one.
- VSIX packaged: no.
