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
    public async Task<DiagramModel> AnalyzeAsync(
        Project selectedProject,
        DiagramSettings settings,
        CancellationToken cancellationToken = default)
    {
        return await AnalyzeAsync(new[] { selectedProject }, settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DiagramModel> AnalyzeAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken = default)
    {
        var projects = selectedProjects?.Where(project => project is not null).ToList()
            ?? new List<Project>();
        var resolver = new StyleResolver(settings);
        var typeBySymbol = new Dictionary<ISymbol, TypeNode>(SymbolEqualityComparer.Default);
        var typeByFullName = new Dictionary<string, TypeNode>();
        var registeredImplementationByService = new Dictionary<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
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
                    type.TypeKind.ToString(),
                    NewNodeId());

                types.Add(node);
                typeBySymbol[type.OriginalDefinition] = node;
                typeByFullName[fullName] = node;
            }

            projectTypes.Add((project, StableId.From("project", project.Id.Id.ToString()), types));
        }

        foreach (var project in projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

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

        return new DiagramModel(
            projectContainers.Where(p => p.Types.Count > 0).ToImmutableArray(),
            orderedExternalDependencies.ToImmutableArray(),
            edges.ToImmutableArray(),
            new DiagramMetadata());
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

    private static async Task<IEnumerable<ServiceRegistration>> CollectServiceRegistrationsAsync(
        Project project,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var registrations = new List<ServiceRegistration>();
        foreach (var document in project.Documents.Where(d => d.SupportsSyntaxTree))
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                continue;
            }

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
