using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum FanoutDirection { Source, Target }
internal enum FanoutSide { Left, Right }

internal sealed record TerminalFanoutMembership(
    string GroupId,
    FanoutDirection Direction,
    string SharedNodeId,
    int TerminalOrder,
    int LaneOrder,
    int RemoteNodeOrder,
    FanoutSide Side);

internal sealed record TerminalFanoutGroup(
    string Id,
    FanoutDirection Direction,
    string SharedNodeId,
    IReadOnlyList<TerminalFanoutMembership> Routes);

internal sealed record TerminalTransition(
    string Id,
    string EdgeId,
    int RouteRevision,
    FanoutDirection Direction,
    string TerminalNodeId,
    FanoutSide Side,
    int PortCoordinate,
    Segment ProtectedStub,
    string? FirstOrdinaryCorridorId,
    int RequiredDepth,
    int RequiredSpread,
    int LaneOrder);
