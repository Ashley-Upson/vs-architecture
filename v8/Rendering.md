# Diagram rendering and command execution

## Command line

```powershell
dotnet build v8/DiagramCLI
dotnet build v8/StandardIo.ArchitectureDiagram.SampleProject
dotnet v8/DiagramCLI/bin/Debug/net10.0/DiagramCLI.dll Architecture v8/StandardIo.ArchitectureDiagram.SampleProject/StandardIo.ArchitectureDiagram.SampleProject.csproj --output Test.drawio
dotnet v8/DiagramCLI/bin/Debug/net10.0/DiagramCLI.dll Architecture v8/StandardIo.ArchitectureDiagram.SampleProject/StandardIo.ArchitectureDiagram.SampleProject.csproj --output Test.html --format html
```

The two architecture views serve different purposes:

- With `--noduplicates`, splitting is bypassed. The combined project graph shows
  shared dependencies across the mapped codebase, including one global external
  box per assembly.
- Without the flag, each identified root is expanded into its own tree. Each tree
  receives its own external assembly boxes, containing only the external types
  that tree uses. A type appears once per tree, and links resolve to that tree's
  external copy. This supports examining one execution tree in isolation.

External scoping happens during shared rendering preparation, so HTML and Draw.io
use identical ownership and link endpoints. It does not change extraction or the
splitter's models. Types defined in another mapped source project still belong to
that source project; they are not treated as external assembly copies.

Multiple csproj paths are supported. `-o` aliases `--output`; `-f` aliases `--format`.
Without an explicit format, Draw.io is used. Pass `--format html` for HTML output.
The supplied output path is independent of renderer selection and its extension is
neither inferred nor checked by the rendering library.
Both Architecture and Data requests use their registered renderer keys.

Program creates a ServiceCollection, calls AddArchitectureDiagram, builds and disposes
its provider, resolves DiagramRenderCommand, and passes the unchanged argument array.
Program creates the output directory and writes the returned bytes. Help text goes to
stdout; failures go to stderr with exit code 1. Rendering itself performs no file I/O.

## Service chain

```text
DiagramRenderCommand (public exposure)
  IDiagramRenderOrchestrationService
    ICommandParserProcessingService -> DiagramRenderRequest
    IDiagramRequestProcessingService
      IDiagramRequestService
        IDiagramRequestBroker
          IDiagramRendererFactory -> creates IDiagramGenerationOrchestrationService
            IProjectModelBuilderService -> ProjectModelBroker -> ProjectModelBuilder
            IProjectModelTreeService -> ProjectModelSplitterBroker -> ProjectModelSplitter
            IDiagramRenderer (constructor-injected using format/type key)
```

The parser validates command syntax and normalises paths without loading source.
The parser converts trimmed, case-insensitive names into DiagramFormats (DrawIO, Html)
and DiagramTypes (Architecture, Data), rejecting unknown or numeric names. DrawIO is
the default format. The request processing service delegates to the foundation, which
validates the request and project paths. The request processing and foundation methods are named RenderDiagramRenderRequestAsync and take DiagramRenderRequest diagramRenderRequest. The foundation passes the typed request and cancellation
token to the broker, leaving the paths unchanged. The broker interpolates
`$"{request.Format}_{request.DiagramType}"` and passes that string to the factory.
The factory resolves the shared builder/tree services and the keyed renderer, then
constructs the generation orchestration. The broker calls its
GenerateAsync(request, cancellationToken) method.
Generation builds each project once and optionally splits it, then calls its
injected renderer once, returning raw bytes. No renderer is passed into a service
method. The command orchestration depends only on its two processing services.
Cancellation and errors propagate.
DiagramRenderResult carries OutputPath and Content so the console owns persistence;
its null OutputPath denotes help text rather than an output file.

## Public renderer contract

```csharp
public interface IDiagramRenderer
{
    byte[] Render(RenderModel renderModel);
}
```

DrawIODiagramRenderer is the renamed former ProjectModelDrawIORenderer.
HtmlDiagramRenderer implements the same contract. Both return UTF-8 bytes.
Services, service interfaces, brokers, the factory and drawing records remain internal.
IDiagramRenderer is public so external renderers can register.

```csharp
var services = new ServiceCollection();
services.AddArchitectureDiagram();
services.AddKeyedTransient<IDiagramRenderer, MyDiagramRenderer>("Html_Architecture");
using ServiceProvider provider = services.BuildServiceProvider();
DiagramRenderResult result = await provider.GetRequiredService<DiagramRenderCommand>()
    .ExecuteAsync(args);
```

AddArchitectureDiagram registers IDiagramRenderer keys for each format/type combination,
such as `Html_Architecture` and `DrawIO_Data`, interpolating both enum names.
There is no separate rule model or extension registry. A replacement renderer uses
ordinary keyed DI registration; adding a format also adds a DiagramFormats member. The built-in Data keys currently share
the existing renderer implementations.
Exposures and their service graphs are resolved through AddArchitectureDiagram.
Hand-built parameterless exposure constructors have been removed; use the service
provider for DiagramGenerator, ProjectModelBuilder, ProjectModelSplitter,
LayoutModelBuilder and both renderers. Tests likewise use the registered graph or
explicitly inject a test dependency. The CLI already uses this composition root.

## Shared preparation and layout

Both renderers accept ProjectModel[] and obtain a fully prepared RenderModel through
format-specific DI services:

```text
HtmlDiagramRenderer / DrawIODiagramRenderer
  IHtmlModelPreparationService / IDrawIOModelPreparationService
    IHtmlModelPreparationBroker / IDrawIOModelPreparationBroker
      LayoutModelBuilder
        ILayoutOrchestrationService
          IProjectModelPresentationService
          IProjectModelLayoutService
          IRenderModelProcessingService
```

LayoutOrchestrationService prepares the display model and its RenderModel, then calls
ProjectModelLayoutService. That foundation gets its ordered rule services through:

```text
ProjectModelLayoutService
  IProjectModelLayoutBroker
    ILayoutRuleFactory
      IEnumerable<ILayoutRuleProcessingService>
```

Each rule exposes ApplyRule(RenderModel). Registered rules handle depth, initial
subtree spacing, parent centring, shared-parent-group centring, node spacing, container
bounds/text placement and routing. The initial spacing rule runs once per model; the
other rules are reapplied. Moving a parent for spacing carries its exclusive subtree.
Parent centring uses the midpoint of the child group's outer edges, allowing wider
spacing where descendants require it. Shared-group corrections are divided between
the parent and child groups so competing corrections can converge.

After each complete pass the foundation validates centring (0.01-unit tolerance), node
spacing, container bounds, finite positions and downward depth (except genuine cycle-closing links).
It stops as soon as the model is valid. MaxLayoutIterations defaults to 1000; the CLI
accepts --max-layout-iterations <positive integer>. Exhaustion throws with the remaining
constraint names, rather than emitting a diagram that failed validation.

The format document services serialize the final RenderModel, including prepared text,
colours, bounds and route points. They do not run positioning rules themselves.

The shared layout stages are:

1. ProjectModelPresentationService validates type identities/endpoints and prepares a
   separate display model. Implemented interfaces become a comma-separated second
   line on the implementing class, including inherited contracts. Their standalone
   nodes/implementation edges are omitted; unresolved contracts remain boundaries.
2. Retain one directed connection per type pair and dependency kind. Method data is
   excluded from the display model and output, while the input ProjectModel retains it
   for later diagram types. Class inheritance remains a distinct relationship.
3. ProjectModelLayoutService chooses a first-discovery placement parent for shared
   children; all additional links and cycles remain in the output. It measures subtree
   widths bottom-up and assigns equal sibling slots based on the widest child subtree.
   Parents are centred over the child slots. A second positioning pass places shared
   nodes at the midpoint of their full parent span and carries their descendants with
   them. Neighbours whose preferred positions conflict are spaced around those positions;
   minimum spacing takes precedence over exact centring when both cannot be satisfied.
   Container bounds expand when needed. Nodes are 210 by 60 units, with minimum
   horizontal gaps of 60 and vertical gutters of 100.
4. Place trees side by side, with 80-unit container gaps and a shared top edge. Containers
   have grey fills and top-left white labels. Full names remain identity metadata;
   visible node labels use the last dot-separated segment.
5. Apply shared role colours from namespace/suffix conventions: exposures blue,
   orchestrations cyan, processing purple, foundations green and brokers olive.
   Unclassified internal types use dark amber; unclassified external types use burnt orange. Text is white; connections are light grey.

Input models are not changed. Identical inputs produce deterministic bytes.

## Output formats

DrawIODiagramRenderer delegates to DrawIODocumentService. It writes an uncompressed
mxfile with one Architecture page. Container-relative vertex positions and relationship
styles are explicit; Both renderers use shared explicit orthogonal waypoints between centre anchors.

HtmlDiagramRenderer delegates to HtmlDocumentService. It writes a standalone HTML file
with embedded CSS and inline SVG. Nodes use the exact same container-relative positions,
labels and colours. The document scrolls horizontally and vertically. Relationships are
SVG polylines with ordinary or hollow inheritance arrowheads. XML serialization escapes
labels and metadata, including generic brackets and malicious-looking markup. No external
CSS, JavaScript, fonts or image resources are required. The file opens directly in a browser.

Each destination has one horizontal bus in the gutter immediately above its row.
All sources targeting that node use the same bus height. Independent horizontal spans
reuse the centred channel. Only touching or overlapping destination spans need another
lane, normally 10 units farther down the gutter. A lane can be reused after its span ends.
Source exits are assigned per parent and direction: nearer destinations get inner exits,
farther destinations get outer exits at the requested spacing (10 units by default) (compressed within the node if
needed). Single-destination exits stay centred. Final channels use these actual exits,
processing longer horizontal source-to-destination distances first. Independent spans
reuse a lane; overlapping shorter spans get a lower lane. Stable geometric/name tie-breaks
make this independent of relationship input order. This nests the simple fan-out without
crossings; aligned links remain straight. Both formats serialize
these prepared endpoints and waypoints, with automatic Draw.io routing disabled.
This rule does not solve obstacle avoidance, cycles or unrelated
vertical crossings. Role detection is
conventional, not semantic proof. Cross-project ownership reconciliation, data relationships,
event-delivery graphs and richer member diagrams remain separate work.

## Verification

184 tests pass. Tests cover enum parsing, request validation, output-path independence,
keyed DI selection, provider validation, custom renderers, cancellation, existing
extraction/splitting behaviour, and standalone CLI execution for both formats. HTML and
Draw.io geometry are compared, and encoded labels are checked against markup injection.
The default sample produces eight containers, 100 visible type occurrences and 105 type connections.
With --noduplicates it produces one project container, 50 unique visible types and 60 connections.
The current Test.drawio and Test.html files use --noduplicates.
Test.drawio and Test.html are generated in the repository root; the latest generated container/node counts were checked from the output files. Test/coverage artifacts remain outside the repository.

The sample copies package dependencies beside its build output. The CLI uses the .NET 10
ASP.NET shared framework needed by the sample's cCoder.Eventing dependency and the existing
folder loader. Microsoft.Extensions.DependencyInjection 10.0.11 matches the already-required
cCoder.CodeAnalysis dependency version. No MSBuild workspace is introduced.
Existing analyzer warnings remain visible; tests do not certify full coding-standard compliance.


The RenderModel refactor was verified with injected preparation/writer tests, arbitrary
precomputed coordinates and colours, and byte-for-byte comparison of both regenerated
sample artifacts against their pre-refactor versions. Both files were identical.

Collection property and collection-returning method calls (such as context.Items.Add and
context.Set<T>().Add) use the receiver owner as the type dependency. The called method
name is retained. The sample storage brokers therefore depend on SchoolDataContext.

HorizontalOffset on both request models defaults to 10 pixels. The CLI accepts
--horizontal-offset <pixels> (positive and finite). It controls source exit and gutter
lane spacing through the shared preparation pipeline; spacing compresses when required
to fit node bounds or gutters. Shared descendant widths are excluded from first-parent
tree measurements, preventing asymmetric empty space in the first tree.

Architecture preparation omits project-defined types with no modelled methods and
relationships incident on those types. The input ProjectModel stays unchanged. External
boundary types remain visible because their method metadata is not extracted. The Data
format path retains these types; its distinct data-model layout is still future work.

ColourLines defaults to false on both request models. Enable --colour-lines to colour
each connection and its arrowhead using its destination node fill. Shared preparation
stores the colour on RenderConnection.Stroke; both writers use that prepared value.
The current sample HTML and Draw.io files enable this option.

The node palette never uses the grey project-container fill (#374151). Default internal
nodes use #713f12 and external nodes #9a3412, both with white-text contrast above 7:1.

SchoolDataContext includes an empty public OnModelCreating method in the sample. It
therefore remains visible in Architecture output, with the five storage-broker links.

The sample now includes SchoolImportManager -> SchoolImportAggregationService, whose
ImportSchool method creates the school and imports Teachers, Students, Classes and
each class's Students collection through all five existing orchestration interfaces.
The manager and aggregation service are registered in AddSchoolSample. Acceptance
testing imports multiple items and reads them back through the existing managers.

Before horizontal placement, layout assigns rows from the complete call graph: every
child is one row below its deepest parent. Discovery order no longer freezes a shared
node at an earlier depth. Cycle-closing edges are retained for rendering but excluded
from depth propagation, because a cycle cannot satisfy the downward-only constraint.
The import sample has 60 downward connections; the generated geometry was checked for
60-unit minimum node gaps and no connection passing through an unrelated node.

The rules refactor was verified with the import sample's parent-centre regression,
DI rule replacement, iteration-limit failure, all existing rendering tests and a
400-node / 399-edge graph. The large synthetic graph converged in four passes and
approximately 1.2 seconds on this workstation (not a universal timing guarantee).
ContentManagement was also attempted: its project builds successfully, and generated
global usings are now loaded, but extraction stops on System.Action<T> because the
current DefinedType contract supports Class and Interface, not Delegate. No successful
ContentManagement diagram or real-project layout timing is claimed.


## Two-project school sample

`StandardIo.ArchitectureDiagram.SampleProject` references the sibling
`StandardIo.ArchitectureDiagram.SampleProject.Data` project. Data owns the five
entity models, SchoolDataContext, ISchoolFactory and SchoolFactory. Namespaces
follow the new Data project location. The service stacks and DI registration
remain in the original sample. Both projects target .NET 10.

Generate the current pair from the repository root:

```powershell
dotnet v8/DiagramCLI/bin/Debug/net10.0/DiagramCLI.dll Architecture v8/StandardIo.ArchitectureDiagram.SampleProject/StandardIo.ArchitectureDiagram.SampleProject.csproj v8/StandardIo.ArchitectureDiagram.SampleProject.Data/StandardIo.ArchitectureDiagram.SampleProject.Data.csproj --output Test.html --format Html --noduplicates --colour-lines
```

Use `--output Test.drawio --format DrawIO` for Draw.io. Build the sample first
so folder-based extraction can resolve the referenced Data assembly.

Selected projects now share presentation-time type resolution. A source definition
replaces its external metadata copy, interfaces resolve to their selected concrete
implementations, and each selected type is drawn in its owning project. Extraction
models are not mutated. Unselected external dependencies remain boundary nodes.

`RenderModel.CrossProjectConnections` stores cross-container edges separately,
with diagram-coordinate points. Local connections retain container-relative
points. Both format writers serialize the prepared routes. Project boxes use the existing depth, outer-edge centring, shared-parent centring,
and spacing rules through `ProjectPositioningLayoutRuleProcessingService`.
Spacing accounts for actual box widths and row heights. Project constraints are
validated by the same iterative layout loop as node constraints.

`CrossProjectClearanceLayoutRuleProcessingService` leaves clear downward exit
columns for cross-project sources; existing centring and spacing rules then
reconcile their parents. `CrossProjectRoutingLayoutRuleProcessingService` calls
the same `DiagramRouting` used within projects, using absolute node coordinates.
There is no outside-corridor routing branch. Draw.io preserves the computed exit
offsets, and HTML draws the same prepared points.

The sample regression checks centring, direct gutter routes, node clearance,
ownership and both project input orders. An additional fixture covers unequal
project sizes, sibling spacing and shared dependencies. These are geometry checks,
not a guarantee of optimal layouts for arbitrary cyclic or dense graphs.

Single-project tree duplication retains its existing behavior. The model still
identifies types by full name and cannot distinguish identically named types
from different assemblies.

Each project has a reviewed ExpectedModel.json oracle. Acceptance tests check
both extraction paths (folder and project file), reference ownership, broker
endpoints, both output formats, and the existing sample behavior.


The branch-spacing correction and ordered rule contracts are documented in [Layout rule interaction review](LayoutRuleInteractionReview.md). Flat row spacing has been replaced by recursive branch spacing in the active pipeline.


## External ownership and compact sizing

External DefinedType records now retain AssemblyName from Roslyn. Splitting
preserves that field. Composition creates one labelled external assembly
container for each unselected declaring assembly, rather than placing its types
inside a consumer's project. Selected source definitions continue to take
precedence. These containers show metadata boundaries, not a crawl of the package.

Initial sibling allocation uses the sum of actual branch widths plus the existing
60px gaps, rather than multiplying the largest width by the number of children.
Independent branches only compete at vertical levels where their intervals
actually overlap. Initial project rows likewise use actual box widths once,
which prevents shared-parent corrections from starting with overlapping boxes.

The palette remains in the same hue families with a slightly darker grey container
and modest fill adjustments. The existing 7:1 white-text contrast test is retained.

The two-project sample remains 50 unique nodes and 60 links. Its generated size
changed from approximately 7566 x 1460 to 4420 x 1300 (42% narrower, 11% shorter).
Both CLI duplication modes and both formats pass; the suite contains 196 tests.


## Extension-method boundary

Roslyn extraction excludes calls to extension methods declared outside the project
compilation. This uses the same per-project assembly boundary as IsInternal, not
the namespace, class name, or type being extended. Reduced extension syntax and
explicit static calls are treated identically. Internally declared extensions
remain, including extensions of external framework types. Ordinary external
instance/static calls remain dependencies.

The sample's own IServiceCollectionExtensions definition remains, while outgoing
calls to external DI/logging/event-registration extensions are excluded. The
regenerated project pair contains 46 nodes and 56 links across the two selected
projects and the cCoder.Eventing external boundary container. All 197 tests pass.


## Render configuration

`DiagramRenderRequest.RenderConfiguration` (also on `DiagramGenerationRequest`) owns the rendering options. Generation places that **same instance** on `RenderModel.Configuration`. The renderer, format preparation foundation/broker, `LayoutModelBuilder`, layout orchestration, metadata preparation, layout rules and document writer each accept a single `RenderModel`. The request-based asynchronous entry points retain their cancellation token.

The model carries the source `ProjectModels` and selected `DiagramType` before preparation; preparation fills its positioned `Projects` and cross-project connections in place. Rendering the same model again recomputes layout from its source models and current configuration.

Shared configuration contains `NoDuplicates`, `HorizontalOffset` (10px), `ColourLines` (false), and `MaxLayoutIterations` (1000). Architecture settings are:

| Setting | Default | Meaning |
| --- | --- | --- |
| RowDepth | 160 | Top-to-top distance between node rows; node height remains 60px. |
| NodeWidth | 210 | Width of every type node. |
| NodeSpacing | 60 | Minimum horizontal gap between nodes and their owned branches. Descendant space and shared parents can require larger gaps. |
| ProjectSpacing | 150 | Minimum gap between project boxes, horizontally or vertically. |
| LayerColours | Current seven colours | Ordered palette: orchestration, processing, foundation, broker, exposure, other internal, external. |

`CallChain` and `DataModel` have their own empty configuration objects ready for their future specialised layouts. Enum names are `Architecture`, `CallChain`, `DataModel`; the CLI also accepts the previous `Data` spelling. This change does not add those specialised diagram implementations.

Use `--config <json-file>` (or `-c`) to load the configuration object directly. See `render-configuration.example.json` for the complete default tree. Omitted properties retain defaults; JSON property names are case-insensitive. Existing flags remain supported and override file values regardless of argument order. The output path still does not select the format.

```powershell
dotnet run --project v8/DiagramCLI -- Architecture `
  v8/StandardIo.ArchitectureDiagram.SampleProject/StandardIo.ArchitectureDiagram.SampleProject.csproj `
  v8/StandardIo.ArchitectureDiagram.SampleProject.Data/StandardIo.ArchitectureDiagram.SampleProject.Data.csproj `
  --output Test.html --format Html --config v8/render-configuration.example.json `
  --noduplicates --colour-lines
```

A partial file can be as small as:

```json
{
  "Architecture": {
    "NodeWidth": 220,
    "NodeSpacing": 80,
    "RowDepth": 200,
    "ProjectSpacing": 120
  }
}
```

Configuration loading uses an injected file broker and foundation service. Parsing validates configuration before dispatch; layout also validates direct library calls. Dimensions must be finite and positive, `RowDepth` must exceed the 60px node height, `NodeWidth` must exceed the 10px port margin, and `LayerColours` must contain seven `#RRGGBB` values. Invalid JSON and unknown property names are reported rather than silently ignoring misspelled settings. Users supplying a custom palette control its contrast.

## Inline external boundaries and type labels

Both architecture views use a bold short type name, normal interface names and a
smaller base-type name. Only interfaces declared directly on the type are labelled;
interfaces inherited through its base class or another interface are excluded.
Up to two direct interface names are shown; larger sets display `<multiple interfaces>`
while the raw model retains all direct declarations. Inherited contracts still participate
in dependency resolution.
Missing lines and System.Object are omitted. Inheritance
is label information, not a dependency line. Declared-method and data-type
classification is described in [Models.md](Models.md).

`Architecture.InlineExternals` defaults to false. Enable it in a render config
file to place external terminal nodes inside each split tree, rather than in
external assembly boxes. The combined `--noduplicates` view ignores this option.
A consumed connection to an inline external is red when its source also consumes
a different internal component. This flags a boundary for architectural review;
it does not claim every such use is wrong. Other links retain their configured
colours. No absolute row number is used for this decision.

The supplied [Architecture.InlineExternals.json](Architecture.InlineExternals.json)
enables this option and coloured connections:

```powershell
dotnet v8/DiagramCLI/bin/Debug/net10.0/DiagramCLI.dll Architecture "path/to/project.csproj" --format Html --output Diagram.html --config v8/Architecture.InlineExternals.json
```

Set InlineExternals to false to restore the per-tree external boxes. Data-only
source trees and empty external containers do not create empty architecture boxes.

### Layout cleanup and built-in types

After branch placement and parent centring, the cleanup rule packs completed branches using the same ownership and clearance constraints. It reclaims excess horizontal gaps before bounds and routes are computed. Architecture views omit `System.*` nodes and their connections; raw extraction remains available for other diagram types. Draw.io output disables grid and page view. Deferred-execution rendering has been rolled back.

### Inferred naming rows

`InferredRowLayoutRuleProcessingService` runs after dependency depth and before horizontal placement. It splits short type names into words and discovers repeated prefixes and suffixes across distinct types in the selected diagram. It contains no architectural vocabulary. Peer patterns are preferred over patterns containing direct calls between their members; longer patterns win next, with suffixes as a deterministic tie breaker. After assigning a group, candidates are reconsidered using remaining types, so a general suffix can describe the residual peer set.

`RenderModel.Rows` records inferred group keys, starting logical rows, and node IDs. Dependencies within a group create additional subrows. Dependencies between groups establish their ordering, including forward cross-project references. Groups that make the ordering contradictory fall back to individual dependency-based placement. Real cycle handling stays with the existing depth rule.

Projects at the same dependency tier share row positions. Each downstream tier starts fresh rather than reserving empty rows for its upstream projects. Matching is a layout heuristic, not a declaration that a type complies with an architectural standard. Unmatched types retain dependency constraints. More spacing may be required to align groups; branch clearance, centring, compaction, and routing still run afterwards.
