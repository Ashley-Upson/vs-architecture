using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

public sealed class DiagramRendererRegistry : IDiagramRendererRegistry
{
    private readonly IReadOnlyList<IDiagramRenderer> _renderers;
    private readonly IDiagramRenderer _defaultRenderer;

    public DiagramRendererRegistry()
        : this(new IDiagramRenderer[] { new DrawioDiagramRenderer(), new JsonDiagramRenderer() })
    {
    }

    public DiagramRendererRegistry(IEnumerable<IDiagramRenderer> renderers)
    {
        if (renderers is null)
        {
            throw new ArgumentNullException(nameof(renderers));
        }

        _renderers = renderers.Where(renderer => renderer is not null).ToArray();
        _defaultRenderer = _renderers.FirstOrDefault(renderer =>
            string.Equals(renderer.RendererId, DiagramRendererIds.Drawio, StringComparison.OrdinalIgnoreCase))
            ?? _renderers.FirstOrDefault()
            ?? throw new ArgumentException("At least one diagram renderer is required.", nameof(renderers));
    }

    public IReadOnlyList<IDiagramRenderer> Renderers => _renderers;

    public IDiagramRenderer Resolve(string? rendererId)
    {
        if (string.IsNullOrWhiteSpace(rendererId))
        {
            return _defaultRenderer;
        }

        return _renderers.FirstOrDefault(renderer =>
            string.Equals(renderer.RendererId, rendererId, StringComparison.OrdinalIgnoreCase))
            ?? _defaultRenderer;
    }
}
