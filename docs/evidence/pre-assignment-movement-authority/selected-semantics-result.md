# Allocated return slots: real closure result

This evidence records the opt-in development-authority result after return departure and arrival segments were integrated
with the canonical deterministic InterLayer slot allocator. Normal production does not execute this path. Rejected trial
geometry remains atomic and is not emitted.

## Reproduction

```powershell
dotnet build src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release
dotnet run --project src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-build -- "C:\Users\Ash\Documents\ccoder\ccoder.ContentManagement\src\cCoder.ContentManagement\cCoder.ContentManagement.csproj" --settings "C:\Users\Ash\Documents\standard-io\artifacts\node-duplication\real-project-deduplicated-settings.json" --renderer drawio --development-common-authority-trial "C:\Users\Ash\Documents\standard-io\artifacts\pre-assignment-authority-trial"
```

The generated `before.drawio`, atomically rolled-back `after.drawio`, and complete `trial-report.json` are written under
`artifacts/pre-assignment-authority-trial`.

## Implemented semantics

- A return retains one departure horizontal, one ownership-local exterior column, and one arrival horizontal.
- Departure and arrival are ordinary `LinkSegmentDemand` records with `ReturnDeparture` and `ReturnArrival` roles.
- Same-layer departure and arrival use different InterLayers. Upward routes use the InterLayers immediately below the
  source and above the destination.
- Return and ordinary horizontal demands share conflict grouping, deterministic lowest-slot allocation, exact persistent
  InterLayer height proposals, vertical suffix materialisation, invalidation, and changed-interval reassignment.
- Project-owned external nodes participate in layer bounds and move with their owning suffix.
- No side terminal, extra horizontal segment, free-form Y adjustment, positional reorder, or legacy repair was added.

## Real return result

| Measurement | Result |
| --- | ---: |
| Same-layer returns | 32 |
| Upward returns | 42 |
| Departure demands | 74 |
| Arrival demands | 74 |
| Distinct departure InterLayers | 7 |
| Distinct arrival InterLayers | 5 |
| Slot regions | 8 |
| Assigned return segments | 148 |
| Assigned exterior columns | 74 |
| Valid return assignments | 74/74 |
| Remaining return blockers | 0 |
| Persistent InterLayer constraints | 8 |
| Vertical materialisation iterations | 11 |

This closes the former 21 ordering-invariant fixed-Y blockers. Both existing-slot reuse and exact InterLayer expansion are
exercised by focused fixtures; the real report retains the final persistent height requirements for depths 1 through 8.

## New qualifying blocker

Full real closure cannot reach a finite horizontal fixed point. The return slot allocation itself is complete, but the
reprojected destination-column constraints expose a positive three-subtree cycle in the large cCoder project:

```text
layout subtree ..._4_type_9a3beb03... must move right to clear ..._6_type_a0340b84...
layout subtree ..._5_type_a9f0314f... must move right to clear ..._4_type_9a3beb03...
layout subtree ..._6_type_a0340b84... must move right to clear ..._5_type_a9f0314f...
```

A diagnostic run raised the iteration bound from 16 to 128 without changing semantics. After initial propagation, the
three requirements repeated in order and each three-iteration circuit increased all required coordinates by 7 pixels.
Representative final requirements were X >= 29306, X >= 29811, and X >= 29192. Therefore no finite monotonic horizontal
placement satisfies the selected directions. The diagnostic bound and telemetry were removed after establishing the
cycle; they are not production changes.

Trying deterministic opposing directions removed the initial reciprocal pair but did not solve the larger dependency
graph. The subsequent component-alternative tranche retained every complete hierarchy-preserving destination, blocker,
sibling-prefix/suffix and project-prefix/suffix choice produced for the real conflicts. Formal SCC analysis replaced the
one-off opposing-direction restart and mutual-pair diagnostic.

The immutable real placement produces 16 connected positional conflicts. Every conflict has multiple valid alternatives:
131 directed alternative edges form 52 cycle-focused selections. Formal analysis rejects all 52 selections and finds no
complete acyclic solution. The final rejected selection contains a two-scope positive SCC with total weight 2,575.

This is the complete proof available without positional reordering. The search includes bundled blocker scopes that move
all blockers for a destination column coherently. Broader scopes excluded by alternative production contain unsupported
incident links and cannot form a complete atomic movement closure. Return exterior-side selection cannot change the
result because the contradiction exists before return assignment; all 74 returns remain independently assignable.

Positional reordering remains prohibited. The next transition requires common-authority support for every incident link
in one of the currently incomplete broader movement scopes, or explicit approval to change established positional order.

The normal 16-iteration run consequently reports one destination conflict, three vertical-column obstacles, no return
stub obstacle, non-converged horizontal and changed-interval closure, 79 invalidated links, and zero regenerated output
routes. The component is rejected before execution; all findings remain identical to production.

## Production isolation

- Development authority: opt-in only.
- Atomic result: rolled back.
- Routes emitted after rollback: 0.
- Alternative selections evaluated/rejected: 52/52.
- Complete acyclic solutions: 0.
- Required production SHA-256: `08D70BBA59130F8D56EC4F411D3A5BB360B6FB1BBA800D5C43FE1A6386DAB7F6`.
- VSIX packaged: no.
