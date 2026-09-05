using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace InterfaceGenerator;

/// <summary>
/// Renders symbols as the source text of an interface member.
/// </summary>
/// <remarks>
/// <see cref="ISymbol.ToDisplayString(SymbolDisplayFormat)"/> allocates per symbol visited, which
/// is why generators that run off <c>CompilationProvider</c> avoid it. Here the work hangs off
/// <c>ForAttributeWithMetadataName</c>, so it runs only for the annotated types in a changed file,
/// and re-implementing C# signature rendering by hand would be far worse.
/// </remarks>
internal static class SymbolFormatter
{
    /// <summary>
    /// A whole member signature, minus its accessibility and its containing type: the interface
    /// supplies the first and does not want the second.
    /// </summary>
    private static readonly SymbolDisplayFormat SignatureFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
            | SymbolDisplayGenericsOptions.IncludeTypeConstraints,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeType
            | SymbolDisplayMemberOptions.IncludeRef,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeParamsRefOut
            | SymbolDisplayParameterOptions.IncludeDefaultValue,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// A type reference alone, fully qualified so that the generated file never depends on the
    /// usings in scope where the annotated type is declared.
    /// </summary>
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Renders one member declaration, or <see langword="null"/> for a member an interface cannot
    /// carry.
    /// </summary>
    public static string? Member(ISymbol member) => member switch
    {
        IMethodSymbol method => method.ToDisplayString(SignatureFormat) + ";",
        IPropertySymbol property => Property(property),
        IEventSymbol @event => $"event {@event.Type.ToDisplayString(TypeFormat)} {@event.Name};",
        _ => null,
    };

    /// <summary>
    /// Only the accessors that are themselves public are declared: a
    /// <c>{ get; private set; }</c> property satisfies a get-only interface member and nothing more.
    /// </summary>
    private static string Property(IPropertySymbol property)
    {
        StringBuilder builder = new StringBuilder()
            .Append(property.ToDisplayString(SignatureFormat))
            .Append(" {");

        if (property.GetMethod is { DeclaredAccessibility: Accessibility.Public })
        {
            builder.Append(" get;");
        }

        if (property.SetMethod is { DeclaredAccessibility: Accessibility.Public } setter)
        {
            builder.Append(setter.IsInitOnly ? " init;" : " set;");
        }

        return builder.Append(" }").ToString();
    }

    public static string TypeParameters(INamedTypeSymbol type)
    {
        if (type.TypeParameters.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder().Append('<');

        for (int i = 0; i < type.TypeParameters.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(type.TypeParameters[i].Name);
        }

        return builder.Append('>').ToString();
    }

    /// <summary>
    /// The <c>where</c> clauses of a generic type, one per constrained parameter.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than through a display format: a format renders the constraints as
    /// part of the whole type declaration, and splitting that string back apart is worse than
    /// walking the four constraint flags.
    /// </remarks>
    public static string[] Constraints(INamedTypeSymbol type)
    {
        List<string> clauses = [];

        foreach (ITypeParameterSymbol parameter in type.TypeParameters)
        {
            List<string> parts = [];

            if (parameter.HasReferenceTypeConstraint)
            {
                parts.Add(
                    parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                        ? "class?"
                        : "class");
            }
            else if (parameter.HasUnmanagedTypeConstraint)
            {
                parts.Add("unmanaged");
            }
            else if (parameter.HasValueTypeConstraint)
            {
                parts.Add("struct");
            }
            else if (parameter.HasNotNullConstraint)
            {
                parts.Add("notnull");
            }

            foreach (ITypeSymbol constraint in parameter.ConstraintTypes)
            {
                parts.Add(constraint.ToDisplayString(TypeFormat));
            }

            // A struct constraint already implies it, and the two cannot be written together.
            if (parameter.HasConstructorConstraint && !parameter.HasValueTypeConstraint)
            {
                parts.Add("new()");
            }

            if (parts.Count > 0)
            {
                clauses.Add($"where {parameter.Name} : {string.Join(", ", parts)}");
            }
        }

        return [.. clauses];
    }
}
