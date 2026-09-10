// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;

internal partial class RoslynBroker
{
    public IEnumerable<INamedTypeSymbol> GetDefinedTypes(Compilation compilation) =>
        GetNamespaceTypes(scope: compilation.Assembly.GlobalNamespace);

    private IEnumerable<INamedTypeSymbol> GetNamespaceTypes(INamespaceSymbol scope)
    {
        foreach (INamedTypeSymbol type in GetTypes(scope: scope))
        {
            foreach (INamedTypeSymbol nested in GetTypeDefinitions(type: type)) yield return nested;
        }

        foreach (INamespaceSymbol child in GetNamespaces(scope: scope))
        {
            foreach (INamedTypeSymbol type in GetNamespaceTypes(scope: child)) yield return type;
        }
    }

    private IEnumerable<INamedTypeSymbol> GetTypeDefinitions(INamedTypeSymbol type)
    {
        if (!type.IsImplicitlyDeclared && (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Interface)) yield return type;

        foreach (INamedTypeSymbol child in GetNestedTypes(type: type))
        {
            foreach (INamedTypeSymbol nested in GetTypeDefinitions(type: child)) yield return nested;
        }
    }

    public IEnumerable<INamedTypeSymbol> GetBaseTypes(INamedTypeSymbol type)
    {
        if (type.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
        {
            yield return baseType.OriginalDefinition;
        }

        foreach (var interfaceType in type.Interfaces)
        {
            yield return interfaceType.OriginalDefinition;
        }
    }

    public IEnumerable<(IMethodSymbol? Caller, IMethodSymbol Target)> GetCalls(Compilation compilation, INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var calls = GetRawCalls(compilation: compilation, type: type, cancellationToken: cancellationToken).ToArray();
        var entries = calls.Select(call => call.Caller)
            .Where(caller => caller is null || caller.DeclaredAccessibility == Accessibility.Public
                || caller.MethodKind is MethodKind.ExplicitInterfaceImplementation or MethodKind.Constructor
                    or MethodKind.StaticConstructor or MethodKind.PropertyGet or MethodKind.PropertySet)
            .Distinct<IMethodSymbol?>(comparer: SymbolEqualityComparer.Default);
        INamedTypeSymbol[] implementations = GetDefinedTypes(compilation: compilation)
            .Where(candidate => candidate.TypeKind == TypeKind.Class && !candidate.IsAbstract).ToArray();

        foreach (IMethodSymbol? entry in entries)
        {
            var pending = new Queue<IMethodSymbol>(collection: calls
                .Where(call => SymbolEqualityComparer.Default.Equals(call.Caller, entry))
                .Select(call => call.Target));
            var visitedHelpers = new HashSet<IMethodSymbol>(comparer: SymbolEqualityComparer.Default);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IMethodSymbol target = pending.Dequeue();
                bool isLocalHelper = SymbolEqualityComparer.Default.Equals(target.ContainingType.OriginalDefinition, type.OriginalDefinition)
                    && (target.MethodKind == MethodKind.LocalFunction
                        || target.MethodKind == MethodKind.Ordinary && target.DeclaredAccessibility != Accessibility.Public);

                if (isLocalHelper)
                {
                    if (!visitedHelpers.Add(item: target.OriginalDefinition)) continue;

                    foreach (var helperCall in calls.Where(call => SymbolEqualityComparer.Default.Equals(call.Caller?.OriginalDefinition, target.OriginalDefinition)))
                    {
                        pending.Enqueue(item: helperCall.Target);
                    }

                    continue;
                }

                IMethodSymbol[] resolved = ResolveImplementations(method: target, implementations: implementations).ToArray();

                if (resolved.Length == 0)
                {
                    yield return (entry, target.OriginalDefinition);
                }
                else
                {
                    foreach (IMethodSymbol implementation in resolved)
                    {
                        yield return (entry, implementation.OriginalDefinition);
                    }
                }
            }
        }
    }

    private static IEnumerable<IMethodSymbol> ResolveImplementations(IMethodSymbol method, INamedTypeSymbol[] implementations)
    {
        if (method.ContainingType.TypeKind != TypeKind.Interface) yield break;

        foreach (INamedTypeSymbol type in implementations)
        {
            foreach (INamedTypeSymbol contract in type.AllInterfaces.Where(contract =>
                SymbolEqualityComparer.Default.Equals(contract, method.ContainingType)
                || type.IsGenericType && contract.TypeArguments.Any(argument => argument.TypeKind == TypeKind.TypeParameter)
                    && SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, method.ContainingType.OriginalDefinition)))
            {
                IMethodSymbol? contractMethod = contract.GetMembers(name: method.Name).OfType<IMethodSymbol>()
                    .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, method.OriginalDefinition));

                if (contractMethod is not null
                    && type.FindImplementationForInterfaceMember(interfaceMember: contractMethod) is IMethodSymbol implementation
                    && implementation.ContainingType.TypeKind == TypeKind.Class)
                {
                    IMethodSymbol resolved = ResolveOverride(type: type, implementation: implementation);
                    if (!resolved.IsAbstract) yield return resolved;
                }
            }
        }
    }

    private static IMethodSymbol ResolveOverride(INamedTypeSymbol type, IMethodSymbol implementation)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (IMethodSymbol candidate in current.GetMembers(name: implementation.Name).OfType<IMethodSymbol>())
            {
                for (IMethodSymbol? overridden = candidate.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
                {
                    if (SymbolEqualityComparer.Default.Equals(overridden.OriginalDefinition, implementation.OriginalDefinition)) return candidate;
                }
            }
        }

        return implementation;
    }

    private IEnumerable<(IMethodSymbol? Caller, IMethodSymbol Target)> GetRawCalls(Compilation compilation, INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        foreach (var declaration in type.DeclaringSyntaxReferences)
        {
            var syntax = GetSyntax(declaration: declaration, cancellationToken: cancellationToken);
            var semanticModel = GetSemanticModel(compilation: compilation, tree: syntax.SyntaxTree);

            foreach (var invocation in syntax.DescendantNodes()
                .OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var enclosing = GetEnclosingSymbol(model: semanticModel, position: invocation.SpanStart, cancellationToken: cancellationToken);

                if (!SymbolEqualityComparer.Default.Equals(x: enclosing?.ContainingType, y: type))
                {
                    continue;
                }

                var target = GetSymbolInfo(model: semanticModel, node: invocation, cancellationToken: cancellationToken).Symbol as IMethodSymbol ?? throw new NotSupportedException(message: $"Cannot statically resolve invocation at {invocation.GetLocation().GetLineSpan()}.");
                var caller = enclosing as IMethodSymbol;

                while (caller is { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
                {
                    caller = caller.ContainingSymbol as IMethodSymbol;
                }

                yield return (caller, target.ReducedFrom ?? target);
            }
        }
    }
}
