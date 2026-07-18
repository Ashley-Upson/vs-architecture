using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed record DevelopmentCommonAuthorityTrialResult(
    string BeforeDocument,
    string AfterDocument,
    string ReportJson,
    IReadOnlyList<GeneratedRoute> BeforeRoutes,
    IReadOnlyList<GeneratedRoute> AfterRoutes);
