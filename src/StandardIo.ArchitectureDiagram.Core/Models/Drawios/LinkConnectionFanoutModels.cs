using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum FanoutDirection { Source, Target }
internal enum FanoutSide { Left, Right }

internal sealed record LinkConnectionFanoutMembership(
    string GroupId,
    FanoutDirection Direction,
    string SharedNodeId,
    int TerminalOrder,
    int LaneOrder,
    int RemoteNodeOrder,
    FanoutSide Side);

internal sealed record LinkConnectionFanoutGroup(
    string Id,
    FanoutDirection Direction,
    string SharedNodeId,
    IReadOnlyList<LinkConnectionFanoutMembership> Routes);

internal sealed record LinkConnectionTransition(
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
