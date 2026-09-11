using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public sealed class DrawioDocumentComposer : IDrawioDocumentComposer
{
    public DrawioDocument Compose(IReadOnlyList<DrawioPage> pages, DrawioDocumentSettings settings)
    {
        pages ??= Array.Empty<DrawioPage>();
        settings ??= new DrawioDocumentSettings();
        if (pages.Count == 0 && !settings.AllowEmptyDocument)
            throw new InvalidOperationException("At least one Draw.io page is required.");

        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = new List<string>();
        var ids = new List<string>();
        var file = new XElement("mxfile", new XAttribute("host", settings.Host));
        foreach (var page in pages)
        {
            var baseName = string.IsNullOrWhiteSpace(page.SuggestedName) ? "Diagram" : page.SuggestedName.Trim();
            nameCounts.TryGetValue(baseName, out var count);
            nameCounts[baseName] = ++count;
            var name = count == 1 ? baseName : $"{baseName} ({count})";
            var id = StableId.From("page", $"{page.StablePageKey}|{name}|{names.Count}");
            names.Add(name);
            ids.Add(id);
            file.Add(new XElement("diagram",
                new XAttribute("id", id),
                new XAttribute("name", name),
                new XElement(page.GraphModel)));
        }

        var content = new XDocument(file).ToString(SaveOptions.DisableFormatting);
        return new DrawioDocument(content, names, ids);
    }
}
