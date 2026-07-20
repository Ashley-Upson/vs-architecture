using System.Linq;
using Microsoft.CodeAnalysis;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.DataModels;

internal static class DataModelEntitySelectionPolicy
{
    internal const string SelectionReason =
        "Class has at least one public instance property and no public instance ordinary methods.";

    public static bool IsEligible(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Class &&
        type.GetMembers().OfType<IPropertySymbol>().Any(IsCapturedProperty) &&
        !type.GetMembers().OfType<IMethodSymbol>().Any(IsCountedMethod);

    public static bool IsCapturedProperty(IPropertySymbol property) =>
        !property.IsStatic && property.DeclaredAccessibility == Accessibility.Public;

    private static bool IsCountedMethod(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Ordinary &&
        method.DeclaredAccessibility == Accessibility.Public &&
        !method.IsStatic;
}
