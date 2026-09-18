# Architecture diagrams - V8

The compiler model is shared evidence. Each diagram selects its own meaning; layout arranges that meaning; writers draw the result without rediscovering layout.

## Production stages

1. [Request and configuration](documentation/stages/request.md)
2. [Compiler model](documentation/stages/compiler-model.md)
3. [Contextual diagram model](documentation/stages/contextual-model.md)
4. [Render model and layout](documentation/stages/render-model.md)
5. [Format rendering](documentation/stages/rendering.md)
6. [Document compilation](documentation/stages/document.md)

[Rule execution contract](documentation/rule-contract.md) | [Layout rule catalogue](documentation/rules/readme.md) | [Selectable implementations](documentation/implementations/readme.md)

The command orchestrates these stages through their exposure points. `All` fetches compiler data once and builds four independent tabs. A single diagram request produces only that diagram. Configuration travels with `RenderModel.Configuration`.

## Maintenance

A rule describes one condition and the fields it may change. Correct the rule when its direction is wrong; do not add an opposing cleanup pass. Keep its document and regression test aligned with the implementation. Duplicate selection changes the graph, not the architecture layout contract.

## Run and verify

From the repository root, using .NET 10:

```powershell
dotnet run --project v8/DiagramCLI -- All "C:\path\Project.csproj" --format Html --output diagram.html
dotnet run --project v8/DiagramCLI -- Architecture "C:\path\Project.csproj" --format DrawIO --output diagram.drawio --noduplicates
dotnet run --project v8/DiagramCLI -- --help
dotnet test v8/StandardIo.ArchitectureDiagram.Core2.Tests
```

The [sample project](StandardIo.ArchitectureDiagram.SampleProject/README.md) supplies integration scenarios. The solution also includes its Data project. `--config` reads the render configuration JSON tree; `--max-layout-iterations` bounds convergence work.
