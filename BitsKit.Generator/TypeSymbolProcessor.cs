using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using System.Text;
using BitsKit.Generator.Models;
using System.Linq;

namespace BitsKit.Generator;

internal sealed record TypeSymbolProcessor
{
    public EquatableReadOnlyList<BitFieldModel> Fields { get; }
    public string? Namespace { get; }
    public string FullName { get; set; }
    
    public BitOrder DefaultBitOrder { get; }
    public bool IsStruct { get; }
    public bool IsInlineArray { get; }
    
    private readonly string _syntaxKeyword;
    private readonly string _syntaxIdentifier;

    public TypeSymbolProcessor(INamedTypeSymbol typeSymbol, AttributeData attribute)
    {
        _syntaxKeyword = typeSymbol.TypeKind switch
        {
            TypeKind.Struct when typeSymbol.IsRecord => "record struct",
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            TypeKind.Class when typeSymbol.IsRecord => "record",
            _ => "class"
        };
        _syntaxIdentifier = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        FullName = typeSymbol.ToString();
        
        Namespace = typeSymbol.ContainingNamespace.ToDisplayString(new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));
        if (string.IsNullOrWhiteSpace(Namespace)) Namespace = null;
        
        DefaultBitOrder = (BitOrder)attribute.ConstructorArguments[0].Value!;
        IsStruct = typeSymbol.TypeKind == TypeKind.Struct;
        IsInlineArray = HasInlineArrayAttribute(typeSymbol);
        
        Fields = EnumerateFields(typeSymbol);
    }

    public void GenerateCSharpSource(StringBuilder sb)
    {
        sb.AppendIndentedLine(1,
            StringConstants.TypeDeclarationTemplate,
            _syntaxKeyword,
            _syntaxIdentifier)
          .AppendIndentedLine(1, "{");

        foreach (BitFieldModel field in Fields)
            field.GenerateCSharpSource(sb);

        sb.RemoveLastLine()
          .AppendIndentedLine(1, "}")
          .AppendLine();
    }

    private EquatableReadOnlyList<BitFieldModel> EnumerateFields(ITypeSymbol typeSymbol)
    {
        var output = new List<BitFieldModel>();

        foreach (IFieldSymbol field in typeSymbol.GetMembers().OfType<IFieldSymbol>())
        {
            if (!IsValidFieldSymbol(field))
                continue;

            BackingFieldType backingType = field.Type.ToDisplayString() switch
            {
                "System.Memory<byte>" => BackingFieldType.Memory,
                "System.ReadOnlyMemory<byte>" => BackingFieldType.Memory,
                "System.Span<byte>" => BackingFieldType.Span,
                "System.ReadOnlySpan<byte>" => BackingFieldType.Span,
                "byte[]" => BackingFieldType.Span,
                "byte*" => BackingFieldType.Pointer,

                _ when field.Type.IsSupportedIntegralType() => IsInlineArray ? 
                    BackingFieldType.InlineArray : 
                    BackingFieldType.Integral,

                _ => BackingFieldType.Invalid
            };

            if (backingType == BackingFieldType.Invalid)
                continue;

            var backingModel = new BackingFieldModel(field, backingType);
            CreateBitFieldModels(output, field, backingModel);
        }

        return output.ToEquatableReadOnlyList();
    }

    private void CreateBitFieldModels(List<BitFieldModel> output, IFieldSymbol backingField, BackingFieldModel backingModel)
    {
        int offset = 0;

        foreach (AttributeData attribute in backingField.GetAttributes())
        {
            BitFieldModel? bitField = CreateBitFieldFromAttribute(attribute, this);

            if (bitField == null)
                continue;

            bitField.BackingField = backingModel;
            bitField.BitOffset = offset;

            // padding fields are not generated
            if (bitField is not { FieldType: BitFieldType.Padding })
            {
                // invert the bit order if necessary
                if (bitField.ReverseBitOrder)
                    bitField.BitOrder ^= BitOrder.MostSignificant;

                // integrals inherit their field type from their backing field
                if (backingModel.Type == BackingFieldType.Integral)
                    bitField.FieldType = backingField.Type.SpecialType.ToBitFieldType();

                // allow inline arrays to infer their type
                if (backingModel.Type == BackingFieldType.InlineArray)
                    bitField.FieldType ??= backingField.Type.SpecialType.ToBitFieldType();

                // add to list of fields to generate
                output.Add(bitField);
            }

            offset += bitField.BitCount;
        }
    }
    
    public static BitFieldModel? CreateBitFieldFromAttribute(AttributeData attribute, TypeSymbolProcessor processor)
    {
        string? attributeType = attribute.AttributeClass?.ToDisplayString();
            
        return attributeType switch
        {
            StringConstants.BitFieldAttributeFullName => new IntegralFieldModel(attribute, processor),
            StringConstants.BooleanFieldAttributeFullName => new BooleanFieldModel(attribute, processor),
            StringConstants.EnumFieldAttributeFullName => new EnumFieldModel(attribute, processor),
            _ => null
        };
    }

    private static bool HasInlineArrayAttribute(ITypeSymbol typeSymbol)
    {
        return (int?)typeSymbol
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == StringConstants.InlineArrayAttributeFullName)?
            .ConstructorArguments[0].Value > 0;
    }

    private static bool IsValidFieldSymbol(IFieldSymbol member) => member is
    {
        CanBeReferencedByName: true,
        IsConst: false,
        IsStatic: false,
        IsImplicitlyDeclared: false,
        Type: { }
    };
}
