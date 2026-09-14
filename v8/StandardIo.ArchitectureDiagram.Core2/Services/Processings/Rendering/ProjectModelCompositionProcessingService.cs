// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
internal sealed class ProjectModelCompositionProcessingService(IProjectModelPresentationService presentationService) : IProjectModelCompositionProcessingService
{
    public ProjectModelPresentation[] Prepare(RenderModel renderModel)
    {
        var presentations = PrepareProjects(renderModel.ProjectModels, renderModel.DiagramType);
        if (renderModel.Configuration.NoDuplicates) return presentations.Take(renderModel.ProjectModels.Length)
            .Concat(presentations.Skip(renderModel.ProjectModels.Length).Where(external => external.Model.Types!.Length > 0)).ToArray();

        var sources = presentations.Take(renderModel.ProjectModels.Length)
            .SelectMany((source,index) => renderModel.DiagramType == DiagramTypes.Architecture
                ? PrepareTree(source,renderModel.ProjectModels[index]) : new[] {source}).ToArray();
        var externalProjects = presentations.Skip(renderModel.ProjectModels.Length).ToArray();
        if (renderModel.Configuration.Architecture.InlineExternals && renderModel.DiagramType == DiagramTypes.Architecture)
        {
            return sources.Select(source =>
            {
                var destinations = source.Model.Dependencies!.Select(link => link.ToType!).ToHashSet(StringComparer.Ordinal);
                var externalTypes = externalProjects.SelectMany(external => external.Model.Types!)
                    .Where(type => destinations.Contains(type.Name!)).DistinctBy(type => type.Name).ToArray();
                return new ProjectModelPresentation(new ProjectModel
                {
                    Name = source.Model.Name, Path = source.Model.Path,
                    Types = source.Model.Types!.Concat(externalTypes).ToArray(), Dependencies = source.Model.Dependencies
                }, source.Labels);
            }).ToArray();
        }
        var scopedExternals = sources.SelectMany((source, index) =>
        {
            var destinations = source.Model.Dependencies!.Select(link => link.ToType!).ToHashSet(StringComparer.Ordinal);
            return externalProjects.Select(external => new ProjectModelPresentation(new ProjectModel
            {
                Name = external.Model.Name,
                Path = external.Model.Path,
                Types = external.Model.Types!.Where(type => destinations.Contains(type.Name!)).ToArray(),
                Dependencies = Array.Empty<TypeRelationship>()
            }, external.Labels) { SourceTreeIndex = index }).Where(external => external.Model.Types!.Length > 0);
        });
        return sources.Concat(scopedExternals).ToArray();
    }

    private static IEnumerable<ProjectModelPresentation> PrepareTree(ProjectModelPresentation source,ProjectModel original)
    {
        if(!original.IsSplitTree){yield return source;yield break;}
        var types=source.Model.Types!;var names=types.Select(t=>t.Name!).ToHashSet(StringComparer.Ordinal);
        var links=source.Model.Dependencies!;var outgoing=links.ToLookup(l=>l.FromType!);
        // Splitting preceded Architecture filtering. Re-evaluate the visible graph
        // so removed attachments cannot leave unrelated branches in a root's tree.
        bool hasRoot=original.RootTypeName is not null && names.Contains(original.RootTypeName);
        var referenced=links.Where(l=>l.FromType!=l.ToType && names.Contains(l.FromType!)).Select(l=>l.ToType!).ToHashSet();
        var seeds=hasRoot?new[]{original.RootTypeName!}:types.Select(t=>t.Name!).Where(n=>!referenced.Contains(n)).Concat(types.Select(t=>t.Name!)).Distinct();
        var covered=new HashSet<string>();
        foreach(string seed in seeds)
        {
            if(covered.Contains(seed))continue;
            var reachable=new HashSet<string>();var pending=new Queue<string>();pending.Enqueue(seed);
            while(pending.TryDequeue(out string? current))
            {
                if(!reachable.Add(current))continue;
                foreach(var link in outgoing[current])pending.Enqueue(link.ToType!);
            }
            covered.UnionWith(reachable);
            yield return new ProjectModelPresentation(new ProjectModel {
                Name=source.Model.Name,Path=source.Model.Path,
                Types=types.Where(t=>reachable.Contains(t.Name!)).ToArray(),
                Dependencies=links.Where(l=>reachable.Contains(l.FromType!)).ToArray()
            },source.Labels);
        }
    }

    private ProjectModelPresentation[] PrepareProjects(ProjectModel[] projectModels, DiagramTypes diagramType)
    {
        foreach (var model in projectModels) ArgumentNullException.ThrowIfNull(model);
        var sourceNames = projectModels.SelectMany(model => model.Types ?? Array.Empty<DefinedType>())
            .Where(type => type.IsInternal).Select(type => type.Name!).ToHashSet(StringComparer.Ordinal);
        var externalProjects = projectModels.SelectMany(model => model.Types ?? Array.Empty<DefinedType>())
            .Where(type => !type.IsInternal && type.AssemblyName is not null && !sourceNames.Contains(type.Name!))
            .DistinctBy(type => type.Name).GroupBy(type => type.AssemblyName!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => new ProjectModel
            {
                Name = group.Key + " (external)", Path = "assembly:" + group.Key,
                Types = group.ToArray(), Dependencies = Array.Empty<TypeRelationship>()
            }).ToArray();
        projectModels = projectModels.Concat(externalProjects).ToArray();
        if (projectModels.Select(model => (model.Path, model.Name)).Distinct().Count() <= 1)
            return projectModels.Select(model => presentationService.Prepare(model, diagramType)).ToArray();

        // Source definitions take precedence over metadata boundary copies.
        var types = projectModels.SelectMany(model => model.Types ?? Array.Empty<DefinedType>())
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .Select(group => group.FirstOrDefault(type => type.IsInternal) ?? group.First()).ToArray();
        var ownedNames = types.Where(type => type.IsInternal).Select(type => type.Name!).ToHashSet(StringComparer.Ordinal);
        var combined = presentationService.Prepare(new ProjectModel
        {
            Types = types,
            Dependencies = projectModels.SelectMany(model => model.Dependencies ?? Array.Empty<TypeRelationship>()).ToArray()
        }, diagramType);

        return projectModels.Select(model =>
        {
            var localNames = (model.Types ?? Array.Empty<DefinedType>())
                .Where(type => type.IsInternal || !ownedNames.Contains(type.Name!) && (type.AssemblyName is null || model.Path == "assembly:" + type.AssemblyName))
                .Select(type => type.Name!).ToHashSet(StringComparer.Ordinal);
            return new ProjectModelPresentation(new ProjectModel
            {
                Name = model.Name,
                Path = model.Path,
                Types = combined.Model.Types!.Where(type => localNames.Contains(type.Name!)).ToArray(),
                Dependencies = combined.Model.Dependencies!.Where(link => localNames.Contains(link.FromType!)).ToArray()
            }, combined.Labels);
        }).ToArray();
    }
}
