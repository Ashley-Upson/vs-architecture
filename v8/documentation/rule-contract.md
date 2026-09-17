# Rule execution contract

## Completion

Initial model construction happens before iteration. For each pass, snapshot meaningful model contents, execute each registered rule, and compare contents again. Complete only when the model is unchanged and all conditions are satisfied. New object instances containing identical values are not changes. Coordinate comparisons use six decimal places; this is finer than the 0.01 geometry clearance tolerance. Check after each rule as well as each pass so two cancelling adjustments cannot masquerade as completion.

An iteration limit or repeated non-final state is a failure, with diagnostics naming changing rules. A stable model with unmet conditions fails immediately and names the responsible conditions. Never return an unfinished model as success. Do not compare iteration counters or performance measurements.

## One service, one condition

Each rule documents applicability, condition, allowed changes, and verification. If satisfied, leave the model unchanged. Helpers may calculate a solution, but must not execute a hidden second rules engine. A rule must not silently accept an unmet condition. Mutually exclusive colour conditions avoid overwriting one another.

Initial construction is not a rule to replay. External operations such as reading files and writing documents are not repeated until equal: their in-memory model transformations are the relevant boundary.

## Geometric goals

Preserve dependency truth, node identity, project ownership and diagram-specific selection. Keep nodes clear, parents above children except cycle-closing edges, and routing passages usable. Optimisation must not undo these conditions. Where exact centring is infeasible because of shared dependencies or reserved passages, document the exception rather than oscillate.

## Migration status

All four render-model builders finish through the common engine. Architecture has the detailed flat positioning, routing and styling rules; contextual initial-layout algorithms remain in their respective builders. Compiler extraction and contextual projection are currently direct pipelines, not flat iterative rule collections. Their stage documents describe the actual code and the intended boundary; they must not be mistaken for already migrated implementations.
