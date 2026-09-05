using System;

namespace InterfaceGenerator.Sample;

/// <summary>
/// No lifetime of its own: it takes whatever the registration method is called with.
/// </summary>
[GenerateInterface]
public partial class Clock
{
    public DateTimeOffset Now() => DateTimeOffset.UtcNow;
}
