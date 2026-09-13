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
            .Where(predicate: diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if (errors.Length > 0)
        {
            throw new InvalidOperationException(message: $"Project {project.Name} has compilation errors:\n{string.Join(separator: "\n", values: errors.Select(selector: error => error.ToString()))}");
        }

        var dependencies = new List<TypeRelationship>();

        foreach (INamedTypeSymbol type in roslynBroker.GetDefinedTypes(compilation: compilation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fromType = roslynBroker.GetTypeName(type: type.OriginalDefinition);

            foreach (INamedTypeSymbol target in roslynBroker.GetBaseTypes(type: type))
            {
                dependencies.Add(item: new TypeRelationship { DependencyType = DependencyType.Inheritance, FromType = fromType, ToType = roslynBroker.GetTypeName(type: target.OriginalDefinition) });
            }

            var calls = roslynBroker.GetCalls(compilation, type, cancellationToken).ToArray();
            foreach (bool referenceOnly in new[] { false, true })
            foreach (var target in roslynBroker.GetReferencedTypes(compilation, type, cancellationToken, referenceOnly).Where(target => referenceOnly || !calls.Any(call => !call.IsResultExtension && SymbolEqualityComparer.Default.Equals(call.DependencyType.OriginalDefinition, target.OriginalDefinition))))
                dependencies.Add(new TypeRelationship { DependencyType = DependencyType.Consumed, IsComposition = referenceOnly, FromType = fromType, ToType = roslynBroker.GetTypeName(target.OriginalDefinition) });

            foreach (var call in calls)
            {
                var method = call.Target.ReducedFrom ?? call.Target;
                bool isComposition = roslynBroker.IsCompositionRoot(call.DependencyType) || method.ContainingNamespace.ToDisplayString() is "Microsoft.Extensions.DependencyInjection" or "Microsoft.Extensions.Hosting" || method.IsExtensionMethod && method.Parameters.Any(parameter =>
                    parameter.Type.ContainingNamespace?.ToDisplayString() is "Microsoft.Extensions.DependencyInjection" or "Microsoft.Extensions.Hosting");
                dependencies.Add(new TypeRelationship { DependencyType = DependencyType.Consumed, IsComposition = isComposition,
                    FromType = fromType, ToType = roslynBroker.GetTypeName(call.DependencyType.OriginalDefinition),
                    FromMethod = call.Caller?.Name, ToMethod = call.Target.Name, IsResultExtension = call.IsResultExtension && !isComposition });
            }
        }

        project.Dependencies = dependencies.Where(link => !string.Equals(link.FromType, link.ToType, StringComparison.Ordinal)).ToArray();
    }
}