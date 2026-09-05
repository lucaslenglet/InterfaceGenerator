using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace InterfaceGenerator;

internal static class Diagnostics
{
    private const string Category = "InterfaceGenerator";

    /// <summary>
    /// The generated file carries a second declaration of the type, which adds the interface to
    /// its base list. Without <c>partial</c> there is nowhere to put it.
    /// </summary>
    public static readonly DiagnosticDescriptor NotPartial = new(
        id: "IG0001",
        title: "Annotated type must be partial",
        messageFormat: "'{0}' is annotated with [GenerateInterface] but is not partial, so the generated interface cannot be applied to it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Supporting these means re-emitting every enclosing type as a partial declaration, each of
    /// which must itself be partial. Left out rather than half-supported.
    /// </summary>
    public static readonly DiagnosticDescriptor NestedType = new(
        id: "IG0002",
        title: "Nested types are not supported",
        messageFormat: "'{0}' is nested inside another type; [GenerateInterface] only supports top-level types",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StaticType = new(
        id: "IG0003",
        title: "Static types cannot implement an interface",
        messageFormat: "'{0}' is static, so it cannot implement a generated interface",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// One names what is left out, the other names what is kept. Setting both leaves no reading
    /// of the intent that is not a guess.
    /// </summary>
    public static readonly DiagnosticDescriptor IncludeWithExclude = new(
        id: "IG0004",
        title: "Include and Exclude are mutually exclusive",
        messageFormat: "'{0}' sets both Include and Exclude on [GenerateInterface]; use one or the other",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Catches a typo or a member that stopped being eligible, both of which would otherwise
    /// change the generated interface silently.
    /// </summary>
    public static readonly DiagnosticDescriptor UnmatchedName = new(
        id: "IG0005",
        title: "Listed member name matches nothing",
        messageFormat: "'{1}' lists '{0}', which matches no eligible member of the type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// A container has no instance of an abstract type to hand out.
    /// </summary>
    public static readonly DiagnosticDescriptor AbstractRegistration = new(
        id: "IG0006",
        title: "An abstract type cannot be registered",
        messageFormat: "'{0}' is abstract, so the lifetime set on [GenerateInterface] is ignored and it is not registered",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reported only when a lifetime was set explicitly: an assembly using the generator for
    /// interfaces alone never asked for a container and must not be warned about one.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingServiceCollection = new(
        id: "IG0007",
        title: "Service registration needs Microsoft.Extensions.DependencyInjection.Abstractions",
        messageFormat: "A lifetime is set on [GenerateInterface], but this assembly does not reference Microsoft.Extensions.DependencyInjection.Abstractions, so no registration method is generated",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}

/// <summary>
/// A source position stored as plain data.
/// </summary>
/// <remarks>
/// A <see cref="Location"/> holds its <see cref="SyntaxTree"/>, which every edit replaces. Keeping
/// one in a pipeline value would both pin the compilation in memory and make the value compare
/// unequal on every keystroke, defeating the caching the pipeline is built around.
/// </remarks>
internal readonly record struct LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public static LocationInfo? From(Location? location) =>
        location?.SourceTree is null
            ? null
            : new LocationInfo(
                location.SourceTree.FilePath,
                location.SourceSpan,
                location.GetLineSpan().Span);

    public static LocationInfo? From(ISymbol symbol) =>
        From(symbol.Locations.Length > 0 ? symbol.Locations[0] : null);

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}

/// <summary>
/// A diagnostic held in an incremental pipeline. <see cref="Diagnostic"/> itself is unsuitable
/// there: it holds a symbol graph alive and does not compare structurally.
/// </summary>
internal readonly record struct DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    EquatableArray<string> Arguments)
{
    public DiagnosticInfo(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] arguments)
        : this(descriptor, location, new EquatableArray<string>(arguments))
    {
    }

    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(Descriptor, Location?.ToLocation(), Arguments.Cast<object?>().ToArray());
}
