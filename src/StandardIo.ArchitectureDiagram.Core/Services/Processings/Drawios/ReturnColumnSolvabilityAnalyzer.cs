namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum ReturnColumnHorizontalSolvability
{
    LeftExteriorClear,
    RightExteriorClear,
    OrderingInvariantInteriorBlocker
}

internal static class ReturnColumnSolvabilityAnalyzer
{
    public static ReturnColumnHorizontalSolvability Analyze(ReturnColumnEnvelopeConstraint constraint)
    {
        if (constraint.LeftBlockingSubtreeIds.Count == 0)
            return ReturnColumnHorizontalSolvability.LeftExteriorClear;
        if (constraint.RightBlockingSubtreeIds.Count == 0)
            return ReturnColumnHorizontalSolvability.RightExteriorClear;
        return ReturnColumnHorizontalSolvability.OrderingInvariantInteriorBlocker;
    }
}
