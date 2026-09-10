# Project model splitting

> Rendering update: the Architecture generator now returns Draw.io bytes through the
> builder/splitter/renderer service chain. See [Rendering.md](Rendering.md).
> Earlier statements below describing generation as unimplemented are historical.


## Exposure and stack

```csharp
ProjectModel model = await new ProjectModelBuilder().BuildAsync(projectFilePath);
ProjectModel[] trees = new ProjectModelSplitter().Split(model);
```

```text
ProjectModelSplitter (public exposure: delegates only)
  ProjectModelSplitterProcessingService (internal: splitting algorithm)
    ProjectModelService (internal: creates independent copies)
```

All service interfaces remain internal. Splitting is synchronous, in memory and
independent of Roslyn, file loading, coordinates or rendering.

## Algorithm

1. Index the input types and outgoing dependencies by full type name.
2. Determine roots from the original input, before processing any tree:
   - The type has at least one declared method.
   - No different type has a dependency targeting it.
   - Both Consumed and Inheritance relationships count, including interfaces.
   - A self-reference does not disqualify a root: it is not another type.
3. For each root, in input type order:
   - Start a fresh visited set and work queue containing that root.
   - Add the next type and enqueue all its outgoing dependency targets.
   - Skip types already visited in this result, preventing cycles and repeated
     expansion through diamonds. Keep their dependency edges.
   - Create a child ProjectModel containing all reached types and their outgoing
     dependency records, preserving method-level links and dependency kinds.
   - Record those type names as covered, without modifying the original model.
4. Collect every type not covered by a root result.
   - If there are any, start one final traversal from all of them together.
   - Include reachable targets even if another tree already contains them. This
     keeps leftover dependencies self-contained rather than dropping links.
   - Create one final leftovers model.
5. Return the array. Do not append an empty leftovers model.

The work queue implements recursive reachability without a recursive call stack.
Within a root result, its root is the first type, followed by breadth-first discovery
in input dependency order. Dependencies retain their input order. Leftover seeds
retain input type order. Every output preserves the source Name and Path.

## Copying and boundaries

Each result owns copies of its types, fields, properties, methods and dependencies.
Editing one output cannot change the input or another result. Shared types occur
once within each result and can occur in multiple results, with their reachable
subgraphs copied into each. Missing member arrays are treated as empty arrays.

Splitting follows the dependency edges actually supplied. It does not infer runtime
interface implementations, filter external types, distinguish Architecture/Data views,
or add property/construction/event-delivery links. Those are separate drawing or
model-generation decisions. Cycle and diamond edges remain in each result, so the
outputs may retain graph relationships as well as ordinary parent/child branches.

Empty input returns an empty array. Duplicate type names and dependencies whose
endpoints are absent from the input are rejected rather than silently losing facts.
The splitter expects the complete input model, including its referenced boundaries.

## Sample acceptance and coverage

The fixed school sample produces **7 output models**:

- Six roots: ClassManager, ClassStudentManager, SchoolManager, StudentManager,
  TeacherManager and SchoolServiceCollectionExtensions (the DI registration helper).
- One leftovers model containing Class, ClassStudent, School, SchoolDataContext,
  Student and Teacher, with no call/inheritance dependencies among those leftovers.

The SchoolManager result contains 20 types and 47 dependency records: its complete
concrete storage and eventing stacks, their interfaces, SchoolFactory/ISchoolFactory,
List<T> and IEventHub. SchoolFactory is shared across all five manager results.
Extraction resolves interface calls before splitting; the splitter remains unchanged.

The suite passes 72 tests. Fourteen focused splitter cases cover roots, shared
subgraphs, methodless types, interfaces, isolated types, self-references, cycles,
leftovers pointing to already used nodes, independent copies, distinct method links,
empty input and invalid endpoints/identities. A further acceptance test builds the
real sample through ProjectModelBuilder and checks the expected split.

Coverlet measured 100% line and branch coverage for ProjectModelSplitter,
ProjectModelSplitterProcessingService and ProjectModelService. This does not imply
coverage of the unfinished layout or export stages.

## Position in Algorithm 2

Single-project extraction and the requested source-graph splitting are implemented.
Repeating shared dependencies is now an explicit user instruction. Interface
implementation resolution is not a prerequisite for this splitter: interfaces are
ordinary dependency targets. Multi-project reconciliation, display choices, geometry,
routing and Draw.io export remain separate later stages.
