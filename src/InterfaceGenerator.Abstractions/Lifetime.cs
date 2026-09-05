namespace InterfaceGenerator;

/// <summary>
/// The service lifetime a generated interface is registered with.
/// </summary>
/// <remarks>
/// Deliberately not <c>Microsoft.Extensions.DependencyInjection.ServiceLifetime</c>: using it here
/// would put a dependency on every consumer of this package, including those that never touch a
/// container, and its <c>Singleton = 0</c> leaves no value to mean "not set". The name avoids
/// <c>ServiceLifetime</c> so that having both namespaces imported is not ambiguous.
/// </remarks>
public enum Lifetime
{
    /// <summary>
    /// Registered with the default the generated extension method was called with.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Not registered at all.
    /// </summary>
    None,

    Transient,

    Scoped,

    Singleton,
}
