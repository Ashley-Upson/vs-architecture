namespace StandardIo.ArchitectureDiagram.Core2.Models;
internal sealed record ContextualDiagram(ContextualType[] Types, ContextualLink[] Links)
{
    public CompositionTree[]? Trees { get; init; }
}
internal sealed record ContextualType(string Name, string Project, string[] Lines);
internal sealed record ContextualLink(string From, string To, string Label);

internal sealed record CompositionTree(string Title, CompositionTreeNode[] Nodes);
internal sealed record CompositionTreeNode(string Id, string TypeName, string Label, int Depth, string? ParentId)
{
    public string? MemberId { get; init; }
    public string? TargetMemberId { get; init; }
    public string? TargetTypeName { get; init; }
    public string[]? Details { get; init; }
}
