# Layout rule interaction review

## Findings

The branch regression was a constraint conflict, not an extraction problem.
Individual node spacing and parent centring could both pass while descendants
from different orchestration branches interleaved. The missing invariant was
separation of the whole owned branch.

Three interactions were demonstrated during the regression work:

1. Shared-parent centring moved a parent service alone. Its foundation/processing
   ancestors then followed it on later passes, stretching the branch into neighbours.
2. Cross-project clearance moved a leaf alone, creating the same problem.
3. Flat row spacing and branch spacing made competing corrections. The former
   followed the current row order; the latter translated whole subtrees. Keeping
   both prevented the two-project regression from converging within 1,000 passes.
   Removing the flat row correction allowed the branch constraints to converge.

Moving an entire orchestration rigidly is also too restrictive: event and storage
sub-branches may need different spacing to accommodate different shared consumers.
The correction unit is therefore a straight owned chain, stopping at the next
branching parent or shared boundary. Translate that chain's subtree together.

## Shared definitions

- Owned child: a lower-row child with exactly one distinct local parent.
- Owned branch: a root and its recursively owned descendants. Shared destinations
  start independent branches and are not claimed by their consumers.
- Branch bounds: the outer edges of all nodes in that owned branch.
- Centred: the parent's centre equals the midpoint of the outer edges of its
  immediate owned children. This does not require equal distances between child
  centres; grandchildren can require different branch widths.
- Separation: 60 px minimum between competing branch intervals. Independent
  branches compete horizontally when their vertical extents overlap.

## Ordered pass

| Rule | Reads / writes | Interaction contract |
|---|---|---|
| Depth | Relationships / node Y | Resolve complete chain depth before horizontal ownership traversal. Cycle-closing edges retain the existing exception. |
| Initial tree spacing | Graph / initial X | Runs once. Must not restore seed coordinates on later passes. |
| Shared-parent centring | Shared parent groups / owned chain subtrees and shared child subtrees | Preserve straight-chain geometry; branch widths may change and must be reconciled below. |
| Cross-project clearance | Lower obstacles / source's owned chain subtree | Reserve an exit column without pulling the source away from its straight chain. Later spacing/centring may require another pass. |
| Branch spacing | Recursive branch extents / whole owned subtrees | Process deepest sibling groups first, then independent root branches. Descendant space propagates upward. Replaces flat row spacing in the node pipeline. |
| Parent centring | Final child-group edges / parent X | Run deepest-first after branch spacing, so parents respond to descendant requirements rather than stale positions. |
| Bounds | Final nodes / text coordinates and box dimensions | Translate a whole container's contents uniformly. Preserve relative placement. |
| Project positioning | Project dependency graph and box extents / project X and Y | Reuse depth, shared-parent, branch-spacing and parent-centring rules in that order. Seed rows once using actual box widths; use actual box heights for rows. |
| Local routing | Settled local geometry / local connection points | Does not move nodes. |
| Cross-project routing | Settled absolute geometry / cross-project points | Uses the same destination-channel router; does not move nodes or boxes. |

DI enumeration order is intentional and is defined in ServiceCollectionExtensions.
NodeSpacingLayoutRuleProcessingService is no longer registered in the layout loop;
branch separation also provides the required individual node spacing. Its former
independent pass must not be reintroduced alongside branch spacing.

## Recursion and convergence

Recursion happens inside ownership traversal and deepest-first sibling processing.
The outer bounded loop reconciles shared graph constraints between branches.
Repeating passes is not a substitute for compatible movement units or ordered
processing. Increasing the iteration limit was not used to address this defect.

Validation checks node spacing, branch spacing, parent centring, shared-group
centring, depth and containment together after a full pass. It also checks the
project graph using the same geometric constraints. A correction that breaks one
of these conditions requires another pass; reaching the limit remains an error.

The real sample regression asserts entire orchestration branch intervals, for both
one-project and two-project rendering. It then runs layout again and requires node
positions to remain within 0.1 px of the accepted result. Existing cross-project
regressions still check direct gutter routes, node clearance and both project orders.

## Limits of the evidence

Branch competition uses actual simultaneous vertical overlap, not transitive overlap through a third branch.

These checks establish compatibility for the tested graphs, including the existing
400-node fixture. They are not a mathematical convergence proof for arbitrary DAGs
or cycles. The runtime validator does not yet constitute a general edge-crossing
or obstacle-avoidance solver. Shared dependency links are intentionally allowed to
join branches; owned child branches must keep their reserved space.
