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
        var legacySettings = new DiagramSettings
        {
            ExcludedNamespaces = settings.ExcludedNamespaces.ToList(),
            ExcludedNames = settings.ExcludedNames.ToList(),
            RootDiscoveryPatternsText = settings.RootDiscoveryPatternsText,
            ExternalDependencyTag = settings.ExternalDependencyTag
        };
        var model = await AnalyzeCoreAsync(selectedProjects, legacySettings, cancellationToken).ConfigureAwait(false);
        return ToArchitectureDiagram(model);
    }

    public async Task<DiagramModel> AnalyzeAsync(
        Project selectedProject,
        DiagramSettings settings,
        CancellationToken cancellationToken = default)
    {
        return await AnalyzeCoreAsync(new[] { selectedProject }, settings, cancellationToken).ConfigureAwait(false);
    }

    public Task<DiagramModel> AnalyzeAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken = default) =>
        AnalyzeCoreAsync(selectedProjects, settings, cancellationToken);

    private async Task<DiagramModel> AnalyzeCoreAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken)
    {
        var projects = selectedProjects?.Where(project => project is not null)
            .OrderBy(StableProjectKey, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.Name, System.StringComparer.Ordinal)
            .ToList()
            ?? new List<Project>();
        var resolver = new StyleResolver(settings);
        var typeBySymbol = new Dictionary<ISymbol, TypeNode>(SymbolEqualityComparer.Default);
        var typeByFullName = new Dictionary<string, TypeNode>();
        var registeredImplementationByService = new Dictionary<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
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

                if (resolver.IsExcluded(type.Name, fullName))
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
                        .ToImmutableArray(),
                    CollectProperties(type, typeByFullName),
                    CountMethods(type));

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
                if (!SymbolEqualityComparer.Default.Equals(registration.Service.OriginalDefinition, registration.Implementation.OriginalDefinition) &&
                    !registeredImplementationByService.ContainsKey(registration.Service.OriginalDefinition))
                {
                    registeredImplementationByService[registration.Service.OriginalDefinition] = registration.Implementation.OriginalDefinition;
                }
            }
        }

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
                        var resolvedDependency = ResolveRegisteredImplementation(dependency, registeredImplementationByService);
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

    private static ArchitectureDiagramModel ToArchitectureDiagram(DiagramModel model)
    {
        var selection = model.Metadata?.SemanticSelection;
        return new ArchitectureDiagramModel(
            model.Projects.Select(project => new ArchitectureProject(
                project.Id,
                project.Name,
                project.Types.Select(type => new ArchitectureNode(
                    type.Id, type.ProjectId, type.Name, type.FullName, type.Kind, type.UniqueId,
                    type.Interfaces ?? System.Array.Empty<string>())).ToArray(),
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
                    model.GetTypeInfo(implementationType, cancellationToken).Type is INamedTypeSymbol implementation)
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
        out TypeSyntax implementationType)
    {
        serviceType = null!;
        implementationType = null!;

        if (GetInvokedName(invocation.Expression) is not GenericNameSyntax genericName ||
            genericName.TypeArgumentList.Arguments.Count != 2 ||
            genericName.Identifier.ValueText is not ("AddScoped" or "AddTransient" or "AddSingleton"))
        {
            return false;
        }

        serviceType = genericName.TypeArgumentList.Arguments[0];
        implementationType = genericName.TypeArgumentList.Arguments[1];
        return true;

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
        Dictionary<ISymbol, INamedTypeSymbol> registeredImplementationByService)
    {
        return registeredImplementationByService.TryGetValue(dependency.OriginalDefinition, out var implementation)
            ? implementation
            : dependency;
    }

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

    private static IReadOnlyList<TypeProperty> CollectProperties(
        INamedTypeSymbol type,
        Dictionary<string, TypeNode> typeByFullName)
    {
        return type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property => !property.IsStatic && property.DeclaredAccessibility == Accessibility.Public)
            .Select(property =>
            {
                var propertyType = UnwrapPropertyType(property.Type);
                var fullName = propertyType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty);
                var typeName = propertyType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ??
                    property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                var typeId = fullName is not null && typeByFullName.TryGetValue(fullName, out var node)
                    ? node.Id
                    : null;

                return new TypeProperty(property.Name, typeName, fullName, typeId);
            })
            .OrderBy(property => property.Name)
            .ToImmutableArray();
    }

    private static INamedTypeSymbol? UnwrapPropertyType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return UnwrapPropertyType(array.ElementType);
        }

        if (type is INamedTypeSymbol named &&
            named.TypeArguments.Length == 1 &&
            named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic" &&
            named.Name is "IEnumerable" or "IReadOnlyList" or "IList" or "List" or "ICollection" or "Collection")
        {
            return UnwrapPropertyType(named.TypeArguments[0]);
        }

        return type as INamedTypeSymbol;
    }

    private static int CountMethods(INamedTypeSymbol type)
    {
        return type.GetMembers()
            .OfType<IMethodSymbol>()
            .Count(method =>
                method.MethodKind == MethodKind.Ordinary &&
                method.DeclaredAccessibility == Accessibility.Public &&
                !method.IsStatic);
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
        DiagramSettings settings)
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
