// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Rendering;
internal sealed class LayoutOrchestrationService(IProjectModelLayoutService layoutService, ILayoutInitializationService initializationService, IRenderModelProcessingService renderModelProcessingService, StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands.IRenderConfigurationService configurationService) : ILayoutOrchestrationService
{
    private static readonly ConcurrentDictionary<LayoutCacheKey, Lazy<RenderModel>> renderModels = new();

    public RenderModel BuildRenderModel(RenderModel renderModel)
    {
        ArgumentNullException.ThrowIfNull(renderModel);
        ArgumentNullException.ThrowIfNull(renderModel.ProjectModels);
        configurationService.Validate(renderModel.Configuration);

        var cacheKey = new LayoutCacheKey(
            projectModels: renderModel.ProjectModels,
            diagramType: renderModel.DiagramType,
            configuration: JsonSerializer.Serialize(
                value: renderModel.Configuration));

        Lazy<RenderModel> cachedRenderModel = renderModels.GetOrAdd(
            key: cacheKey,
            valueFactory: _ => new Lazy<RenderModel>(
                valueFactory: () => BuildUncached(renderModel: renderModel),
                mode: LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return cachedRenderModel.Value;
        }
        catch
        {
            renderModels.TryRemove(
                item: new KeyValuePair<LayoutCacheKey, Lazy<RenderModel>>(
                    key: cacheKey,
                    value: cachedRenderModel));

            throw;
        }
    }

    private RenderModel BuildUncached(RenderModel renderModel)
    {
        renderModelProcessingService.PrepareRenderModel(renderModel);
        initializationService.Initialize(renderModel);
        return layoutService.Layout(renderModel);
    }

    private sealed class LayoutCacheKey(
        ProjectModel[] projectModels,
        DiagramTypes diagramType,
        string configuration) : IEquatable<LayoutCacheKey>
    {
        private readonly int hashCode = CreateHashCode(
            projectModels: projectModels,
            diagramType: diagramType,
            configuration: configuration);

        public bool Equals(LayoutCacheKey? other) =>
            other is not null &&
            diagramType == other.DiagramType &&
            string.Equals(
                a: configuration,
                b: other.Configuration,
                comparisonType: StringComparison.Ordinal) &&
            projectModels.Length == other.ProjectModels.Length &&
            projectModels.Zip(
                second: other.ProjectModels,
                resultSelector: ReferenceEquals)
                .All(predicate: equal => equal);

        public override bool Equals(object? obj) =>
            obj is LayoutCacheKey other && Equals(other: other);

        public override int GetHashCode() => hashCode;

        private ProjectModel[] ProjectModels => projectModels;

        private DiagramTypes DiagramType => diagramType;

        private string Configuration => configuration;

        private static int CreateHashCode(
            ProjectModel[] projectModels,
            DiagramTypes diagramType,
            string configuration)
        {
            var hashCode = new HashCode();
            hashCode.Add(value: diagramType);
            hashCode.Add(value: configuration, comparer: StringComparer.Ordinal);

            foreach (ProjectModel projectModel in projectModels)
            {
                hashCode.Add(value: RuntimeHelpers.GetHashCode(o: projectModel));
            }

            return hashCode.ToHashCode();
        }
    }
}
