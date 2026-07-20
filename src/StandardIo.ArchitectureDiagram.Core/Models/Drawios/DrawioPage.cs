using System.Collections.Generic;
using System.Xml.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.Drawios;

public sealed record DiagramDiagnostic(string Code, string Message, string? SemanticId = null);

public sealed record DrawioPage(
    string SuggestedName,
    string StablePageKey,
    XElement GraphModel,
    IReadOnlyList<DiagramDiagnostic> Diagnostics);

public sealed record DrawioDocument(string Content, IReadOnlyList<string> PageNames, IReadOnlyList<string> PageIds);

public sealed class DrawioDocumentSettings
{
    public string Host { get; set; } = "StandardIo.ArchitectureDiagram";
    public bool AllowEmptyDocument { get; set; }
}
