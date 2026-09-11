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

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Types;
internal sealed class ProjectTypesService : IProjectTypesService
{
    private readonly IRoslynBroker roslynBroker;
    public ProjectTypesService(IRoslynBroker roslynBroker) => this.roslynBroker = roslynBroker;
    public async Task PopulateTypesAsync(ProjectModel project, CancellationToken cancellationToken)
    {
        Compilation compilation = await roslynBroker.LoadCompilationAsync(projectFilePath: project.Path!, cancellationToken: cancellationToken);

        var errors = roslynBroker.GetDiagnostics(compilation: compilation, cancellationToken: cancellationToken)
            .Where(predicate: diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if (errors.Length > 0)
        {
            throw new InvalidOperationException(message: $"Project {project.Name} has compilation errors:\n{string.Join(separator: "\n", values: errors.Select(selector: error => error.ToString()))}");
        }

        INamedTypeSymbol[] definitions = roslynBroker.GetDefinedTypes(compilation: compilation)
            .ToArray();

        IEnumerable<INamedTypeSymbol> targets = definitions.SelectMany(selector: type => roslynBroker.GetBaseTypes(type: type)
            .Concat(second: roslynBroker.GetCalls(compilation: compilation, type: type, cancellationToken: cancellationToken)
            .Select(selector: call => call.DependencyType.OriginalDefinition)));

        var identities = new Dictionary<string, string>(comparer: StringComparer.Ordinal);
        var types = new List<DefinedType>();

        foreach (INamedTypeSymbol type in definitions.Concat(second: targets))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = roslynBroker.GetTypeName(type: type.OriginalDefinition);
            string identity = roslynBroker.GetTypeIdentity(type: type);

            if (identities.TryGetValue(key: name, value: out string? existing))
            {
                if (existing != identity)
                {
                    throw new NotSupportedException(message: $"Type name {name} occurs in different assemblies; the model cannot distinguish these dependency endpoints.");
                }

                continue;
            }

            identities.Add(key: name, value: identity);
            bool isInternal = SymbolEqualityComparer.Default.Equals(x: type.ContainingAssembly, y: compilation.Assembly);
            types.Add(item: CreateType(type: type, isInternal: isInternal));
        }

        project.Types = types.ToArray();
    }

    private DefinedType CreateType(INamedTypeSymbol type, bool isInternal)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Interface or TypeKind.Struct or TypeKind.Enum))
        {
            throw new NotSupportedException(message: $"Dependency target {roslynBroker.GetTypeName(type: type)} is {type.TypeKind}; the model supports Class, Interface, Struct and Enum.");
        }

        return new DefinedType
        {
            Name = roslynBroker.GetTypeName(type: type.OriginalDefinition),
            AssemblyName = isInternal ? null : type.ContainingAssembly.Identity.Name,
            FrameworkType = type.TypeKind switch { TypeKind.Interface => FrameworkType.Interface, TypeKind.Struct => FrameworkType.Struct, TypeKind.Enum => FrameworkType.Enum, _ => FrameworkType.Class },
            IsInternal = isInternal,
            IsDataType = type.IsValueType || type.SpecialType is SpecialType.System_String or SpecialType.System_Array or SpecialType.System_Enum or SpecialType.System_ValueType ||
                (!isInternal && type.ContainingNamespace.ToDisplayString().StartsWith("System.Collections", StringComparison.Ordinal) &&
                 (type.SpecialType == SpecialType.System_Collections_IEnumerable || type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T ||
                  type.AllInterfaces.Any(contract => contract.SpecialType == SpecialType.System_Collections_IEnumerable))),
            BaseTypeName = type.BaseType is { SpecialType: not SpecialType.System_Object } parent ? GetMemberTypeName(parent) : null,
            InterfaceNames = type.AllInterfaces.Length == 0 ? null : type.AllInterfaces.Select(GetMemberTypeName).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            Fields = isInternal ? roslynBroker.GetMembers(type: type)
                .OfType<IFieldSymbol>()
                .Where(predicate: field => !field.IsImplicitlyDeclared)
                .OrderBy(keySelector: field => field.Name, comparer: StringComparer.Ordinal)
                .Select(selector: field => new Field { Name = field.Name, Type = GetMemberTypeName(type: field.Type) })
                .ToArray() : Array.Empty<Field>(),
            Properties = isInternal ? roslynBroker.GetMembers(type: type)
                .OfType<IPropertySymbol>()
                .Where(predicate: property => !property.IsImplicitlyDeclared)
                .OrderBy(keySelector: property => property.Name, comparer: StringComparer.Ordinal)
                .Select(selector: property => new Property { Name = property.Name, Type = GetMemberTypeName(type: property.Type) })
                .ToArray() : Array.Empty<Property>(),
            Methods = roslynBroker.GetMembers(type: type)
                .OfType<IMethodSymbol>()
                .Where(predicate: method => !method.IsImplicitlyDeclared && (method.MethodKind == MethodKind.Ordinary && method.DeclaredAccessibility == Accessibility.Public || method.MethodKind == MethodKind.ExplicitInterfaceImplementation))
                .OrderBy(keySelector: method => method.Name, comparer: StringComparer.Ordinal)
                .Select(selector: method => new Method { Name = method.Name })
                .ToArray()
        };
    }

    private string GetMemberTypeName(ITypeSymbol type)
    {
        if (type is IDynamicTypeSymbol)
        {
            return "System.Object";
        }

        if (type is IArrayTypeSymbol array)
        {
            return GetMemberTypeName(type: array.ElementType) + "[" + new string (c: ',', count: array.Rank - 1) + "]";
        }

        if (type is IPointerTypeSymbol pointer)
        {
            return GetMemberTypeName(type: pointer.PointedAtType) + "*";
        }

        if (type is INamedTypeSymbol named)
        {
            named = named.TupleUnderlyingType ?? named;
            var prefix = named.ContainingType is null ? (named.ContainingNamespace.IsGlobalNamespace ? "" : named.ContainingNamespace.ToDisplayString() + ".") : GetMemberTypeName(type: named.ContainingType) + ".";
            var arguments = named.Arity == 0 ? "" : "<" + string.Join(separator: ", ", values: named.TypeArguments.Select(selector: argument => GetMemberTypeName(type: argument))) + ">";
            return prefix + named.Name + arguments;
        }

        return roslynBroker.GetTypeName(type: type);
    }
}