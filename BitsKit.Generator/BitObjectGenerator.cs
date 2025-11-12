using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BitsKit.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class BitObjectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<TypeSymbolProcessor> typeDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                StringConstants.BitObjectAttributeFullName,
                predicate: IsValidTypeDeclaration,
                transform: ProcessSyntaxNode)
            .Where(x => x is not null)
            .WithTrackingName("Main")!;

        context.RegisterSourceOutput(typeDeclarations, GenerateSourceCode);
    }

    private static TypeSymbolProcessor? ProcessSyntaxNode(GeneratorAttributeSyntaxContext syntaxContext, CancellationToken token)
    {
        if (syntaxContext.TargetNode is not TypeDeclarationSyntax typeDeclaration)
            return null;

        ISymbol? symbol = syntaxContext.SemanticModel.GetDeclaredSymbol(typeDeclaration, token);

        if (symbol is not INamedTypeSymbol typeSymbol)
            return null;

        AttributeData attribute = typeSymbol
            .GetAttributes()
            .Single(a => a.AttributeClass?.ToDisplayString() == StringConstants.BitObjectAttributeFullName);

        return new(typeSymbol, attribute);
    }

    private static void GenerateSourceCode(SourceProductionContext context, TypeSymbolProcessor processor)
    {
        StringBuilder stringBuilder = new(StringConstants.Header);

        // print the current namespace
        if (processor.Namespace is not null)
            stringBuilder
                .AppendLine($"namespace {processor.Namespace}")
                .AppendLine("{");

        processor.GenerateCSharpSource(stringBuilder);

        // apply closing namespace bracket
        if (processor.Namespace is not null)
            stringBuilder.AppendLine("}");

        context.AddSource($"{processor.FullName}.g", stringBuilder.ToString());
    }

    private static bool IsValidTypeDeclaration(SyntaxNode node, CancellationToken _) =>
        node is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax;
}
