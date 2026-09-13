// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;
internal interface IRoslynBroker
{
    IEnumerable<INamedTypeSymbol> GetDefinedTypes(Compilation compilation);

    IEnumerable<INamedTypeSymbol> GetBaseTypes(INamedTypeSymbol type);

    IEnumerable<INamedTypeSymbol> GetReferencedTypes(Compilation compilation, INamedTypeSymbol type, CancellationToken cancellationToken);

    IEnumerable<(IMethodSymbol? Caller, IMethodSymbol Target, INamedTypeSymbol DependencyType)> GetCalls(Compilation compilation, INamedTypeSymbol type, CancellationToken cancellationToken);

    Task<Compilation> LoadCompilationAsync(string projectFilePath, CancellationToken cancellationToken);

    ImmutableArray<Diagnostic> GetDiagnostics(Compilation compilation, CancellationToken cancellationToken);

    ImmutableArray<INamedTypeSymbol> GetTypes(INamespaceSymbol scope);

    IEnumerable<INamespaceSymbol> GetNamespaces(INamespaceSymbol scope);

    ImmutableArray<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type);

    ImmutableArray<ISymbol> GetMembers(INamedTypeSymbol type);

    SyntaxNode GetSyntax(SyntaxReference declaration, CancellationToken cancellationToken);

    SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree tree);

    ISymbol? GetEnclosingSymbol(SemanticModel model, int position, CancellationToken cancellationToken);

    SymbolInfo GetSymbolInfo(SemanticModel model, SyntaxNode node, CancellationToken cancellationToken);

    string GetTypeName(ITypeSymbol type);

    string GetTypeIdentity(INamedTypeSymbol type);
}