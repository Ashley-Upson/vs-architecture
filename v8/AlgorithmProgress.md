# Algorithm 2: implementation review

> Rendering update: the Architecture generator now returns Draw.io bytes through the
> builder/splitter/renderer service chain. See [Rendering.md](Rendering.md).
> Earlier statements below describing generation as unimplemented are historical.


## Latest checkpoint: concrete call chains

Interface calls now resolve to all available nonabstract source implementations,
including explicit implementations and overrides. Public contract methods are
included in method lists; non-public ordinary helpers are hidden and their reachable
calls are attributed to the public/contract method that invokes them.

All 72 tests pass. The reviewed sample remains 94 types and 235 dependencies, but
its local interface call endpoints now point at concrete methods. Splitting yields
five complete manager trees, a DI registration helper tree and one leftovers model
for the six data/context types. Shared dependencies are copied into each tree as
requested. See [Extraction.md](Extraction.md) and [Splitting.md](Splitting.md).

The review below records the earlier baseline; its interface-target, 42-root and
shared-node-policy observations have been superseded by these implemented decisions.
Cross-project reconciliation, layout, routing and Draw.io output remain pending.


Reviewed 9 September 2026 against the v8 code, the sample's ExpectedModel.json,
and the original architecture-algorithm-2-proposal.md in the parent folder.
Later user decisions take precedence over provisional assumptions in that proposal.
This review records status and remaining decisions; it does not implement new stages.

## Update after this review

ProjectModelSplitter has since implemented the user-defined root and splitting rules.
Shared dependencies are explicitly repeated, and interfaces remain dependency targets
without implementation resolution. The school sample yields 42 roots plus one leftovers
model. See [Splitting.md](Splitting.md) for the implemented contract and 65-test status.
The comparison below records the earlier checkpoint; its proposed interface-resolution
prerequisite is superseded by this decision. Layout and export remain pending.

## Position at the original review

The single-project source-model builder is implemented and tested. The complete
multi-project source stage is not finished. Tree construction, placement, routing
and Draw.io export have not been implemented in v8.

| Algorithm step | Current implementation | Status |
|---|---|---|
| Read one project into a source model | ProjectModelBuilder.BuildAsync -> ProjectModelOrchestrationService -> path/types/dependencies stacks | Implemented for the agreed folder-loader contract |
| Combine selected projects | Builder handles one path; no combined ownership index or boundary reconciliation | Pending |
| Save/load source datasets for repeatable layout | Plain models, JSON round-trip checks and a checked-in expected sample snapshot; no production save/load API | Partial |
| Select relationships and find roots | No diagram-specific graph preparation, root selection or business-type filtering | Pending |
| Build trees | No expansion, shared-node placement policy or display cycle handling | Pending |
| Arrange a tree | No node measurements, subtree widths, positions or tree bounds | Pending |
| Pack project boxes | No project rectangles, tree packing or translations | Pending |
| Arrange projects | No project dependency graph or project-box placement | Pending |
| Route connections | No centre attachments, gutters, bend calculation or obstacle checks | Pending |
| Separate links | Deliberately a later increment | Pending |
| Validate geometry and export Draw.io | No geometry or exporter | Pending |

DiagramCLI accepts multiple project paths, but calls DiagramGenerator, which still
throws NotImplementedException. It does not currently invoke ProjectModelBuilder.
The public builder can be called directly from application code and acceptance tests.

## What the source builder delivers

1. Resolve a project path, folder, or another file's containing folder.
2. Create one ProjectModel with name, absolute project path and empty arrays.
3. Pass that same object to the type stack to populate Types.
4. Pass it to the dependency stack to populate Dependencies.
5. Return that object with no Roslyn state attached.

Each foundation loads its own compilation. Roslyn symbols remain behind foundation
contracts; processing services deal with domain models. The source loader follows
cCoder.CodeAnalysis's folder scan, common implicit usings and available runtime/build
output references. It does not evaluate MSBuild compile items, imports or conditions.

The public builder's sample acceptance checks the complete expected model from both
project and directory paths: 94 types (89 internal, 5 external) and 235 dependencies
(194 Consumed, 41 Inheritance). The latest test run passed all 50 tests. These tests
prove the extraction contract, not layout quality or completeness of a future diagram.

## Changes from the original proposal that are intentional

- The original suggested flat project/type/relationship IDs. The later user-defined
  contract is ProjectModel[] with nested Types and Dependencies, using full .NET names.
  No additional IDs, source locations or geometry have been introduced.
- Relationship kinds are now explicitly Inheritance and Consumed. Consumed currently
  means explicit method invocation; it does not mean every possible dependency.
- Source loading uses the cCoder.CodeAnalysis folder approach rather than project
  evaluation. This is a chosen input contract.
- The initial model service accepts one path. Combining models is a distinct next step.
- Compilations are loaded independently by the two foundations rather than passed
  between services. Only ProjectModel is shared.

## The next algorithmic gap: source relationships versus display connections

The intended school stack is:

```text
SchoolManager -> SchoolOrchestrationService -> SchoolProcessingService -> ...
```

The extracted facts correctly say:

```text
SchoolManager --Consumed--> ISchoolOrchestrationService
SchoolOrchestrationService --Inheritance--> ISchoolOrchestrationService
```

There is no directed call edge from the interface to its implementation. Following
outgoing source relationships from SchoolManager stops at the interface. The concrete
orchestration remains a separate zero-incoming candidate.

Counting internal types with outgoing links but no incoming links across both extracted
relationship kinds yields 42 candidates in the sample. Six internal types are isolated
under those same relationships. These counts are a direct calculation from the fixed
sample model, not results of an implemented root-selection service. With Consumed alone,
there are 41 candidates and 12 isolated types.

Before identifying architectural roots, define a display-graph step that can:

- Collapse repeated method-level calls into type-level connections while retaining facts.
- Use implementation relationships to associate interfaces with implementing classes.
- Decide whether to display an interface, its implementation, or a grouped representation.
- Treat multiple implementations explicitly rather than guessing runtime DI selection.
- Exclude or separately represent composition helpers, model types and external boundaries.

A unique implementation within the selected dataset is useful evidence for a provisional
architecture view, but is not proof of runtime DI dispatch. Do not blindly reverse all
Inheritance edges: class inheritance and interface implementation are different cases.
The original source model can remain intact while the display graph makes these choices.

## Other gaps exposed by the sample

### Multiple project ownership

IsInternal currently means defined in the single source folder being analysed. A referenced
assembly is an external boundary even if another separately built ProjectModel later
contains its source definition. A combined dataset must resolve these names to their owning
projects before counting roots or cross-project links. Ambiguous same-named definitions
need an explicit outcome because string names do not include assembly qualification.

### Data and construction relationships

The six isolated model/context types are not evidence that extraction failed. They have
properties and collections, but those are not Consumed edges. A Data diagram needs to
interpret Field.Type and Property.Type, including arrays/generics, as its own relationship
selection. That projection is not implemented.

SchoolFactory.Create uses new SchoolDataContext(). Object creation is not currently a
Consumed relationship, so the intended factory-to-context connection is also absent from
the call graph. Decide whether that belongs in a data view or needs additional extraction.
Do not infer that connection merely from a naming convention.

### Eventing

Broker calls to IEventHub are recorded. Named event channels and publisher-to-subscriber
relationships are not. The source model has no event-name/payload-routing fields, and the
sample's listeners are in its tests. Drawing an event service stack is possible from calls;
drawing the event delivery graph would be a separate requirement.

### Shared nodes and cycles

The sample shares ISchoolFactory, IEventHub and List<T> across its stacks. Consequently,
independent tree layout alone does not settle where these shared types should appear.
The original proposal's repeated-subtree policy was explicitly provisional, not approved.
The competing requirement to draw each type once still needs a display policy.

Extraction handles cyclic calls without recursively expanding indefinitely. It does not
yet choose a display root for a cyclic group or route cycle connections.

## Suggested next increment

Before writing coordinates, build and test the domain-only graph preparation stage:

1. Combine the selected ProjectModels and resolve project ownership.
2. Choose Architecture or Data relationships explicitly.
3. Define interface/implementation presentation and ambiguous implementation behaviour.
4. Assert the intended roots and connections using small fixtures and the school sample.
5. Decide the shared-dependency display policy, then build the trees.

After that, implement the original first visual increment: one parent and one child with
aligned centres and a straight link, followed by uneven branching trees. Project packing,
cross-project routing and link separation remain later increments.

