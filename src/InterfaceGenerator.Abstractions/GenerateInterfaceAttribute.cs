using System;

namespace InterfaceGenerator;

/// <summary>
/// Generates an interface from the public instance members declared by the annotated type, and
/// makes the type implement it.
/// </summary>
/// <remarks>
/// The type must be <see langword="partial"/>: the generated file carries the second declaration
/// that adds the interface to its base list.
/// </remarks>
/// <example>
/// <code>
/// [GenerateInterface]
/// public partial class Greeter
/// {
///     public string Hello(string name) => $"Hi {name}";
/// }
///
/// // generated: public interface IGreeter { string Hello(string name); }
/// //            public partial class Greeter : IGreeter;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GenerateInterfaceAttribute : Attribute
{
    /// <summary>
    /// The interface name. Defaults to the type name prefixed with <c>I</c>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The namespace to declare the interface in. Defaults to the namespace of the type.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// The lifetime the generated interface is registered with by the generated service
    /// registration extension method.
    /// </summary>
    /// <remarks>
    /// <see cref="InterfaceGenerator.Lifetime.Default"/> defers to the default the extension
    /// method was called with, itself <c>Transient</c> unless the caller says otherwise;
    /// <see cref="InterfaceGenerator.Lifetime.None"/> leaves the type unregistered. Registration
    /// only happens at all in an assembly that references
    /// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>.
    /// </remarks>
    public Lifetime Lifetime { get; set; }

    /// <summary>
    /// The names of the members to leave out; every other eligible member is included.
    /// Mutually exclusive with <see cref="Include"/>.
    /// </summary>
    /// <remarks>
    /// Members are matched by name, so all overloads sharing a listed name are affected. Prefer
    /// <c>nameof</c> over a literal: a name matching nothing is reported as a warning, but
    /// <c>nameof</c> catches the typo at the point of the rename instead.
    /// </remarks>
    public string[]? Exclude { get; set; }

    /// <summary>
    /// The names of the members to include; every other member is left out. Mutually exclusive
    /// with <see cref="Exclude"/>.
    /// </summary>
    /// <remarks>
    /// Members are matched by name, so all overloads sharing a listed name are included.
    /// </remarks>
    public string[]? Include { get; set; }
}
