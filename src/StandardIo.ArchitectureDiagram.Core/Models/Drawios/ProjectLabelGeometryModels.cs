namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record ProjectLabelGeometry(
    string ProjectId,
    Rect ProjectBounds,
    Rect ProjectLabelTextBounds,
    Rect ProjectLabelObstacleBounds);
