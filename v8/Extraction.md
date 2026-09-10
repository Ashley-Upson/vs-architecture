# Project model extraction

> Rendering update: the Architecture generator now returns Draw.io bytes through the
> builder/splitter/renderer service chain. See [Rendering.md](Rendering.md).
> Earlier statements below describing generation as unimplemented are historical.


## Service ownership

```text
ProjectModelBuilder (public exposure)
  ProjectModelOrchestrationService
    ProjectProcessingService
      ProjectService
        FileBroker
    ProjectTypesProcessingService
      ProjectTypesService
        RoslynBroker
    ProjectDependenciesProcessingService
      ProjectDependenciesService
        RoslynBroker
```

The public exposure is `ProjectModelBuilder.BuildAsync(projectFilePath, cancellationToken)`.
It creates the internal service stack and delegates to this orchestration entry point:

```csharp
Task<ProjectModel> GenerateProjectModelAsync(
    string projectFilePath,
    CancellationToken cancellationToken = default);
```

All dependencies are injected through internal interfaces. The orchestration does
not instantiate services or brokers and has no file scanning, symbol traversal,
collection sorting or deduplication logic. It produces one model per call.

## Logical flow

### Stage 1: Resolve the project

ProjectModelOrchestrationService calls ProjectProcessingService first.

- Reject an empty path and normalize relative segments to an absolute path.
- If the path identifies a .csproj file, use it directly.
- If it identifies another file, use that file's containing folder.
- Otherwise treat the path as a folder.
- Ask ProjectService for the .csproj files immediately inside the folder.
  - One project: use its absolute path.
  - No project: report the missing project.
  - Multiple projects: require a specific .csproj path.
- Filesystem operations are provided by FileBroker.

### Stage 2: Create the shared model

The orchestration creates one ProjectModel with Name and resolved Path, plus empty
Types and Dependencies arrays. It passes that same instance to both processing stacks.
Only ProjectModel and CancellationToken cross those service boundaries.

### Stage 3: Populate types

The orchestration awaits ProjectTypesProcessingService.PopulateTypesAsync(project).

- The processing calls ProjectTypesService.PopulateTypesAsync with the same model.
- The foundation loads its own compilation through RoslynBroker using project.Path.
  - Scan the containing folder recursively for .cs files, excluding bin/obj segments.
  - Parse deterministically and add common implicit usings.
  - Add runtime references and DLLs beside the most recent project build.
  - Exclude the project's own DLL and deduplicate dependency filenames.
  - Create a CSharpCompilation directly; no MSBuild workspace is used.
- The foundation rejects compilation errors before changing project.Types.
- RoslynBroker queries the defined classes/interfaces, including nested types, and
  the base types and method-call targets needed to discover external boundaries.
- For each distinct type, the foundation checks its assembly/type identity and maps
  it to DefinedType.
  - Local definitions include declared fields and properties, public ordinary methods,
    and explicit interface implementations (public contract methods). Private, protected
    and internal ordinary methods are not listed.
  - External targets retain name and kind with empty member arrays.
  - Conflicting full names fail before assigning the completed array.
- The foundation assigns project.Types; the processing sorts those domain objects.
- project.Dependencies, Name and Path are unchanged.

### Stage 4: Populate dependencies

The orchestration awaits ProjectDependenciesProcessingService.PopulateDependenciesAsync
with that same model instance.

- The processing calls ProjectDependenciesService.PopulateDependenciesAsync.
- That foundation loads and validates its own compilation from project.Path.
  It does not rely on project.Types or a compilation supplied by the type stack.
- For each defined type, RoslynBroker supplies its base/interface symbols and
  resolved explicit method calls.
- The foundation maps those facts directly to Dependency records.
  - Inheritance includes interface implementation but omits automatic System.Object.
  - Start each call chain from a public method or explicit interface implementation.
  - Follow reachable non-public helpers on the same type, attributing their outgoing
    calls to the entry method; a visited set stops helper cycles. Uncalled helpers
    do not create independent call chains.
  - For an interface method call, use Roslyn's interface-member mapping to find
    each available nonabstract implementation in this source compilation. Record
    concrete implementation method endpoints, including overrides and explicit methods.
  - If no implementation is available, retain the interface endpoint as a boundary.
    Inheritance links to contracts are retained independently.
  - Call targets normalize generic definitions and extension-method ownership.
  - Lambda/local-function calls are attributed to their containing declared method.
  - Constructors/accessors retain their method names; initializers can have no caller.
- The foundation assigns project.Dependencies.
- The processing deduplicates and orders the domain dependency records.
- project.Types, Name and Path are unchanged.

### Stage 5: Return the populated model

The orchestration returns the original ProjectModel instance. Roslyn types are
confined to brokers and foundation implementation details: they do not appear in
model properties, foundation service contracts, processing services or orchestration.
There are no ProjectCompilation/ProjectDependencies wrapper models or shared compiler
cache. Each foundation obtains its own compilation, so a complete extraction performs
two source loads. Only the domain ProjectModel is shared between the stacks.

## Folder loader contract

The loader follows cCoder.CodeAnalysis's ArchitectureService.Build(string) approach.
A successful build helps provide third-party/project-reference DLLs beside the output.
Missing references surface as compilation errors. The loader does not restore packages,
run a build, evaluate .csproj Include/Remove items or imports, choose conditional symbols,
or execute source generators. Common implicit usings are supplied unconditionally.
This is source-folder extraction, not MSBuild project evaluation.

All four v8 projects target .NET 10. Roslyn 5.0 supplies compiler/workspace APIs;
Microsoft.Build, MSBuildWorkspace and Microsoft.Build.Locator packages are absent.

## Expectations and validation

The suite currently passes 72 tests:

- Seven concrete-call fixtures cover multiple implementations, explicit contracts,
  helper cycles and overloads, public member filtering, generic/inherited contracts,
  and unresolved interface boundaries.
- Existing semantic fixtures retain type/member naming, partial and nested types,
  interface calls, inheritance, cycles, boundaries, constructors, accessors,
  initializers, dynamic rejection and compiler-error expectations.
- Real temporary folders cover path variants, ambiguity, missing projects, build-output
  exclusion, implicit usings and dependency assemblies from local build output.
- The orchestration test verifies resolve/types/dependencies order, parameter forwarding,
  the same ProjectModel instance at each stage and propagation of stage failures.
- Foundation tests verify independent compilations, isolated mutation of Types and
  Dependencies, validation before replacement, and ambiguous-name rejection.
- A contract test prevents Roslyn types from reappearing in models or service interfaces.
- Sample acceptance calls the public ProjectModelBuilder with a csproj path and folder path through the production loader
  and compares the complete result with the independently specified ExpectedModel.json:
  94 types and 235 dependencies.
- Sample CRUD/event tests and the three generator-scaffold tests continue to pass.

The previous selection-array tests were replaced with single-path and coordination
contracts. Combined multi-project ownership/deduplication is no longer performed by
this single-project service. A later aggregation step can reconcile external boundary
names with the definitions in other ProjectModels.

Coverage measured during this refactor gives 100% line and branch coverage to the
orchestration, all three processing services and all three foundations. Some broker
fallback and implicit-symbol/generated branches remain unexercised. This is not full
coverage of every component or of the unfinished diagram generation algorithm.

```powershell
dotnet test v8/StandardIo.ArchitectureDiagram.Core2.Tests --collect:"XPlat Code Coverage"
dotnet build v8/DiagramCLI
```

## Remaining diagram work

ProjectModelSplitter now performs the requested root selection and splitting; see
[Splitting.md](Splitting.md). DiagramGenerator and DiagramCLI still explicitly report
diagram generation as unimplemented. Cross-project aggregation, arrangement, routing
and Draw.io export remain separate stages. Explicit method calls and inheritance are the
current dependency kinds; object creation and property access do not add call links.
Roslyn distinguishes overloads during resolution, but the resulting string method
names cannot distinguish them. All available source implementations are potential
targets; this does not determine the actual runtime DI registration. cCoder.CodeAnalysis
remains attached with diagnostics visible; passing tests does not certify full
coding-standard compliance.
