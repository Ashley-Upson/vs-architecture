// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Brokers.ProjectModels;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
internal sealed class ProjectModelBuilderService(IProjectModelBroker broker) : IProjectModelBuilderService
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<ProjectModel>>> projectModels =
        new(comparer: StringComparer.OrdinalIgnoreCase);

    public async Task<ProjectModel> BuildAsync(
        string projectFilePath,
        CancellationToken cancellationToken)
    {
        string cacheKey = Path.GetFullPath(path: projectFilePath);

        Lazy<Task<ProjectModel>> cachedProjectModel = projectModels.GetOrAdd(
            key: cacheKey,
            valueFactory: path => new Lazy<Task<ProjectModel>>(
                valueFactory: () => broker.BuildAsync(
                    projectFilePath: path,
                    cancellationToken: CancellationToken.None),
                mode: LazyThreadSafetyMode.ExecutionAndPublication));

        Task<ProjectModel> projectModelTask = cachedProjectModel.Value;

        try
        {
            return await projectModelTask.WaitAsync(cancellationToken: cancellationToken);
        }
        catch
        {
            if (projectModelTask.IsCanceled || projectModelTask.IsFaulted)
            {
                projectModels.TryRemove(
                    item: new KeyValuePair<string, Lazy<Task<ProjectModel>>>(
                        key: cacheKey,
                        value: cachedProjectModel));
            }

            throw;
        }
    }
}
