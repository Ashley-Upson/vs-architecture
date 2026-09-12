using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;

internal partial class RoslynBroker
{
    private static IEnumerable<string> GetRestoredReferences(string directory)
    {
        string path = Path.Combine(directory, "obj", "project.assets.json");
        if (!File.Exists(path)) yield break;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var target = root.GetProperty("targets").EnumerateObject().First(t => !t.Name.Contains('/'));
        var libraries = root.GetProperty("libraries");
        var folders = root.GetProperty("packageFolders").EnumerateObject().Select(p => p.Name).ToArray();
        foreach (var library in target.Value.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("compile", out var compile)
                || !libraries.TryGetProperty(library.Name, out var metadata)
                || metadata.GetProperty("type").GetString() != "package") continue;
            foreach (var asset in compile.EnumerateObject().Where(a => a.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                string relative = Path.Combine(metadata.GetProperty("path").GetString()!, asset.Name);
                string? resolved = folders.Select(folder => Path.Combine(folder, relative)).FirstOrDefault(File.Exists);
                if (resolved is null) throw new FileNotFoundException("Restore the analysed project's packages before generating its diagram.", relative);
                yield return resolved;
            }
        }
    }
}
