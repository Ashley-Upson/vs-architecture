using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.DataModels;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.DataModels;

public sealed class RoslynDataModelAnalyser : IDataModelAnalyser
{
    public async Task<DataModelDiagram> AnalyseAsync(
        IEnumerable<Project> selectedProjects,
        DataModelAnalysisSettings settings,
        CancellationToken cancellationToken = default)
    {
        settings ??= new DataModelAnalysisSettings();
        var projects = selectedProjects.Where(project => project is not null)
            .OrderBy(project => project.FilePath ?? project.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.Name, StringComparer.Ordinal).ToArray();
        var discovered = new List<DiscoveredEntity>();

        // Pass 1: establish the complete eligible entity set and stable lookup.
        foreach (var project in projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null) continue;
            foreach (var type in GetNamedTypes(compilation.Assembly.GlobalNamespace)
                .OrderBy(CanonicalName, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullName = CanonicalName(type);
                if (IsExcluded(type.Name, fullName, settings) || !DataModelEntitySelectionPolicy.IsEligible(type))
                    continue;
                discovered.Add(new DiscoveredEntity(project, type, fullName,
                    StableId.From("data_model_entity", fullName)));
            }
        }

        var entityIdBySymbol = discovered.ToDictionary(
            item => (ISymbol)item.Type.OriginalDefinition,
            item => item.Id,
            SymbolEqualityComparer.Default);
        var entityIdByName = discovered.ToDictionary(item => item.FullName, item => item.Id, StringComparer.Ordinal);
        var relationships = new List<DataModelRelationship>();

        // Pass 2: properties resolve against the complete lookup from pass 1.
        var entities = discovered.OrderBy(item => item.FullName, StringComparer.Ordinal).Select(item =>
        {
            var properties = item.Type.GetMembers().OfType<IPropertySymbol>()
                .Where(DataModelEntitySelectionPolicy.IsCapturedProperty)
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => CreateProperty(item, property, entityIdBySymbol, entityIdByName, relationships))
                .ToImmutableArray();
            var location = item.Type.Locations.FirstOrDefault(candidate => candidate.IsInSource)?.GetLineSpan();
            return new DataModelEntity(
                item.Id,
                StableId.From("project", item.Project.FilePath ?? item.Project.Name),
                item.Project.Name,
                item.Type.Name,
                item.FullName,
                item.Type.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                item.Type.TypeKind.ToString(),
                item.Type.DeclaredAccessibility.ToString(),
                item.Type.IsAbstract,
                item.Type.IsSealed,
                item.Type.IsStatic,
                location is null || !location.Value.IsValid ? null : new DataModelSourceLocation(
                    location.Value.Path,
                    location.Value.StartLinePosition.Line + 1,
                    location.Value.StartLinePosition.Character + 1),
                DataModelEntitySelectionPolicy.SelectionReason,
                item.FullName,
                properties);
        }).ToImmutableArray();

        return new DataModelDiagram(
            entities,
            relationships.OrderBy(item => item.OrderKey, StringComparer.Ordinal).ToImmutableArray(),
            Array.Empty<DataModelDiagnostic>());
    }

    private static DataModelProperty CreateProperty(
        DiscoveredEntity entity,
        IPropertySymbol property,
        IReadOnlyDictionary<ISymbol, string> idBySymbol,
        IReadOnlyDictionary<string, string> idByName,
        ICollection<DataModelRelationship> relationships)
    {
        var (referenceType, isCollection) = Unwrap(property.Type);
        var targetId = ResolveId(referenceType, idBySymbol, idByName);
        var propertyId = StableId.From("data_model_property", $"{entity.FullName}.{property.Name}");
        var nullable = property.NullableAnnotation == NullableAnnotation.Annotated ||
            property.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
        var collectionElementId = isCollection ? targetId : null;
        if (targetId is not null)
        {
            var kind = isCollection
                ? DataModelRelationshipKind.CollectionPropertyReference
                : DataModelRelationshipKind.PropertyReference;
            relationships.Add(new DataModelRelationship(
                StableId.From("data_model_relationship", $"{entity.Id}|{propertyId}|{targetId}"),
                entity.Id,
                targetId,
                propertyId,
                kind,
                isCollection,
                nullable,
                $"Public instance property {entity.FullName}.{property.Name} has declared type {property.Type.ToDisplayString()}.",
                $"{entity.FullName}|{property.Name}|{targetId}"));
        }

        return new DataModelProperty(
            propertyId,
            entity.Id,
            property.Name,
            property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            targetId,
            property.DeclaredAccessibility.ToString(),
            property.IsStatic,
            property.GetMethod is not null,
            property.SetMethod is { IsInitOnly: false },
            property.SetMethod?.IsInitOnly == true,
            nullable,
            isCollection,
            collectionElementId,
            $"{entity.FullName}|{property.Name}");
    }

    private static string? ResolveId(
        ITypeSymbol? type,
        IReadOnlyDictionary<ISymbol, string> idBySymbol,
        IReadOnlyDictionary<string, string> idByName)
    {
        if (type is not INamedTypeSymbol named) return null;
        if (idBySymbol.TryGetValue(named.OriginalDefinition, out var id)) return id;
        var name = CanonicalName(named.OriginalDefinition);
        return idByName.TryGetValue(name, out id) ? id : null;
    }

    private static (ITypeSymbol Type, bool IsCollection) Unwrap(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return (array.ElementType, true);
        if (type is INamedTypeSymbol named && named.TypeArguments.Length == 1 &&
            named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic" &&
            named.Name is "IEnumerable" or "IReadOnlyList" or "IList" or "List" or "ICollection" or "Collection")
            return (named.TypeArguments[0], true);
        if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return (nullable.TypeArguments[0], false);
        return (type, false);
    }

    private static bool IsExcluded(string name, string fullName, DataModelAnalysisSettings settings) =>
        settings.ExcludedNames.Any(pattern => Wildcard(pattern, name)) ||
        settings.ExcludedNamespaces.Any(pattern => Wildcard(pattern, fullName));

    private static bool Wildcard(string pattern, string value) =>
        Regex.IsMatch(value, "^" + Regex.Escape(pattern ?? string.Empty).Replace("\\*", ".*") + "$",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    private static string CanonicalName(INamedTypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty);

    private static IEnumerable<INamedTypeSymbol> GetNamedTypes(INamespaceOrTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers())
        {
            if (member is INamespaceOrTypeSymbol nested)
                foreach (var type in GetNamedTypes(nested)) yield return type;
            if (member is INamedTypeSymbol named) yield return named;
        }
    }

    private sealed record DiscoveredEntity(Project Project, INamedTypeSymbol Type, string FullName, string Id);
}
