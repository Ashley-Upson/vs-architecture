# Project source model

> Rendering update: the Architecture generator now returns Draw.io bytes through the
> builder/splitter/renderer service chain. See [Rendering.md](Rendering.md).
> Earlier statements below describing generation as unimplemented are historical.


Produce one ProjectModel for each selected project. The in-memory input for the
later diagram algorithm is an array of these models.

```text
ProjectModel
  string Name
  string Path
  DefinedType[] Types
  Dependency[] Dependencies

DefinedType
  string Name
  FrameworkType FrameworkType     Class | Interface
  bool IsInternal
  Field[] Fields                 Type, Name
  Property[] Properties          Type, Name
  Method[] Methods               Name

Dependency
  DependencyType DependencyType  Inheritance | Consumed
  string FromType
  string ToType
  string FromMethod
  string ToMethod
```

## Extraction expectations

- List classes and interfaces defined by each selected project in its Types array.
- IsInternal means the type is defined in any selected project, not that it has the
  C# internal access modifier. A public type in a selected project is internal to
  this dataset; a type outside the selected projects is external to it.
- Start from the selected projects' types and crawl the reachable type graph across
  project boundaries. Expand internal types, keeping a visited set so shared
  dependencies and cycles do not cause repeated expansion or infinite recursion.
- Retain each reached external type with IsInternal = false and the incoming link,
  then stop at that type. Do not inspect its members or follow its dependencies;
  its Fields, Properties and Methods arrays are empty boundary information.
- Internal types remain in their defining ProjectModel, including those reached
  from another selected project. External boundary entries belong in each referring
  project's Types array, deduplicated within that project. Such entries describe
  references, not definitions owned by the referring project.
- DefinedType.Name, Field.Type, Property.Type, Dependency.FromType and
  Dependency.ToType use full .NET type names without assembly qualification:
  System.String rather than string, and System.IO.File rather than File or an
  assembly-qualified name. Generic, nested and array name formatting needs explicit
  examples in the extraction tests so declarations and references match consistently.
- Describe declared fields and properties. List only public ordinary methods and
  explicit interface implementations, which are public contract methods.
- Record method-call links as Consumed, from the caller type/method to the referenced
  type/method. Fields and properties describe the type but do not, by themselves,
  introduce additional dependencies under this rule.
- Record inheritance links as Inheritance, from the derived type to the base type.
  Interface inheritance and class-to-interface implementation fit this same category.
  FromMethod and ToMethod are null for inheritance.
- Store dependencies in the project containing FromType. A ToType can be defined in
  another selected project or outside the selection. A reference does not create a
  duplicate internal DefinedType in the referring project.
- Types, fields, properties, methods and dependencies are data only. There are no
  compiler objects, source-location records, IDs, geometry or precomputed roots.
- Successful extraction supplies all required names/paths and arrays, using empty
  arrays when no items exist. Nullable C# annotations allow incomplete DTOs before
  validation; inheritance method endpoints and initializer calling-method names are intentionally absent.

## Limits to keep explicit in the forthcoming tests

- A simple method name does not distinguish overloads. This is sufficient for
  type-level connections; overload-specific analysis would need a naming convention.
- Interface calls resolve to all available concrete implementations in the current
  source compilation; unresolved interfaces remain boundaries. Reachable private
  helper calls are attributed to their public or explicit-contract entry method.
  This is a set of possible targets, not proof of runtime dependency injection.
- The enum intentionally includes only Class and Interface. Extraction includes only classes and interfaces as definitions, omits automatic
  System.Object inheritance, and rejects method-call targets of unsupported kinds
  (such as structs) rather than labeling them Class.
- Distinct types with the same full name in different projects can be defined in
  separate models, but the string dependency endpoints alone cannot disambiguate
  references between them. No assembly/project identity has been added to this model.

## Extraction implementation

The first extraction implementation and its test coverage are described in
[Extraction.md](Extraction.md). ProjectModelOrchestrationService now accepts one path
and returns one ProjectModel. ProjectProcessingService resolves the project path;
RoslynBroker scans its containing folder using the cCoder.CodeAnalysis approach.
DiagramGenerator and DiagramCLI still report diagram generation as unimplemented.

The multi-project ownership rules above remain the intended combined dataset contract.
The current single-project call marks definitions from its folder internal and retains
referenced assemblies as external boundaries. Reconciling boundaries against models
from other selected projects is a later aggregation step; it is not hidden in this
orchestration. The orchestration creates one ProjectModel and passes it down both
stacks for population. Each Roslyn foundation loads its own compilation. Roslyn symbols
and compilations are confined to broker/foundation implementation details; there are
no compiler-bearing intermediate models or symbol types in service interfaces.

Current naming rules use fully qualified readable .NET names: System.String,
System.Nullable<System.Int32>, System.ValueTuple<System.Int32, System.String>,
Example.Box<System.String[]> and Example.Outer<T>.Inner<U>. Dependency endpoints
use original generic definitions so references resolve to their DefinedType entries.

Calls in constructors/accessors retain Roslyn method names such as .ctor and
get_Property. Calls in field/property initializers have no FromMethod. Lambda and
local-function bodies are attributed to the containing declared method. The model
still lists only explicitly declared ordinary methods and interface implementations.
