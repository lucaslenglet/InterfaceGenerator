using System;

namespace InterfaceGenerator;

/// <summary>
/// Renames the generated service-registration extension method, or moves it to another namespace.
/// </summary>
/// <remarks>
/// Without it the method is named after the assembly — <c>AddAcmeBillingServices</c> for
/// <c>Acme.Billing</c> — so that a library and the application referencing it do not both offer an
/// identically named extension on <c>IServiceCollection</c>, which would make the call ambiguous.
/// </remarks>
/// <example>
/// <code>
/// [assembly: GeneratedServiceRegistration(MethodName = "AddApplicationServices")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class GeneratedServiceRegistrationAttribute : Attribute
{
    /// <summary>
    /// The name of the generated extension method. Defaults to <c>Add{AssemblyName}Services</c>.
    /// </summary>
    public string? MethodName { get; set; }

    /// <summary>
    /// The namespace to declare the extension class in. Defaults to
    /// <c>Microsoft.Extensions.DependencyInjection</c>, where it needs no import to be found.
    /// </summary>
    public string? Namespace { get; set; }
}
