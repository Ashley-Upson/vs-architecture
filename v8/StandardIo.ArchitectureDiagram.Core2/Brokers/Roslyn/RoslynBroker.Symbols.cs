// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;
internal partial class RoslynBroker
{
    public IEnumerable<INamedTypeSymbol> GetDefinedTypes(Compilation compilation) =>
        GetNamespaceTypes(scope: compilation.Assembly.GlobalNamespace);

    private IEnumerable<INamedTypeSymbol> GetNamespaceTypes(INamespaceSymbol scope)
    {
        foreach (INamedTypeSymbol type in GetTypes(scope: scope))
        {
            foreach (INamedTypeSymbol nested in GetTypeDefinitions(type: type))
            {
                yield return nested;
            }
        }

        foreach (INamespaceSymbol child in GetNamespaces(scope: scope))
        {
            foreach (INamedTypeSymbol type in GetNamespaceTypes(scope: child))
            {
                yield return type;
            }
        }
    }

    private IEnumerable<INamedTypeSymbol> GetTypeDefinitions(INamedTypeSymbol type)
    {
        if (!type.IsImplicitlyDeclared && (type.TypeKind is TypeKind.Class or TypeKind.Interface or TypeKind.Struct or TypeKind.Enum))
        {
            yield return type;
        }

        foreach (INamedTypeSymbol child in GetNestedTypes(type: type))
        {
            foreach (INamedTypeSymbol nested in GetTypeDefinitions(type: child))
            {
                yield return nested;
            }
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

    public IEnumerable<(IMethodSymbol? Caller, IMethodSymbol Target, INamedTypeSymbol DependencyType, INamedTypeSymbol? ExecutorType, INamedTypeSymbol? RegistrationType)> GetCalls(Compilation compilation, INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var calls = GetRawCalls(compilation: compilation, type: type, cancellationToken: cancellationToken)
            .ToArray();

        var entries = calls.Select(selector: call => call.Caller)
            .Where(predicate: caller => caller is null || caller.DeclaredAccessibility == Accessibility.Public || caller.MethodKind is MethodKind.ExplicitInterfaceImplementation or MethodKind.Constructor or MethodKind.StaticConstructor or MethodKind.PropertyGet or MethodKind.PropertySet)
            .Distinct<IMethodSymbol?>(comparer: SymbolEqualityComparer.Default);

        INamedTypeSymbol[] implementations = GetDefinedTypes(compilation: compilation)
            .Where(predicate: candidate => candidate.TypeKind == TypeKind.Class && !candidate.IsAbstract)
            .ToArray();

        foreach (IMethodSymbol? entry in entries)
        {
            var pending = new Queue<(IMethodSymbol Target, INamedTypeSymbol DependencyType, INamedTypeSymbol? ExecutorType, INamedTypeSymbol? RegistrationType)>(collection: calls.Where(predicate: call => SymbolEqualityComparer.Default.Equals(x: call.Caller, y: entry))
                .Select(selector: call => (call.Target, call.DependencyType, call.ExecutorType, call.RegistrationType)));

            var visitedHelpers = new HashSet<IMethodSymbol>(comparer: SymbolEqualityComparer.Default);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var call = pending.Dequeue();
                IMethodSymbol target = call.Target;
                // Dependency endpoints use the same classifications as defined types.
                if (call.DependencyType.TypeKind is not (TypeKind.Class or TypeKind.Interface or TypeKind.Struct or TypeKind.Enum))
                {
                    continue;
                }

                bool isLocalHelper = SymbolEqualityComparer.Default.Equals(x: target.ContainingType.OriginalDefinition, y: type.OriginalDefinition) && (target.MethodKind == MethodKind.LocalFunction || target.MethodKind == MethodKind.Ordinary && target.DeclaredAccessibility != Accessibility.Public);

                if (isLocalHelper)
                {
                    if (!visitedHelpers.Add(item: target.OriginalDefinition))
                    {
                        continue;
                    }

                    foreach (var helperCall in calls.Where(predicate: call => SymbolEqualityComparer.Default.Equals(x: call.Caller?.OriginalDefinition, y: target.OriginalDefinition)))
                    {
                        pending.Enqueue(item: (helperCall.Target, helperCall.DependencyType, helperCall.ExecutorType ?? call.ExecutorType, helperCall.RegistrationType ?? call.RegistrationType));
                    }

                    continue;
                }

                IMethodSymbol[] resolved = ResolveImplementations(method: target, implementations: SymbolEqualityComparer.Default.Equals(target.ContainingType, call.DependencyType) ? implementations : Array.Empty<INamedTypeSymbol>())
                    .ToArray();

                if (resolved.Length == 0)
                {
                    yield return (entry, target.OriginalDefinition, call.DependencyType.OriginalDefinition, call.ExecutorType, call.RegistrationType);
                }
                else
                {
                    foreach (IMethodSymbol implementation in resolved)
                    {
                        yield return (entry, implementation.OriginalDefinition, implementation.ContainingType.OriginalDefinition, call.ExecutorType, call.RegistrationType);
                    }
                }
            }
        }
    }

    private static IEnumerable<IMethodSymbol> ResolveImplementations(IMethodSymbol method, INamedTypeSymbol[] implementations)
    {
        if (method.ContainingType.TypeKind != TypeKind.Interface)
        {
            yield break;
        }

        foreach (INamedTypeSymbol type in implementations)
        {
            foreach (INamedTypeSymbol contract in type.AllInterfaces.Where(predicate: contract => SymbolEqualityComparer.Default.Equals(x: contract, y: method.ContainingType) || type.IsGenericType && contract.TypeArguments.Any(predicate: argument => argument.TypeKind == TypeKind.TypeParameter) && SymbolEqualityComparer.Default.Equals(x: contract.OriginalDefinition, y: method.ContainingType.OriginalDefinition)))
            {
                IMethodSymbol? contractMethod = contract.GetMembers(name: method.Name)
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(predicate: candidate => SymbolEqualityComparer.Default.Equals(x: candidate.OriginalDefinition, y: method.OriginalDefinition));

                if (contractMethod is not null && type.FindImplementationForInterfaceMember(interfaceMember: contractMethod)is IMethodSymbol implementation && implementation.ContainingType.TypeKind == TypeKind.Class)
                {
                    IMethodSymbol resolved = ResolveOverride(type: type, implementation: implementation);

                    if (!resolved.IsAbstract)
                    {
                        yield return resolved;
                    }
                }
            }
        }
    }

    private static IMethodSymbol ResolveOverride(INamedTypeSymbol type, IMethodSymbol implementation)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (IMethodSymbol candidate in current.GetMembers(name: implementation.Name)
                .OfType<IMethodSymbol>())
            {
                for (IMethodSymbol? overridden = candidate.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
                {
                    if (SymbolEqualityComparer.Default.Equals(x: overridden.OriginalDefinition, y: implementation.OriginalDefinition))
                    {
                        return candidate;
                    }
                }
            }
        }

        return implementation;
    }

    private IEnumerable<(IMethodSymbol? Caller, IMethodSymbol Target, INamedTypeSymbol DependencyType, INamedTypeSymbol? ExecutorType, INamedTypeSymbol? RegistrationType)> GetRawCalls(Compilation compilation, INamedTypeSymbol type, CancellationToken cancellationToken)
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

                var target = GetSymbolInfo(model: semanticModel, node: invocation, cancellationToken: cancellationToken).Symbol as IMethodSymbol;
                if (target is null)
                {
                    // Dynamic dispatch has no statically classifiable dependency endpoint.
                    continue;
                }

                target = target.ReducedFrom ?? target;
                if (target.IsExtensionMethod && !SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly))
                {
                    continue;
                }

                var caller = enclosing as IMethodSymbol;

                while (caller is { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
                {
                    caller = caller.ContainingSymbol as IMethodSymbol;
                }

                // An inherited instance method consumes the receiver, not a separate instance of its base type.
                ITypeSymbol? receiver = semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation operation
                    ? operation.Instance?.Type
                    : invocation.Expression switch
                    {
                        MemberAccessExpressionSyntax member => semanticModel.GetTypeInfo(member.Expression, cancellationToken).Type,
                        SimpleNameSyntax => type,
                        _ => null
                    };
                var receiverType = !target.IsStatic && target.ContainingType.TypeKind == TypeKind.Class
                    && receiver is INamedTypeSymbol { TypeKind: TypeKind.Class } namedReceiver ? namedReceiver : null;
                foreach (var executor in GetCallbackExecutors(compilation, type, invocation, semanticModel, cancellationToken).DefaultIfEmpty())
                    yield return (caller, target, GetCollectionOwner(invocation, semanticModel) ?? receiverType ?? target.ContainingType, executor.Executor, executor.Registration);
            }
        }
    }

    private static INamedTypeSymbol? GetCollectionOwner(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax call)
        {
            return null;
        }

        ExpressionSyntax receiver = call.Expression;
        ITypeSymbol? receiverType = semanticModel.GetTypeInfo(receiver).Type;
        bool isCollection = receiverType is IArrayTypeSymbol || receiverType is INamedTypeSymbol named &&
            named.SpecialType != SpecialType.System_String &&
            (named.SpecialType == SpecialType.System_Collections_IEnumerable ||
             named.AllInterfaces.Any(contract => contract.SpecialType == SpecialType.System_Collections_IEnumerable));

        if (!isCollection)
        {
            return null;
        }

        ExpressionSyntax? owner = receiver switch
        {
            MemberAccessExpressionSyntax member => member.Expression,
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } => member.Expression,
            _ => null
        };

        return owner is null ? null : semanticModel.GetTypeInfo(owner).Type as INamedTypeSymbol;
    }

}
