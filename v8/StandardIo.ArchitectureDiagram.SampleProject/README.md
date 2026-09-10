# School proving ground

> Rendering update: the Architecture generator now returns Draw.io bytes through the
> builder/splitter/renderer service chain. See [Rendering.md](../Rendering.md).
> Earlier statements below describing generation as unimplemented are historical.


This sample provides readable school models and concrete storage/event call chains.
Persistence uses lists in memory. Events use the real in-process cCoder.Eventing
EventHub; no database, HTTP or message transport is required. The sample and its test
project target .NET 10 because cCoder.Eventing 2026.9.9.847 targets .NET 10.

Partials, validation layers and exception-handling infrastructure remain deliberately
omitted. The sample-local .editorconfig disables only the rules requiring that
extra infrastructure, pass-through removal, flat storage DTOs or configuration
layers. Compiler, formatting, naming and visibility diagnostics remain enabled.

## Data model

| Model | Properties |
|---|---|
| School | Id, Teachers[], Students[], Classes[] |
| Teacher | Id, SchoolId, Name |
| Student | Id, SchoolId, Name |
| Class | Id, SchoolId, TeacherId, Teacher, Students: ClassStudent[] |
| ClassStudent | Id, ClassId, StudentId, Student |

Identifiers and names are strings. Navigation properties can be unassigned during
construction. SchoolDataContext has initialized lists named Schools, Teachers,
Students, Classes and ClassStudents.

## Service graph

```text
SchoolManager
  SchoolOrchestrationService
    SchoolProcessingService
      SchoolService
        SchoolBroker
          SchoolFactory -> creates SchoolDataContext
    SchoolEventProcessingService
      SchoolEventService
        SchoolEventBroker
          cCoder.Eventing.IEventHub
```

The same explicit stack exists for Teacher, Student, Class and ClassStudent.
Every constructor dependency uses an interface, including managers, storage brokers,
the shared factory and EventHub. School orchestration takes only ISchoolProcessingService
and ISchoolEventProcessingService. It does not traverse or persist child records;
those are managed independently through their corresponding managers.

Managers and orchestrations expose CreateAsync(model), ReadAsync(id), UpdateAsync(model)
and DeleteAsync(id). Storage processing, foundations and brokers expose synchronous
CRUD. Event processing, foundations and brokers expose RaiseCreatedAsync,
RaiseReadAsync, RaiseUpdatedAsync and RaiseDeletedAsync.

An orchestration completes storage first, then awaits the matching event. Event names
are `<Model>.Created`, `<Model>.Read`, `<Model>.Updated` and `<Model>.Deleted`, with
the model as the typed message data and an empty EventAuthInfo. Delete reads the model
before removal so the deleted event contains that record. A missing read, update or
delete throws before any success event is raised. The internal read during deletion
does not publish a separate Read event.

This sample does not provide transactions, duplicate-ID checks, child reconciliation,
retry or rollback. An event failure propagates after storage has changed.

## Composition and use

```csharp
var services = new ServiceCollection();
services.AddSchoolSample();
using var provider = services.BuildServiceProvider();
var manager = provider.GetRequiredService<ISchoolManager>();
await manager.CreateAsync(new School { Id = "school-1" });
```

AddSchoolSample registers logging, cCoder.Eventing, all five event payload types,
ISchoolFactory and all five stacks. Resolve IEventHub from the same provider to
register listeners using ListenToEvent<TModel, THandler>; register the handler with
DI before building the provider. Delivery is awaited and stays within this process.

The sample stacks are singletons so each storage broker retains its list data across
manager resolutions. Every storage broker calls ISchoolFactory.Create once and owns
an independent SchoolDataContext. There is no hidden static store. Lists and sample
operations are intended for sequential use, not concurrent production traffic.

## Fixed extraction expectations

[ExpectedModel.json](ExpectedModel.json) specifies the complete expected extraction:

- 95 types: 48 source classes, 41 source interfaces and 6 external boundaries.
- 236 dependencies: 41 interface implementation links and 195 explicit call links.
- External boundaries: List<T>, IEventHub, and the three DI/eventing registration
  extension classes. External members are not expanded.
- Exact model properties, dependency fields and method names.

Calls to local interfaces resolve to their concrete implementation methods.
Inheritance entries still connect those classes to their contracts. The external
IEventHub has no source implementation here and remains a boundary. The extractor
includes every available source implementation; it does not inspect DI registrations.
Generic registration calls normalize to their defining extension method; they do not
create extra relationships to the registered generic type arguments.

Model navigation properties and context collections remain properties rather than
Consumed links. Object creation, including SchoolFactory's new SchoolDataContext,
does not yet create a dependency. 

## Tests and future acceptance

SampleProjectExtractionTests reads the actual source files, excluding bin/obj, and
compares the whole extracted model with the independently specified ExpectedModel.json.
The acceptance calls now pass either this csproj path or its containing folder to
the public ProjectModelBuilder, using the production folder loader. That loader
follows cCoder.CodeAnalysis: runtime references plus DLLs beside the most recent
project build, without evaluating project compile items. Only the snapshot project
path is replaced. SDK default compile items remain enabled for normal builds.

SampleProjectBehaviorTests checks interface injection, independent empty contexts,
CRUD through every manager into storage and the real EventHub, event order and payload
identity, missing-row failures with no success event, and independent child stacks.

The full Core2 test suite passes 72 tests. Coverlet measured 100% line coverage for
the sample's brokers, services, exposures and composition, and 100% sample branch
coverage. Overall sample line coverage is 99.69%; two navigation properties are not
exercised by the behavior tests. This does not claim full coverage of the unfinished
diagram generation algorithm.

```powershell
dotnet build v8/StandardIo.ArchitectureDiagram.SampleProject
dotnet test v8/StandardIo.ArchitectureDiagram.Core2.Tests --filter FullyQualifiedName~SampleProject
```

Future acceptance should load this csproj through the real project loader and assert
the final Draw.io nodes and relationships. Shared factory and EventHub boundaries,
interfaces and model navigation properties provide concrete layout challenges.
No Draw.io file is generated yet.

ProjectModelSplitter acceptance now verifies six root models (five managers plus the DI registration helper) and a final model for
the six leftover data/context types. See [Splitting.md](../Splitting.md).

CRUD methods include their model name (for example, CreateSchoolAsync). Services
and brokers are internal; public managers are registered through DI factories.
The reviewed oracle includes the resulting GetRequiredService boundary link.
