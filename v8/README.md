# Independent v8 scaffold

The solution's v8 folder contains Core2, Core2.Tests, DiagramCLI and SampleProject. These projects
have no references to Ashley's projects. Existing projects are restored to the
branch baseline.

From the repository root:

```powershell
dotnet run --project v8/DiagramCLI -- Architecture "C:\path\First.csproj" "C:\path\Second.csproj"
dotnet run --project v8/DiagramCLI -- Data "C:\path\First.csproj"
dotnet run --project v8/DiagramCLI -- --help
dotnet test v8/StandardIo.ArchitectureDiagram.Core2.Tests
```

The first argument selects the diagram type (case-insensitive); remaining arguments
are existing C# project paths. DiagramCLI creates the two-property request and calls
DiagramGenerator. The scaffold reports "Diagram generation is not implemented yet."
and exits with code 1; it does not generate output yet.

The [sample project](StandardIo.ArchitectureDiagram.SampleProject/README.md) is the
readable proving ground for extraction tests and future Draw.io acceptance tests.
All four v8 projects target .NET 10. The public ProjectModelBuilder.BuildAsync accepts
a project/file/folder path and returns one ProjectModel through the internal orchestration.
Diagram generation remains a separate unfinished stage.
