# Diagram rendering and command execution

## Command line

```powershell
dotnet build v8/DiagramCLI
dotnet build v8/StandardIo.ArchitectureDiagram.SampleProject
dotnet v8/DiagramCLI/bin/Debug/net10.0/DiagramCLI.dll Architecture v8/StandardIo.ArchitectureDiagram.SampleProject/StandardIo.ArchitectureDiagram.SampleProject.csproj --output Test.drawio
dotnet v8/DiagramCLI/bin/Debug/net10.0/DiagramCLI.dll Architecture v8/StandardIo.ArchitectureDiagram.SampleProject/StandardIo.ArchitectureDiagram.SampleProject.csproj --output Test.html --format html
```

Multiple csproj paths are supported. `-o` aliases `--output`; `-f` aliases `--format`.
Without an explicit format, the factory uses the output extension. Built-in rules are
`drawio` (.drawio) and `html` (.html, .htm), compared without case sensitivity.
Unknown formats, mismatched extensions, duplicate format names and ambiguous inferred
extensions produce errors. Overlapping extensions can be disambiguated by format name.
Data diagram requests remain explicitly unimplemented.

Program creates a ServiceCollection, calls AddArchitectureDiagram, builds and disposes
its provider, resolves DiagramRenderCommand, and passes the unchanged argument array.
Program creates the output directory and writes the returned bytes. Help text goes to
stdout; failures go to stderr with exit code 1. Rendering itself performs no file I/O.

## Service chain

```text
DiagramRenderCommand (public exposure)
  DiagramRenderOrchestrationService
    CommandParserProcessingService -> DiagramRenderRequest
    DiagramRenderRequestService
      DiagramRendererFactory -> IDiagramRenderer, selected by named rule
      DiagramGenerationOrchestrationService
        ProjectModelBuilderService -> ProjectModelBroker -> ProjectModelBuilder
        ProjectModelTreeService -> ProjectModelSplitterBroker -> ProjectModelSplitter
        selected IDiagramRenderer.Render(splitModels) -> byte[]
```

The parser validates command syntax and normalises paths without loading source.
The request service selects a renderer before project extraction, then passes the
selected instance to generation. Generation builds and splits each project once,
collects the trees and calls the renderer once. Cancellation and errors propagate.
DiagramRenderResult carries OutputPath and Content so the console owns persistence;
its null OutputPath denotes help text rather than an output file.

## Public renderer contract

```csharp
public interface IDiagramRenderer
{
    DiagramRendererRule Rule { get; }
    byte[] Render(ProjectModel[] projectModels);
}
```

DrawIODiagramRenderer is the renamed former ProjectModelDrawIORenderer.
HtmlDiagramRenderer implements the same contract. Both return UTF-8 bytes.
Services, service interfaces, brokers, the factory and drawing records remain internal.
IDiagramRenderer and DiagramRendererRule are public so external renderers can register.

```csharp
var services = new ServiceCollection();
services.AddArchitectureDiagram();
services.AddTransient<IDiagramRenderer, MyDiagramRenderer>();
using ServiceProvider provider = services.BuildServiceProvider();
DiagramRenderResult result = await provider.GetRequiredService<DiagramRenderCommand>()
    .ExecuteAsync(args);
```

A renderer declares its name and supported extensions, for example
`new DiagramRendererRule("custom", ".custom")`. The factory uses the registered
IEnumerable<IDiagramRenderer>; adding a format requires no parser or factory switch.
AddArchitectureDiagram explicitly registers all library service contracts and exposures
as transient services. Exposure registrations supply their internal dependencies from
DI. Parameterless builder/splitter/renderer constructors and the existing DiagramGenerator
API remain available for direct callers; DiagramGenerator defaults to Draw.io.
The CLI uses the injected command stack. There are no hidden service-provider instances.

## Shared preparation and layout

Both renderers consume ProjectModelRenderingProcessingService.Prepare, which returns
an internal list of ProjectModelDrawing records. The processing calls these foundations:

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
   Parents are centred over the child slots. Nodes are 180 by 60 units, with minimum
   horizontal gaps of 60 and vertical gutters of 100.
4. Place trees side by side, with 80-unit container gaps and a shared top edge. Containers
   have grey fills and top-left white labels. Full names remain identity metadata;
   visible node labels use the last dot-separated segment.
5. Apply shared role colours from namespace/suffix conventions: exposures blue,
   orchestrations cyan, processing purple, foundations green and brokers olive.
   Unknown roles stay grey. Text is white; connections are light grey.

Input models are not changed. Identical inputs produce deterministic bytes.

## Output formats

DrawIODiagramRenderer delegates to DrawIODocumentService. It writes an uncompressed
mxfile with one Architecture page. Container-relative vertex positions and relationship
styles are explicit; Draw.io handles orthogonal paths between centre anchors.

HtmlDiagramRenderer delegates to HtmlDocumentService. It writes a standalone HTML file
with embedded CSS and inline SVG. Nodes use the exact same container-relative positions,
labels and colours. The document scrolls horizontally and vertically. Relationships are
SVG polylines with ordinary or hollow inheritance arrowheads. XML serialization escapes
labels and metadata, including generic brackets and malicious-looking markup. No external
CSS, JavaScript, fonts or image resources are required. The file opens directly in a browser.

The shared geometry does not solve every cross-link route. Draw.io routing and explicit
HTML polylines may differ around cycles or obstacles. HTML currently uses simple midpoint
orthogonal paths. Neither output claims universal crossing avoidance. Role detection is
conventional, not semantic proof. Cross-project ownership reconciliation, data relationships,
event-delivery graphs and richer member diagrams remain separate work.

## Verification

116 tests pass. Tests cover argument parsing, rule-based selection, invalid/ambiguous
rules, DI provider validation, registered custom renderers, cancellation, existing
extraction/splitting behaviour, and standalone CLI execution for both formats. HTML and
Draw.io geometry are compared, and encoded labels are checked against markup injection.
The sample produces seven containers, 65 visible type occurrences and 53 type connections.
Test.drawio and Test.html are generated in the repository root; the HTML was visually
checked in a browser. Test/coverage artifacts remain outside the repository.

The sample copies package dependencies beside its build output. The CLI uses the .NET 10
ASP.NET shared framework needed by the sample's cCoder.Eventing dependency and the existing
folder loader. Microsoft.Extensions.DependencyInjection 10.0.11 matches the already-required
cCoder.CodeAnalysis dependency version. No MSBuild workspace is introduced.
Existing analyzer warnings remain visible; tests do not certify full coding-standard compliance.
