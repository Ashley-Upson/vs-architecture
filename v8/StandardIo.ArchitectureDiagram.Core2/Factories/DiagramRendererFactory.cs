using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace StandardIo.ArchitectureDiagram.Core2.Factories;
internal sealed class DiagramRendererFactory(IEnumerable<IDiagramRenderer> renderers) : IDiagramRendererFactory
{
    public IDiagramRenderer Create(string? format, string outputPath)
    {
        IDiagramRenderer[] registered = renderers.ToArray();
        if (registered.GroupBy(renderer => renderer.Rule.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Renderer format names must be unique.");
        string extension = Path.GetExtension(outputPath);
        IDiagramRenderer[] matches = registered.Where(renderer => format is null
            ? renderer.Rule.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            : string.Equals(renderer.Rule.Name, format, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) throw new ArgumentException("No renderer is registered for " + (format ?? extension));
        if (matches.Length > 1) throw new InvalidOperationException("More than one renderer matches the extension; supply --format.");
        IDiagramRenderer selected = matches[0];
        if (!selected.Rule.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("The output extension does not match format " + selected.Rule.Name);
        return selected;
    }
}
