# Architecture Diagram Core2

Independent .NET 10 implementation alongside Ashley's original renderer.

- ProjectModelBuilder builds one source model from a project path.
- ProjectModelSplitter derives dependency trees without changing the input.
- DrawIODiagramRenderer and HtmlDiagramRenderer implement IDiagramRenderer and return bytes.
- DiagramRenderCommand processes command-line arguments through registered services.
- DiagramGenerator remains a direct API for default Draw.io generation.

Call `services.AddArchitectureDiagram()` to register the library, then resolve
`DiagramRenderCommand` from the provider. Additional `IDiagramRenderer` implementations
can be registered with their own named format rules.

See [Rendering.md](../Rendering.md) for the complete service flow, CLI examples,
extension contract and output limitations, and [Extraction.md](../Extraction.md)
and [Splitting.md](../Splitting.md) for the model-building stages.
