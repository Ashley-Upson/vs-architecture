using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed class DrawioDiagnosticExportResult
{
    public DrawioDiagnosticExportResult(
        string content,
        string reportJson,
        IReadOnlyDictionary<string, string> focusedOutputs,
        int enforcedFindingCount,
        int uniqueRejectedRouteCount)
    {
        Content = content;
        ReportJson = reportJson;
        FocusedOutputs = focusedOutputs;
        EnforcedFindingCount = enforcedFindingCount;
        UniqueRejectedRouteCount = uniqueRejectedRouteCount;
    }

    public string Content { get; }

    public string ReportJson { get; }

    public IReadOnlyDictionary<string, string> FocusedOutputs { get; }

    public int EnforcedFindingCount { get; }

    public int UniqueRejectedRouteCount { get; }
}
