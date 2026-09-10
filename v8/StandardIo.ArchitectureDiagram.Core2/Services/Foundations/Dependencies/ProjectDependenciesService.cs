// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Dependencies;

internal sealed class ProjectDependenciesService : IProjectDependenciesService
{
    private readonly IRoslynBroker roslynBroker;

    public ProjectDependenciesService(IRoslynBroker roslynBroker) => this.roslynBroker = roslynBroker;

    public async Task PopulateDependenciesAsync(ProjectModel project, CancellationToken cancellationToken)
    {
        Compilation compilation = await roslynBroker.LoadCompilationAsync(projectFilePath: project.Path!, cancellationToken: cancellationToken);
        var errors = roslynBroker.GetDiagnostics(compilation: compilation, cancellationToken: cancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();

        if (errors.Length > 0)
        {
            throw new InvalidOperationException(message: $"Project {project.Name} has compilation errors:\n{string.Join("\n", errors.Select(error => error.ToString()))}");
        }

        var dependencies = new List<Dependency>();

        foreach (INamedTypeSymbol type in roslynBroker.GetDefinedTypes(compilation: compilation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fromType = roslynBroker.GetTypeName(type: type.OriginalDefinition);

            foreach (INamedTypeSymbol target in roslynBroker.GetBaseTypes(type: type))
            {
                dependencies.Add(item: new Dependency
                {
                    DependencyType = DependencyType.Inheritance,
                    FromType = fromType,
                    ToType = roslynBroker.GetTypeName(type: target.OriginalDefinition)
                });
            }

            foreach (var call in roslynBroker.GetCalls(compilation: compilation, type: type, cancellationToken: cancellationToken))
            {
                dependencies.Add(item: new Dependency
                {
                    DependencyType = DependencyType.Consumed,
                    FromType = fromType,
                    ToType = roslynBroker.GetTypeName(type: call.Target.ContainingType.OriginalDefinition),
                    FromMethod = call.Caller?.Name,
                    ToMethod = call.Target.Name
                });
            }
        }

        project.Dependencies = dependencies.ToArray();
    }
}
