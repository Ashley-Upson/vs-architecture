using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed class NodeDuplicationSettings
{
    public bool AllowDuplicateNodes { get; set; } = true;

    public List<string> DuplicationExceptionPatterns { get; set; } = new();
}
