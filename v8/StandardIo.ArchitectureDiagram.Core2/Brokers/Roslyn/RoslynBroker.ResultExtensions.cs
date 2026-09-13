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
    private static ExpressionSyntax? GetExtensionReceiver(InvocationExpressionSyntax invocation, SemanticModel semantic, IMethodSymbol method)
    {
        if (semantic.GetOperation(invocation) is IInvocationOperation operation)
            return operation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 0)?.Value.Syntax as ExpressionSyntax;
        return method.ReducedFrom is not null && invocation.Expression is MemberAccessExpressionSyntax member
            ? member.Expression : invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
    }
    private static bool IsDependencyResult(ExpressionSyntax expression, Compilation compilation, INamedTypeSymbol consumer,
        HashSet<ISymbol> visited, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var semantic = compilation.GetSemanticModel(expression.SyntaxTree);
        bool Trace(ExpressionSyntax value) => IsDependencyResult(value, compilation, consumer, visited, cancellationToken);
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized: return Trace(parenthesized.Expression);
            case CastExpressionSyntax cast: return Trace(cast.Expression);
            case AwaitExpressionSyntax awaited: return Trace(awaited.Expression);
            case MemberAccessExpressionSyntax member: return Trace(member.Expression);
            case ConditionalAccessExpressionSyntax conditional: return Trace(conditional.Expression);
            case ConditionalExpressionSyntax conditional: return Trace(conditional.WhenTrue) && Trace(conditional.WhenFalse);
            case InvocationExpressionSyntax invocation:
                if (semantic.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method) return false;
                if (method.IsExtensionMethod)
                    return GetExtensionReceiver(invocation, semantic, method) is { } receiver && Trace(receiver);
                if (!SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, consumer.OriginalDefinition))
                    return method.ContainingType.TypeKind is TypeKind.Class or TypeKind.Interface;
                if (!visited.Add(method.OriginalDefinition)) return false;
                // Follow only local return expressions; never inspect the implementation of a dependency.
                var returns = method.DeclaringSyntaxReferences.Select(r => r.GetSyntax(cancellationToken))
                    .OfType<MethodDeclarationSyntax>().SelectMany(declaration => declaration.ExpressionBody is { } body
                        ? new[] { body.Expression } : declaration.Body?.DescendantNodes(n => n is not AnonymousFunctionExpressionSyntax && n is not LocalFunctionStatementSyntax)
                            .OfType<ReturnStatementSyntax>().Select(r => r.Expression).OfType<ExpressionSyntax>() ?? [])
                    .ToArray();
                return returns.Length > 0 && returns.All(Trace);
            case IdentifierNameSyntax identifier:
                if (semantic.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local || !visited.Add(local)) return false;
                var declaration = local.DeclaringSyntaxReferences.Select(r => r.GetSyntax(cancellationToken)).OfType<VariableDeclaratorSyntax>().FirstOrDefault();
                if (declaration?.Initializer is not { } initializer) return false;
                // A reassigned local needs control-flow analysis; do not guess its origin.
                var scope = declaration.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
                if (scope?.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(a => a.SpanStart < identifier.SpanStart && SymbolEqualityComparer.Default.Equals(semantic.GetSymbolInfo(a.Left, cancellationToken).Symbol, local)) == true) return false;
                return Trace(initializer.Value);
            default: return false;
        }
    }
}
