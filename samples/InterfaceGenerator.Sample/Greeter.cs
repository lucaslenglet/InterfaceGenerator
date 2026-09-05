using System.Collections.Generic;

namespace InterfaceGenerator.Sample;

/// <summary>
/// The whole surface of the generator: an attribute, and a partial keyword.
/// </summary>
[GenerateInterface(Lifetime = Lifetime.Singleton, Exclude = [nameof(DumpState)])]
public partial class Greeter
{
    private readonly List<string> _greeted = [];

    public string Culture { get; init; } = "en";

    public string Hello(string name)
    {
        _greeted.Add(name);
        return Culture == "fr" ? $"Bonjour {name}" : $"Hello {name}";
    }

    public IReadOnlyList<string> Greeted() => _greeted;

    /// <summary>
    /// Left out of the interface: it exists for debugging, not for callers.
    /// </summary>
    public void DumpState() => System.Console.WriteLine(string.Join(", ", _greeted));
}
