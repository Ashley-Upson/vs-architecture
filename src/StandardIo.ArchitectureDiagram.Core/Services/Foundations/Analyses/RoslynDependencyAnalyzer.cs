using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;

public sealed class RoslynDependencyAnalyzer : IRoslynDependencyAnalyzer, IArchitectureAnalyser
{
    public async Task<ArchitectureDiagramModel> AnalyseAsync(
        IEnumerable<Project> selectedProjects,
        ArchitectureAnalysisSettings settings,
        CancellationToken cancellationToken = default)
    {
        settings ??= new ArchitectureAnalysisSettings();
        var model = await AnalyzeCoreAsync(selectedProjects, settings, cancellationToken).ConfigureAwait(false);
        return ToArchitectureDiagram(model);
    }

    public async Task<DiagramModel> AnalyzeAsync(
        Project selectedProject,
        DiagramSettings settings,
        CancellationToken cancellationToken = default)
    {
        return await AnalyzeCoreAsync(new[] { selectedProject }, ToArchitectureSettings(settings), cancellationToken).ConfigureAwait(false);
    }

    public Task<DiagramModel> AnalyzeAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken = default) =>
        AnalyzeCoreAsync(selectedProjects, ToArchitectureSettings(settings), cancellationToken);

    private async Task<DiagramModel> AnalyzeCoreAsync(
        IEnumerable<Project> selectedProjects,
        ArchitectureAnalysisSettings settings,
        CancellationToken cancellationToken)
    {
        var projects = selectedProjects?.Where(project => project is not null)
            .OrderBy(StableProjectKey, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.Name, System.StringComparer.Ordinal)
            .ToList()
            ?? new List<Project>();
        var typeBySymbol = new Dictionary<ISymbol, TypeNode>(SymbolEqualityComparer.Default);
        var typeByFullName = new Dictionary<string, TypeNode>();
        var registrationsByService = new Dictionary<string, Dictionary<string, INamedTypeSymbol>>(System.StringComparer.Ordinal);
        var projectTypes = new List<(Project Project, string ProjectId, List<TypeNode> Types)>();
        var projectCompilations = new List<(Project Project, Compilation Compilation)>();

        foreach (var project in projects)
        {
            Compilation? compilation;
            using (PerformanceAudit.Measure("Roslyn compilation acquisition"))
            {
                PerformanceAudit.Increment("Roslyn compilation requests");
                compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            }
            if (compilation is null)
            {
                continue;
            }

            projectCompilations.Add((project, compilation));
        }

        foreach (var (project, compilation) in projectCompilations)
        {
            var types = new List<TypeNode>();
            foreach (var type in GetNamedTypes(compilation.Assembly.GlobalNamespace)
                .OrderBy(type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), System.StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                PerformanceAudit.Increment("symbols inspected");

                if (type.TypeKind is not TypeKind.Class and not TypeKind.Interface)
                {
                    continue;
                }

                var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty);

                if (IsExcluded(type.Name, fullName, settings))
                {
                    continue;
                }

                var projectId = StableId.From("project", StableProjectKey(project));
                var node = new TypeNode(
                    StableId.From("type", fullName),
                    projectId,
                    type.Name,
                    fullName,
                    type.TypeKind.ToString(),
                    NewNodeId(),
                    type.Interfaces
                        .Select(item => item.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
                        .OrderBy(name => name)
                        .ToImmutableArray());

                types.Add(node);
                typeBySymbol[type.OriginalDefinition] = node;
                typeByFullName[fullName] = node;
            }

            projectTypes.Add((project, StableId.From("project", StableProjectKey(project)), types));
        }

        foreach (var (project, compilation) in projectCompilations)
        {
            foreach (var registration in await CollectServiceRegistrationsAsync(project, compilation, cancellationToken).ConfigureAwait(false))
            {
                if (SymbolEqualityComparer.Default.Equals(
                    registration.Service.OriginalDefinition,
                    registration.Implementation.OriginalDefinition))
                {
                    continue;
                }

                var serviceIdentity = TypeIdentity(registration.Service);
                var implementationIdentity = TypeIdentity(registration.Implementation);
                if (!registrationsByService.TryGetValue(serviceIdentity, out var implementations))
                    registrationsByService[serviceIdentity] = implementations =
                        new Dictionary<string, INamedTypeSymbol>(System.StringComparer.Ordinal);
                implementations[implementationIdentity] = registration.Implementation.OriginalDefinition;
            }
        }

        ApplyInterfaceResolution(projectTypes, typeBySymbol, typeByFullName, registrationsByService);

        var projectContainers = projectTypes
            .Select(p => new ProjectContainer(
                p.ProjectId,
                p.Project.Name,
                p.Types.ToImmutableArray(),
                NewNodeId()))
            .ToList();

        var externalDependencies = new Dictionary<string, ExternalDependencyNode>();
        var orderedExternalDependencies = new List<ExternalDependencyNode>();
        var edgeIds = new HashSet<string>();
        var edges = new List<DependencyEdge>();

        foreach (var (project, compilation) in projectCompilations)
        {
            foreach (var document in project.Documents.Where(d => d.SupportsSyntaxTree)
                .OrderBy(document => document.FilePath ?? document.Name, System.StringComparer.OrdinalIgnoreCase)
                .ThenBy(document => document.Name, System.StringComparer.Ordinal))
            {
                PerformanceAudit.Increment("syntax trees visited");
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (root is null)
                {
                    continue;
                }

                PerformanceAudit.Increment("semantic models requested");
                var model = compilation.GetSemanticModel(root.SyntaxTree);
                var usingNamespaces = root.DescendantNodes()
                    .OfType<UsingDirectiveSyntax>()
                    .Select(u => u.Name?.ToString())
                    .OfType<string>()
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();

                foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PerformanceAudit.Increment("symbols inspected");

                    if (model.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol sourceSymbol)
                    {
                        continue;
                    }

                    if (!TryFindInternalNode(sourceSymbol, typeBySymbol, typeByFullName, out var sourceNode))
                    {
                        continue;
                    }

                    foreach (var dependency in CollectConstructorDependencies(declaration, model, cancellationToken))
                    {
                        var resolvedDependency = ResolveRegisteredImplementation(dependency, registrationsByService);
                        if (SymbolEqualityComparer.Default.Equals(resolvedDependency, sourceSymbol))
                        {
                            continue;
                        }

                        if (TryFindInternalNode(resolvedDependency, typeBySymbol, typeByFullName, out var targetNode))
                        {
                            AddEdge(edges, edgeIds, sourceNode.Id, targetNode.Id, "internal");
                            continue;
                        }

                        var external = GetExternalDependency(resolvedDependency, externalDependencies, orderedExternalDependencies, usingNamespaces, settings);
                        if (external is not null)
                        {
                            AddEdge(edges, edgeIds, sourceNode.Id, external.Id, "external");
                        }
                    }
                }
            }
        }

        using (PerformanceAudit.Measure(
            "DiagramModel construction",
            inputNodes: projectContainers.Sum(project => project.Types.Count),
            inputRoutes: edges.Count,
            outputObjects: 1))
        {
            var discovered = new DiagramModel(
                projectContainers.Where(p => p.Types.Count > 0).ToImmutableArray(),
                orderedExternalDependencies.ToImmutableArray(),
                edges.ToImmutableArray(),
                new DiagramMetadata());
            return SemanticScopeSelector.Select(discovered, settings);
        }
    }

    private static ArchitectureAnalysisSettings ToArchitectureSettings(DiagramSettings settings) => new()
    {
        ExcludedNamespaces = settings.ExcludedNamespaces.ToList(),
        ExcludedNames = settings.ExcludedNames.ToList(),
        RootDiscoveryPatternsText = settings.RootDiscoveryPatternsText,
        ExternalDependencyTag = settings.ExternalDependencyTag
    };

    private static bool IsExcluded(string name, string fullName, ArchitectureAnalysisSettings settings)
    {
        var index = fullName.LastIndexOf('.');
        var @namespace = index <= 0 ? string.Empty : fullName.Substring(0, index);
        return settings.ExcludedNames.Any(pattern => GlobMatcher.IsMatch(name, pattern)) ||
               settings.ExcludedNames.Any(pattern => GlobMatcher.IsMatch(fullName, pattern)) ||
               settings.ExcludedNamespaces.Any(pattern => GlobMatcher.IsMatch(@namespace, pattern));
    }

    private static ArchitectureDiagramModel ToArchitectureDiagram(DiagramModel model)
    {
        var selection = model.Metadata?.SemanticSelection;
        return new ArchitectureDiagramModel(
            model.Projects.Select(project => new ArchitectureProject(
                project.Id,
                project.Name,
                project.Types.Select(type => new ArchitectureNode(
                    type.Id, type.ProjectId, type.Name, type.FullName, type.Kind, type.UniqueId,
                    type.Interfaces ?? System.Array.Empty<string>(), type.SemanticTypeIdentity,
                    type.InterfaceIdentity, type.ImplementationIdentity, type.ImplementationCount,
                    type.InterfaceResolution)).ToArray(),
                project.UniqueId)).ToArray(),
            model.ExternalDependencies.Select(node => new ArchitectureExternalNode(
                node.Id, node.Name, node.AssemblyName, node.UniqueId, node.FullName, node.Tag)).ToArray(),
            model.Edges.Select(edge => new ArchitectureLink(edge.Id, edge.SourceId, edge.TargetId, edge.Kind)).ToArray(),
            selection is null ? null : new ArchitectureSelectionDiagnostic(
                selection.ScopePolicy,
                selection.Roots.Select(root => new ArchitectureRoot(
                    root.SemanticNodeId, root.MatchedCanonicalValue, root.PatternIndex,
                    root.SourceLine, root.PatternText)).ToArray(),
                selection.SelectedNodeIds,
                selection.OmittedNodeIds,
                selection.SelectedLinkIds,
                selection.OmittedLinkIds,
                selection.UnmatchedPatternIndexes));
    }

    private static string StableProjectKey(Project project) =>
        project.FilePath ?? project.Name;

    private static IEnumerable<INamedTypeSymbol> GetNamedTypes(INamespaceOrTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers())
        {
            if (member is INamespaceOrTypeSymbol namespaceOrType)
            {
                foreach (var type in GetNamedTypes(namespaceOrType))
                {
                    yield return type;
                }
            }

            if (member is INamedTypeSymbol namedType)
            {
                yield return namedType;
            }
        }
    }

    private static async Task<IEnumerable<ServiceRegistration>> CollectServiceRegistrationsAsync(
        Project project,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var registrations = new List<ServiceRegistration>();
        foreach (var document in project.Documents.Where(d => d.SupportsSyntaxTree)
            .OrderBy(document => document.FilePath ?? document.Name, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(document => document.Name, System.StringComparer.Ordinal))
        {
            PerformanceAudit.Increment("syntax trees visited");
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                continue;
            }

            PerformanceAudit.Increment("semantic models requested");
            var model = compilation.GetSemanticModel(root.SyntaxTree);
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryGetServiceRegistrationTypeSyntax(invocation, out var serviceType, out var implementationType))
                {
                    continue;
                }

                if (model.GetTypeInfo(serviceType, cancellationToken).Type is INamedTypeSymbol service &&
                    ResolveRegistrationImplementation(invocation, implementationType, model, cancellationToken) is
                        INamedTypeSymbol implementation)
                {
                    registrations.Add(new ServiceRegistration(service.OriginalDefinition, implementation.OriginalDefinition));
                }
            }
        }

        return registrations;
    }

    private static bool TryGetServiceRegistrationTypeSyntax(
        InvocationExpressionSyntax invocation,
        out TypeSyntax serviceType,
        out TypeSyntax? implementationType)
    {
        serviceType = null!;
        implementationType = null;

        if (GetInvokedName(invocation.Expression) is not GenericNameSyntax genericName ||
            !IsRegistrationMethod(genericName.Identifier.ValueText) ||
            genericName.TypeArgumentList.Arguments.Count is < 1 or > 2)
        {
            return false;
        }

        serviceType = genericName.TypeArgumentList.Arguments[0];
        if (genericName.TypeArgumentList.Arguments.Count == 2)
            implementationType = genericName.TypeArgumentList.Arguments[1];
        return true;

        static bool IsRegistrationMethod(string name) => name is
            "AddScoped" or "AddTransient" or "AddSingleton" or
            "TryAddScoped" or "TryAddTransient" or "TryAddSingleton";

        static SimpleNameSyntax? GetInvokedName(ExpressionSyntax expression)
        {
            return expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
                MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
                SimpleNameSyntax simpleName => simpleName,
                _ => null
            };
        }
    }

    private static INamedTypeSymbol ResolveRegisteredImplementation(
        INamedTypeSymbol dependency,
        Dictionary<string, Dictionary<string, INamedTypeSymbol>> registrationsByService)
    {
        return registrationsByService.TryGetValue(TypeIdentity(dependency), out var implementations) &&
               implementations.Count == 1
            ? implementations.Values.Single()
            : dependency;
    }

    private static INamedTypeSymbol? ResolveRegistrationImplementation(
        InvocationExpressionSyntax invocation,
        TypeSyntax? implementationType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (implementationType is not null)
            return model.GetTypeInfo(implementationType, cancellationToken).Type as INamedTypeSymbol;

        foreach (var argument in invocation.ArgumentList.Arguments.Reverse())
        {
            var expression = argument.Expression switch
            {
                SimpleLambdaExpressionSyntax simple when simple.Body is ExpressionSyntax body => body,
                ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.Body is ExpressionSyntax body => body,
                _ => argument.Expression
            };
            if (model.GetTypeInfo(expression, cancellationToken).Type is INamedTypeSymbol implementation &&
                implementation.TypeKind == TypeKind.Class)
                return implementation;
        }

        return null;
    }

    private static void ApplyInterfaceResolution(
        IList<(Project Project, string ProjectId, List<TypeNode> Types)> projectTypes,
        Dictionary<ISymbol, TypeNode> typeBySymbol,
        Dictionary<string, TypeNode> typeByFullName,
        Dictionary<string, Dictionary<string, INamedTypeSymbol>> registrationsByService)
    {
        var replacements = new Dictionary<string, TypeNode>(System.StringComparer.Ordinal);
        var removed = new HashSet<string>(System.StringComparer.Ordinal);
        var uniqueInterfacesByImplementation = new Dictionary<string, List<INamedTypeSymbol>>(System.StringComparer.Ordinal);

        foreach (var entry in typeBySymbol.ToArray())
        {
            if (entry.Key is not INamedTypeSymbol symbol || symbol.TypeKind != TypeKind.Interface)
                continue;

            var node = entry.Value;
            if (!registrationsByService.TryGetValue(TypeIdentity(symbol), out var implementations) ||
                implementations.Count == 0)
            {
                replacements[node.Id] = node with
                {
                    SemanticTypeIdentity = node.FullName,
                    InterfaceIdentity = node.FullName,
                    ImplementationCount = 0,
                    InterfaceResolution = InterfaceResolutionStatus.Unresolved
                };
                continue;
            }

            if (implementations.Count > 1)
            {
                replacements[node.Id] = node with
                {
                    Name = $"{node.Name}\n({implementations.Count} implementations)",
                    SemanticTypeIdentity = node.FullName,
                    InterfaceIdentity = node.FullName,
                    ImplementationCount = implementations.Count,
                    InterfaceResolution = InterfaceResolutionStatus.Multiple
                };
                continue;
            }

            var implementation = implementations.Values.Single();
            var implementationIdentity = TypeIdentity(implementation);
            if (!uniqueInterfacesByImplementation.TryGetValue(implementationIdentity, out var services))
                uniqueInterfacesByImplementation[implementationIdentity] = services = new List<INamedTypeSymbol>();
            services.Add(symbol);
            removed.Add(node.Id);
        }

        foreach (var entry in typeBySymbol.ToArray())
        {
            if (entry.Key is not INamedTypeSymbol symbol || symbol.TypeKind != TypeKind.Class)
                continue;

            var node = entry.Value;
            if (!uniqueInterfacesByImplementation.TryGetValue(TypeIdentity(symbol), out var services))
            {
                replacements[node.Id] = node with { SemanticTypeIdentity = node.FullName };
                continue;
            }

            var ordered = services
                .OrderBy(service => service.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), System.StringComparer.Ordinal)
                .ToArray();
            var interfaceNames = string.Join(", ", ordered.Select(service => service.Name));
            var interfaceIdentities = string.Join(";", ordered.Select(service => service
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty)));
            replacements[node.Id] = node with
            {
                Name = $"{node.Name} : {interfaceNames}",
                SemanticTypeIdentity = node.FullName,
                InterfaceIdentity = interfaceIdentities,
                ImplementationIdentity = node.FullName,
                ImplementationCount = 1,
                InterfaceResolution = InterfaceResolutionStatus.Unique
            };
        }

        foreach (var project in projectTypes)
        {
            var updated = project.Types.Where(node => !removed.Contains(node.Id))
                .Select(node => replacements.TryGetValue(node.Id, out var replacement) ? replacement : node)
                .ToList();
            project.Types.Clear();
            project.Types.AddRange(updated);
        }

        foreach (var key in typeBySymbol.Keys.ToArray())
            if (removed.Contains(typeBySymbol[key].Id)) typeBySymbol.Remove(key);
            else if (replacements.TryGetValue(typeBySymbol[key].Id, out var replacement)) typeBySymbol[key] = replacement;
        foreach (var key in typeByFullName.Keys.ToArray())
            if (removed.Contains(typeByFullName[key].Id)) typeByFullName.Remove(key);
            else if (replacements.TryGetValue(typeByFullName[key].Id, out var replacement)) typeByFullName[key] = replacement;
    }

    private static string TypeIdentity(INamedTypeSymbol symbol) =>
        symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);

    private static IEnumerable<INamedTypeSymbol> CollectConstructorDependencies(
        TypeDeclarationSyntax declaration,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var ordered = new List<INamedTypeSymbol>();

        foreach (var parameter in GetPrimaryConstructorParameters(declaration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (parameter.Type is not null)
            {
                AddType(model.GetTypeInfo(parameter.Type, cancellationToken).Type);
            }
        }

        foreach (var constructor in declaration.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var parameter in constructor.ParameterList.Parameters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (parameter.Type is not null)
                {
                    AddType(model.GetTypeInfo(parameter.Type, cancellationToken).Type);
                }
            }
        }

        return ordered;

        static IEnumerable<ParameterSyntax> GetPrimaryConstructorParameters(TypeDeclarationSyntax typeDeclaration)
        {
            return typeDeclaration switch
            {
                ClassDeclarationSyntax { ParameterList: not null } classDeclaration => classDeclaration.ParameterList.Parameters,
                StructDeclarationSyntax { ParameterList: not null } structDeclaration => structDeclaration.ParameterList.Parameters,
                RecordDeclarationSyntax { ParameterList: not null } recordDeclaration => recordDeclaration.ParameterList.Parameters,
                _ => Enumerable.Empty<ParameterSyntax>()
            };
        }

        void AddType(ITypeSymbol? type)
        {
            if (type is null)
            {
                return;
            }

            if (type is IArrayTypeSymbol array)
            {
                AddType(array.ElementType);
                return;
            }

            if (type is IPointerTypeSymbol pointer)
            {
                AddType(pointer.PointedAtType);
                return;
            }

            if (type is INamedTypeSymbol named)
            {
                if (seen.Add(named.OriginalDefinition))
                {
                    ordered.Add(named.OriginalDefinition);
                }

                foreach (var argument in named.TypeArguments)
                {
                    AddType(argument);
                }
            }
        }
    }

    private static bool TryFindInternalNode(
        INamedTypeSymbol symbol,
        Dictionary<ISymbol, TypeNode> typeBySymbol,
        Dictionary<string, TypeNode> typeByFullName,
        out TypeNode node)
    {
        if (typeBySymbol.TryGetValue(symbol.OriginalDefinition, out node!))
        {
            return true;
        }

        var definition = symbol.OriginalDefinition;
        foreach (var candidate in typeBySymbol)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.Key, definition))
            {
                node = candidate.Value;
                return true;
            }
        }

        var fullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);
        if (typeByFullName.TryGetValue(fullName, out node!))
        {
            return true;
        }

        node = null!;
        return false;
    }

    private static ExternalDependencyNode? GetExternalDependency(
        INamedTypeSymbol symbol,
        Dictionary<string, ExternalDependencyNode> externalDependencies,
        List<ExternalDependencyNode> orderedExternalDependencies,
        IReadOnlyList<string> usingNamespaces,
        ArchitectureAnalysisSettings settings)
    {
        var assemblyName = symbol.ContainingAssembly?.Identity.Name;
        if (string.IsNullOrWhiteSpace(assemblyName) || IsImplicitFrameworkDependency(symbol, assemblyName!, usingNamespaces))
        {
            return null;
        }

        var fullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);
        var key = string.IsNullOrWhiteSpace(fullName) ? assemblyName! : fullName;
        if (!externalDependencies.TryGetValue(key, out var external))
        {
            external = new ExternalDependencyNode(
                StableId.From("external", key),
                symbol.Name,
                symbol.ContainingNamespace?.ToDisplayString() ?? assemblyName!,
                NewNodeId(),
                fullName,
                settings.ExternalDependencyTag);
            externalDependencies[key] = external;
            orderedExternalDependencies.Add(external);
        }

        return external;
    }

    private static bool IsImplicitFrameworkDependency(
        INamedTypeSymbol symbol,
        string assemblyName,
        IReadOnlyList<string> usingNamespaces)
    {
        if (assemblyName == "System.Private.CoreLib" ||
            assemblyName == "mscorlib" ||
            assemblyName == "netstandard")
        {
            return true;
        }

        if (!IsFrameworkAssembly(assemblyName))
        {
            return false;
        }

        var namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return true;
        }

        return !usingNamespaces.Any(u => namespaceName == u || namespaceName.StartsWith(u + ".", System.StringComparison.Ordinal));
    }

    private static bool IsFrameworkAssembly(string assemblyName)
    {
        return assemblyName.StartsWith("System.", System.StringComparison.Ordinal) ||
            assemblyName.StartsWith("Microsoft.", System.StringComparison.Ordinal);
    }

    private static void AddEdge(List<DependencyEdge> edges, HashSet<string> edgeIds, string sourceId, string targetId, string kind)
    {
        if (sourceId == targetId)
        {
            return;
        }

        var id = StableId.From("edge", $"{sourceId}->{targetId}:{kind}");
        if (edgeIds.Add(id))
        {
            edges.Add(new DependencyEdge(id, sourceId, targetId, kind));
        }
    }

    private static string NewNodeId()
    {
        return System.Guid.NewGuid().ToString("D");
    }

    private sealed record ServiceRegistration(INamedTypeSymbol Service, INamedTypeSymbol Implementation);
}
