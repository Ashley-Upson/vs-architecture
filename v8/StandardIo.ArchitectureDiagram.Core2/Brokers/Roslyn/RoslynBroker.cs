// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;

internal partial class RoslynBroker : IRoslynBroker
{
    private static readonly SymbolDisplayFormat typeNameFormat = new SymbolDisplayFormat(globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted, typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces, genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters, miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable);


    public ImmutableArray<Diagnostic> GetDiagnostics(Compilation compilation, CancellationToken cancellationToken) => compilation.GetDiagnostics(cancellationToken: cancellationToken);

    public ImmutableArray<INamedTypeSymbol> GetTypes(INamespaceSymbol scope) => scope.GetTypeMembers();

    public IEnumerable<INamespaceSymbol> GetNamespaces(INamespaceSymbol scope) => scope.GetNamespaceMembers();

    public ImmutableArray<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type) => type.GetTypeMembers();

    public ImmutableArray<ISymbol> GetMembers(INamedTypeSymbol type) => type.GetMembers();

    public SyntaxNode GetSyntax(SyntaxReference declaration, CancellationToken cancellationToken) => declaration.GetSyntax(cancellationToken: cancellationToken);

    public SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree tree) => compilation.GetSemanticModel(syntaxTree: tree);

    public ISymbol? GetEnclosingSymbol(SemanticModel model, int position, CancellationToken cancellationToken) => model.GetEnclosingSymbol(position: position, cancellationToken: cancellationToken);

    public SymbolInfo GetSymbolInfo(SemanticModel model, SyntaxNode node, CancellationToken cancellationToken) => model.GetSymbolInfo(node: node, cancellationToken: cancellationToken);

    public string GetTypeName(ITypeSymbol type) => type.ToDisplayString(format: typeNameFormat);

    public string GetTypeIdentity(INamedTypeSymbol type) => type.ContainingAssembly.Identity + "|" + GetTypeName(type: type.OriginalDefinition);
}
