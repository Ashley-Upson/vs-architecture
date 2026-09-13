namespace StandardIo.ArchitectureDiagram.Core2.Models;
internal sealed record ContextualDiagram(ContextualType[] Types, ContextualLink[] Links);
internal sealed record ContextualType(string Name, string Project, string[] Lines);
internal sealed record ContextualLink(string From, string To, string Label);
