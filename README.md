# Architecture Diagram Generator

Visual Studio 2022 extension that generates editable Draw.io dependency diagrams from .NET projects.

## Build

```powershell
dotnet build StandardIo.ArchitectureDiagram.sln
dotnet test tests\StandardIo.ArchitectureDiagram.Core.Tests\StandardIo.ArchitectureDiagram.Core.Tests.csproj
```

The local VSIX package is produced at:

```text
src\StandardIo.ArchitectureDiagram.Vsix\StandardIo.ArchitectureDiagram.Vsix.vsix
```

Install it by double-clicking the `.vsix` file or using Visual Studio's VSIX installer.

## Use

- Right-click a C# project and choose `Generate Draw.io Architecture Diagram`.
- Use `Tools > Architecture Diagram Settings` to edit the extension settings JSON.
- Use `Tools > Export Architecture Diagram Settings` and `Tools > Import Architecture Diagram Settings` to move settings between machines.

## Notes

- Generation starts from the selected project and includes referenced solution projects.
- Internal solution projects render as containers around their classes/interfaces.
- External dependencies collapse to package/assembly-level nodes.
- The generated `.drawio` is intentionally editable so layout can be refined in diagrams.net or Draw.io-compatible tooling.

## Architecture generation

The typed Draw.io Architecture renderer owns connector geometry through deterministic topology selection, terminal allocation, horizontal slots, vertical columns, validation, and coordinate ownership. See [Architecture diagram generation](docs/architecture-generation.md) for the production pipeline, compatibility boundary, current limitations, and defect-driven maintenance workflow.
