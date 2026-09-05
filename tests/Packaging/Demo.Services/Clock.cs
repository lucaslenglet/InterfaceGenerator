using InterfaceGenerator;

namespace Demo.Services;

/// <summary>
/// Its interface is generated inside this library and published in its package, so the consumer
/// depends on a type no source in the consumer ever declared.
/// </summary>
[GenerateInterface(Lifetime = Lifetime.Singleton)]
public partial class Clock
{
    public int Ticks() => 2;
}
