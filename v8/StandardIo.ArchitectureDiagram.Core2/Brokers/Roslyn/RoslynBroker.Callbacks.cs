// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;
internal partial class RoslynBroker
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation,
        System.Collections.Concurrent.ConcurrentDictionary<(SyntaxTree, int), (INamedTypeSymbol Executor, INamedTypeSymbol Registration)[]>> callbackExecutors = new();

    private IEnumerable<(INamedTypeSymbol Executor, INamedTypeSymbol Registration)> GetCallbackExecutors(Compilation compilation, INamedTypeSymbol source,
        InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellationToken)
    {
        var lambda = call.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().FirstOrDefault();
        if (lambda is null) return System.Array.Empty<(INamedTypeSymbol Executor, INamedTypeSymbol Registration)>();
        return callbackExecutors.GetOrCreateValue(compilation).GetOrAdd((call.SyntaxTree, lambda.SpanStart),
            _ => ResolveCallbackExecutors(compilation, source, call, model, cancellationToken).ToArray());
    }

    private IEnumerable<(INamedTypeSymbol Executor, INamedTypeSymbol Registration)> ResolveCallbackExecutors(Compilation compilation, INamedTypeSymbol source,
        InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var lambda in call.Ancestors().OfType<AnonymousFunctionExpressionSyntax>())
        {
            var argument = lambda.Parent as ArgumentSyntax;
            if (argument is null || model.GetOperation(argument, cancellationToken) is not IArgumentOperation
                { Parameter: { } parameter, Parent: IInvocationOperation registration }) yield break;

            var executors = TraceCallback(compilation, registration.TargetMethod, parameter.Ordinal,
                registration.Instance?.Type as INamedTypeSymbol ?? registration.TargetMethod.ContainingType,
                source, new HashSet<(IMethodSymbol, int)>(), cancellationToken).Distinct().ToArray();
            if (executors.Length == 0) yield break;
            var otherExecutors = executors.Where(executor => !SymbolEqualityComparer.Default.Equals(executor.Executor, source)).ToArray();
            if (otherExecutors.Length == 0) continue;
            foreach (var executor in otherExecutors) yield return executor;
            yield break;
        }
    }

    private IEnumerable<(INamedTypeSymbol Executor, INamedTypeSymbol Registration)> TraceCallback(Compilation compilation, IMethodSymbol method, int ordinal,
        INamedTypeSymbol receiver, INamedTypeSymbol registrationType, HashSet<(IMethodSymbol, int)> visited, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        method = method.OriginalDefinition;
        if (!visited.Add((method, ordinal))) yield break;
        if (!SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly))
        {
            yield return (receiver, registrationType);
            yield break;
        }
        var implementations = ResolveImplementations(method, GetDefinedTypes(compilation)
            .Where(type => type.TypeKind == TypeKind.Class && !type.IsAbstract).ToArray()).ToArray();
        if (implementations.Length > 0)
        {
            foreach (var implementation in implementations)
            foreach (var executor in TraceCallback(compilation, implementation, ordinal, implementation.ContainingType, registrationType,
                new HashSet<(IMethodSymbol, int)>(visited), cancellationToken)) yield return executor;
            yield break;
        }
        if (ordinal >= method.Parameters.Length) yield break;
        var parameter = method.Parameters[ordinal];
        var aliases = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { parameter };
        ISymbol? ReferencedSymbol(IOperation? operation)
        {
            while (operation is IConversionOperation conversion) operation = conversion.Operand;
            return operation switch
            {
                IParameterReferenceOperation reference => reference.Parameter.OriginalDefinition,
                ILocalReferenceOperation reference => reference.Local,
                IFieldReferenceOperation reference => reference.Field.OriginalDefinition,
                IPropertyReferenceOperation reference => reference.Property.OriginalDefinition,
                _ => null
            };
        }
        bool RefersToParameter(IOperation? operation)
        {
            var symbol = ReferencedSymbol(operation);
            return symbol is not null && aliases.Contains(symbol);
        }
        var declarations = method.DeclaringSyntaxReferences.ToArray();
        bool changed;
        do
        {
            changed = false;
            foreach (var declaration in declarations)
            {
                var syntax = declaration.GetSyntax(cancellationToken);
                var model = compilation.GetSemanticModel(syntax.SyntaxTree);
                foreach (var assignment in syntax.DescendantNodes().Where(node => node is AssignmentExpressionSyntax or VariableDeclaratorSyntax))
                {
                    var operation = model.GetOperation(assignment, cancellationToken);
                    ISymbol? alias = operation switch
                    {
                        ISimpleAssignmentOperation set when RefersToParameter(set.Value) => ReferencedSymbol(set.Target),
                        IVariableDeclaratorOperation local when RefersToParameter(local.Initializer?.Value) => local.Symbol,
                        _ => null
                    };
                    if (alias is null || alias is IFieldSymbol or IPropertySymbol
                        && !SymbolEqualityComparer.Default.Equals(alias.ContainingType, method.ContainingType)) continue;
                    changed |= aliases.Add(alias);
                }
            }
            if (aliases.Any(alias => alias is IFieldSymbol or IPropertySymbol)) declarations = method.ContainingType.DeclaringSyntaxReferences.ToArray();
        } while (changed);
        foreach (var declaration in declarations)
        {
            var syntax = declaration.GetSyntax(cancellationToken);
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (model.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation) continue;
                if (operation.TargetMethod.MethodKind == MethodKind.DelegateInvoke && RefersToParameter(operation.Instance))
                {
                    yield return (receiver, registrationType);
                    continue;
                }
                foreach (var argument in operation.Arguments.Where(argument => argument.Parameter is not null && RefersToParameter(argument.Value)))
                foreach (var executor in TraceCallback(compilation, operation.TargetMethod, argument.Parameter!.Ordinal,
                    operation.Instance?.Type as INamedTypeSymbol ?? operation.TargetMethod.ContainingType, receiver,
                    new HashSet<(IMethodSymbol, int)>(visited), cancellationToken)) yield return executor;
            }
        }
    }
}
