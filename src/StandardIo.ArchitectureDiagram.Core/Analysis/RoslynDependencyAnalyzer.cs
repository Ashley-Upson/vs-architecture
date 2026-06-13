using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Analysis;

public sealed class RoslynDependencyAnalyzer
{
    public async Task<ArchitectureGraph> AnalyzeAsync(
        Project selectedProject,
        DiagramSettings settings,
        CancellationToken cancellationToken = default)
    {
        return await AnalyzeAsync(new[] { selectedProject }, settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ArchitectureGraph> AnalyzeAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken = default)
    {
        var projects = CollectSelectedAndReferencedProjects(selectedProjects);
        var resolver = new StyleResolver(settings);
        var typeBySymbol = new Dictionary<ISymbol, TypeNode>(SymbolEqualityComparer.Default);
        var typeByFullName = new Dictionary<string, TypeNode>();
        var symbolByNodeId = new Dictionary<string, INamedTypeSymbol>();
        var projectTypes = new List<(Project Project, string ProjectId, List<TypeNode> Types)>();

        foreach (var project in projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            var types = new List<TypeNode>();
            foreach (var type in GetNamedTypes(compilation.Assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();

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

                var projectId = StableId.From("project", project.Id.Id.ToString());
                var node = new TypeNode(
                    StableId.From("type", fullName),
                    projectId,
                    type.Name,
                    fullName,
                    type.TypeKind.ToString());

                types.Add(node);
                typeBySymbol[type.OriginalDefinition] = node;
                typeByFullName[fullName] = node;
                symbolByNodeId[node.Id] = type.OriginalDefinition;
            }

            projectTypes.Add((project, StableId.From("project", project.Id.Id.ToString()), types));
        }

        var implementedInterfaceNodes = MapImplementedInterfaces(typeBySymbol, symbolByNodeId);
        var hiddenInterfaceIds = new HashSet<string>(implementedInterfaceNodes.Keys);
        var projectContainers = projectTypes
            .Select(p => new ProjectContainer(
                p.ProjectId,
                p.Project.Name,
                p.Types.Where(t => !hiddenInterfaceIds.Contains(t.Id)).ToImmutableArray()))
            .ToList();

        var externalDependencies = new Dictionary<string, ExternalDependencyNode>();
        var edges = new Dictionary<string, DependencyEdge>();

        foreach (var project in projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            foreach (var document in project.Documents.Where(d => d.SupportsSyntaxTree))
            {
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (root is null)
                {
                    continue;
                }

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

                    if (model.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol sourceSymbol)
                    {
                        continue;
                    }

                    if (!TryFindInternalNode(sourceSymbol, typeBySymbol, typeByFullName, out var sourceNode))
                    {
                        continue;
                    }

                    sourceNode = ResolveVisibleNode(sourceNode, implementedInterfaceNodes);

                    foreach (var dependency in CollectDependencies(declaration, model, cancellationToken))
                    {
                        if (SymbolEqualityComparer.Default.Equals(dependency, sourceSymbol))
                        {
                            continue;
                        }

                        if (TryFindInternalNode(dependency, typeBySymbol, typeByFullName, out var targetNode))
                        {
                            targetNode = ResolveVisibleNode(targetNode, implementedInterfaceNodes);

                            AddEdge(edges, sourceNode.Id, targetNode.Id, "internal");
                            continue;
                        }

                        var external = GetExternalDependency(dependency, externalDependencies, usingNamespaces);
                        if (external is not null)
                        {
                            AddEdge(edges, sourceNode.Id, external.Id, "external");
                        }
                    }
                }
            }
        }

        return new ArchitectureGraph(
            projectContainers.Where(p => p.Types.Count > 0).ToImmutableArray(),
            externalDependencies.Values.ToImmutableArray(),
            edges.Values.ToImmutableArray());
    }

    private static IReadOnlyList<Project> CollectSelectedAndReferencedProjects(IEnumerable<Project> selectedProjects)
    {
        var visited = new HashSet<ProjectId>();
        var ordered = new List<Project>();

        void Visit(Project project)
        {
            if (!visited.Add(project.Id))
            {
                return;
            }

            ordered.Add(project);

            foreach (var reference in project.ProjectReferences)
            {
                var referenced = project.Solution.GetProject(reference.ProjectId);
                if (referenced is not null)
                {
                    Visit(referenced);
                }
            }
        }

        foreach (var project in selectedProjects)
        {
            Visit(project);
        }

        return ordered;
    }

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

    private static IEnumerable<INamedTypeSymbol> CollectDependencies(
        TypeDeclarationSyntax declaration,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var ordered = new List<INamedTypeSymbol>();

        foreach (var baseType in declaration.BaseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>())
        {
            AddType(model.GetTypeInfo(baseType.Type, cancellationToken).Type);
        }

        foreach (var node in declaration.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (node)
            {
                case ObjectCreationExpressionSyntax creation:
                    AddType(model.GetTypeInfo(creation, cancellationToken).Type);
                    break;
                case TypeSyntax typeSyntax:
                    AddType(model.GetTypeInfo(typeSyntax, cancellationToken).Type);
                    break;
            }
        }

        return ordered;

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

    private static Dictionary<string, TypeNode> MapImplementedInterfaces(
        Dictionary<ISymbol, TypeNode> typeBySymbol,
        Dictionary<string, INamedTypeSymbol> symbolByNodeId)
    {
        var implementedInterfaceNodes = new Dictionary<string, TypeNode>();
        foreach (var nodeSymbol in symbolByNodeId)
        {
            var implementationSymbol = nodeSymbol.Value;
            if (implementationSymbol.TypeKind != TypeKind.Class ||
                !typeBySymbol.TryGetValue(implementationSymbol, out var implementationNode))
            {
                continue;
            }

            foreach (var interfaceSymbol in implementationSymbol.AllInterfaces)
            {
                if (typeBySymbol.TryGetValue(interfaceSymbol.OriginalDefinition, out var interfaceNode) &&
                    !implementedInterfaceNodes.ContainsKey(interfaceNode.Id))
                {
                    implementedInterfaceNodes[interfaceNode.Id] = implementationNode;
                }
            }
        }

        return implementedInterfaceNodes;
    }

    private static TypeNode ResolveVisibleNode(
        TypeNode node,
        Dictionary<string, TypeNode> implementedInterfaceNodes)
    {
        return implementedInterfaceNodes.TryGetValue(node.Id, out var visibleNode)
            ? visibleNode
            : node;
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
        IReadOnlyList<string> usingNamespaces)
    {
        var assemblyName = symbol.ContainingAssembly?.Identity.Name;
        if (string.IsNullOrWhiteSpace(assemblyName) || IsImplicitFrameworkDependency(symbol, assemblyName!, usingNamespaces))
        {
            return null;
        }

        if (!externalDependencies.TryGetValue(assemblyName!, out var external))
        {
            external = new ExternalDependencyNode(
                StableId.From("external", assemblyName!),
                assemblyName!,
                assemblyName!);
            externalDependencies[assemblyName!] = external;
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

    private static void AddEdge(Dictionary<string, DependencyEdge> edges, string sourceId, string targetId, string kind)
    {
        if (sourceId == targetId)
        {
            return;
        }

        var id = StableId.From("edge", $"{sourceId}->{targetId}:{kind}");
        edges[id] = new DependencyEdge(id, sourceId, targetId, kind);
    }
}
