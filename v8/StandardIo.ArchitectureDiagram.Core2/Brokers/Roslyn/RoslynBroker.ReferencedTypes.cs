using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;
internal partial class RoslynBroker
{
    public IEnumerable<INamedTypeSymbol> GetReferencedTypes(Compilation compilation, INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var referenced = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        void Add(ITypeSymbol? symbol)
        {
            if (symbol is not INamedTypeSymbol named || named.TypeKind is not (TypeKind.Class or TypeKind.Interface) || named.SpecialType == SpecialType.System_String || named.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable)) return;
            var implementations = named.TypeKind == TypeKind.Interface ? GetDefinedTypes(compilation).Where(t => t.TypeKind == TypeKind.Class && !t.IsAbstract && t.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, named.OriginalDefinition))).ToArray() : [];
            if (implementations.Length == 0) referenced.Add(named.OriginalDefinition);
            else foreach (var implementation in implementations) referenced.Add(implementation.OriginalDefinition);
        }
        foreach (var constructor in type.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared))
            foreach (var parameter in constructor.Parameters) Add(parameter.Type);
        foreach (var declaration in type.DeclaringSyntaxReferences)
        {
            var syntax = declaration.GetSyntax(cancellationToken);
            var semantic = compilation.GetSemanticModel(syntax.SyntaxTree);
            foreach (var node in syntax.DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SymbolEqualityComparer.Default.Equals(semantic.GetEnclosingSymbol(node.SpanStart, cancellationToken)?.ContainingType, type)) continue;
                if (node is BaseObjectCreationExpressionSyntax creation) Add(semantic.GetTypeInfo(creation, cancellationToken).Type);
                if (node is TypeOfExpressionSyntax typeOf) Add(semantic.GetTypeInfo(typeOf.Type, cancellationToken).Type);
                // Generic arguments capture composition registrations without traversing the external library.
                if (node is InvocationExpressionSyntax invocation && semantic.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method)
                    foreach (var argument in method.TypeArguments) Add(argument);
            }
        }
        return referenced;
    }
}
