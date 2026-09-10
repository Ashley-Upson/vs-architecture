# Layout convergence evidence — 10 September 2026

Evidence gathered against `21ada8c` on `codex/architecture-v8`. No production algorithms changed in this pass. The investigation used the previously extracted ContentManagement ProjectModel, bypassing Roslyn to isolate layout. These are diagnostic timings, not a controlled performance benchmark.

## A confirmed contradiction with four nodes

The real ContentManagement call graph contains this shape:

```text
PageRoleProcessingService (A) ──> PageRoleService (B)
           │                          │       │
           └──────> PageBroker (S) <───┘       │
                                              v
                                    PageRoleBroker (D)
```

Processing and foundation both call `PageBroker.GetAllPagesIgnoringFilters`; the foundation also calls PageRoleBroker CRUD methods. Source locations are `Services/Processings/PageRoleProcessingService.cs` and `Services/Foundations/Storages/PageRoleService.cs` in the ContentManagement library. This is a direct and indirect dependency, not a cycle.

The current placement rules require these centre coordinates:

1. A's only exclusively owned child is B, so centre(A) = centre(B).
2. B's only exclusively owned child is D, so centre(B) = centre(D).
3. S has parents A and B, so centre(S) = midpoint(A, B).
4. Depth puts S and D on the same row. They must have a 60px gap.

The first three requirements force S and D onto the same horizontal coordinate. The fourth forbids it. No number of iterations can satisfy all four simultaneously.

The real `LayoutModelBuilder` entry point reproduces this with four nodes and four links. The new `DirectAndIndirectDependencyLayoutTests.ShouldLayoutDirectAndIndirectCallsToTheSameBroker` fails after 100 passes with `Shared parent centring: SharedBroker`. The existing six-node shared-report regression still passes (focused run: one expected failure, one pass).

After 20 diagnostic passes, the small graph has centres A=370, B=370, D=370, S=130; S and D share row Y=380. Each full pass returns the same coordinates and the same validation error. Within that pass, shared-parent centring moves coordinates by up to 120px; branch spacing, parent centring and bounds then move them again. The rules are repeatedly undoing work.

## ContentManagement measurements

Counts include generated project/external containers and visible node occurrences after preparation.

| Mode | Containers | Nodes | Links | Width after pass 1 | Width after pass 20 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Deduplicated | 22 | 300 | 756 | 30,171px | 922,750,291,230px |
| With duplicates | 47 | 936 | 2,613 | 282,047px | 2,604,591,048px |

After pass 20, deduplicated mode still reports 54 shared-parent centring and 245 branch-spacing violations. Duplicated mode reports 105 shared-parent centring, 386 branch-spacing and 9 node-spacing violations. These runs diverge rather than gradually settling. The four-node contradiction is confirmed, but is not yet proven to explain every failure in these larger graphs.

Across the 20 passes, BranchSpacingLayoutRuleProcessingService consumed 17.85 seconds in deduplicated mode and 25.94 seconds with duplicates: about 92% of measured rule execution time in each. This excludes validation, extraction and other harness overhead. The three trace cases completed in 73 seconds overall. Trace tests collect observations; their passing status does not assert successful convergence.

## Recommended changes and locations

1. **Separate placement parents from displayed dependencies.** In this case A already reaches S through B. Retain A→S in the model and output, but let B determine S's placement together with D. Then B can centre over the outer edges of S and D, and A can centre over B. This is a proposed placement policy, not a change implemented by this investigation.
2. **Use the same placement relationships across all relevant rules and validation.** Start with `Services/Processings/Layout/LayoutGraph.cs`: `Parents`, `OwnedChildren`, `OwnedBranch`, `SharedGroups`, and `BranchGroups`. Review ParentCentring and SharedParentCentring consumers together with `Services/Foundations/Rendering/ProjectModelLayoutService.Validate`. Changing just one centring rule leaves the validator and ownership calculations disagreeing. Preserve full dependency information for routing; retain existing cycle handling and verify any reduced placement graph against depth requirements.
3. **After the four-node test passes, replay both complete graphs.** Identify any remaining contradictions independently; do not assume this resolves every cross-project clearance or branch-spacing interaction.
4. **Optimise the measured hotspot.** LayoutGraph repeatedly scans connections and resolves node IDs while rebuilding branch and ancestry information. Index nodes and adjacency once for stable topology, and reuse topology calculations where their inputs have not changed. Keep geometry-dependent calculations current. Measure again before broadening this work.
5. **Recognise stalled layouts.** The minimal graph has identical end-of-pass coordinates with unchanged violations. A repeated-state check could report this immediately instead of spending the remaining iteration budget. This improves diagnosis; it does not make an invalid layout valid.

Async alone cannot solve incompatible placement constraints. Parallel execution of the existing rules would also mutate shared coordinates concurrently. Independent extraction work may be a separate optimisation, but these measurements isolate a layout problem.

## Reproduction and retained evidence

Run the new regression with:

```powershell
dotnet test v8/StandardIo.ArchitectureDiagram.Core2.Tests --filter FullyQualifiedName~DirectAndIndirectDependencyLayoutTests
```

At the end of the initial evidence pass it remained red pending implementation. The pre-existing full suite had 220 passing tests at the baseline revision. See the implementation update below for the current state.

Temporary trace instrumentation has been removed from the checkout and saved with raw JSONL rule timings, final model snapshots and the regression output in `%TEMP%/v8-convergence-evidence`. Its cached input is `%TEMP%/v8-layout-investigation/content-model.json`. These local evidence files are not checked into Git. The retained repository changes are this report and the small failing regression test. Existing diagram output and production sources are unchanged.

## Implementation update — sample improved; ContentManagement unresolved

The subsequent implementation pass retained these corrections:

- Placement relationships omit redundant ancestor calls while retaining every displayed dependency. Entry links into cycles are preserved.
- Shared centring reads current positions after earlier groups move and distributes translations by group size, keeping each group's internal spacing together.
- Branch spacing uses a consistent ownership order. An internal shared branch cannot reverse its owning tree's order against another tree. Independent trees move as units when separating them.
- Node lookups are indexed and branch calculations are reused within a spacing pass.
- Obstructed connection exits route around intervening nodes. The former cross-project clearance rule, which could move a source together with the very descendants obstructing it, was removed. Destination bus sharing is retained.

The destination-bus test now identifies the final horizontal segment rather than assuming every route has four points. Its expected bus height and shared arrival point remain unchanged.

Original sample files are backed up in:

```text
C:\Users\ward_\AppData\Local\Temp\v8-placement-backup-20260910-193747
```

All four sample files were regenerated through the CLI, using the sample project pair and `--colour-lines`, with and without `--noduplicates`.

| Sample mode | Original width | Updated width | Height | Nodes | Connections |
| --- | ---: | ---: | ---: | ---: | ---: |
| Deduplicated | 6,099.93px | 5,619.94px | 1,300px | 52 | 62 |
| With duplicates | 8,356.36px | 7,863.53px | 1,300px | 87 | 107 |

Heights, node counts and connection counts are unchanged. Widths decreased by approximately 7.9% and 5.9%. Automated viewing of the open local-file browser tab was blocked by the browser URL policy; visual confirmation was requested from the user. The comparison above is an artifact/geometry comparison, not a claimed browser inspection.

Final full test run for this checkpoint: **229 passed, one remaining regression failed, 230 total**, approximately 18 seconds. Existing analyzer warnings remain enabled. The failed regression is `ConnectedSharedBranchesTests.ShouldSettleAConnectedGraphOfSharedServices`: a connected fourteen-node/thirteen-link reduction of ContentManagement that still fails centring/spacing validation after 1,000 passes. It remains active, not skipped or weakened.

The direct/indirect-call, stale-position, branch-ordering, seventeen-node interleaving, cycle-entry and cross-project obstacle regressions pass. Full ContentManagement still does not converge; no successful ContentManagement diagram is claimed.

An experimental neighbour-based row-ordering pass made the fourteen-node test pass but regressed an existing cycle test and worsened the full graph. It was reverted. Temporary simultaneous-constraint experiments also did not establish feasibility for the full graph within their time limits. A timeout is not proof that the rules are impossible to satisfy. No external solver dependency was added to the application.

The outstanding design decision is whether to continue preserving exact shared-parent centring through coordinated constraint solving, or make shared centring a preference subordinate to non-overlapping branches and reasonably direct connections. The latter changes an agreed requirement and has not been implemented.

Further temporary C# harnesses are preserved in `%TEMP%/v8-placement-retained-evidence`; graph reductions and independent diagnostic scripts are in `%TEMP%/v8-layout-investigation`. They have been removed from the checkout. Production changes, sample artifacts and active regression tests remain reviewable in the shared branch.
