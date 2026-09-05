# InterfaceGenerator

Generate an interface from a class, and have the class implement it, with a single attribute.

```csharp
using InterfaceGenerator;

[GenerateInterface]
public partial class Greeter
{
    public string Hello(string name) => $"Hi {name}";
    public int Count { get; set; }
}
```

generates

```csharp
public partial class Greeter : global::IGreeter
{
}

public interface IGreeter
{
    string Hello(string name);
    int Count { get; set; }
}
```

so `IGreeter` can be registered in a container, mocked in a test, or handed to a caller, with
nothing to keep in sync by hand.

## Install

```bash
dotnet add package lucaslgt.InterfaceGenerator
```

One package: the attributes are a normal reference, the generator ships inside it as an analyzer.

## What ends up on the interface

Every member the annotated type **declares** itself that an interface can carry:

- public, non-static methods, properties, indexers and events;
- with their type parameters, constraints, `ref`/`out`/`in`/`params` modifiers, default values and
  nullable annotations preserved.

Left out: fields, constructors, operators, anything not `public`, anything `static`, `override`s of
`object` members, compiler-synthesized members (a record's `Equals`, `Deconstruct`, …), and
**members inherited from a base class** — the interface describes what the type declares, not what
its bases happen to expose.

Only the accessors that are themselves public are declared, so
`public string Name { get; private set; }` becomes `string Name { get; }`.

## Options

| Option | Effect |
| --- | --- |
| `Name` | Interface name. Defaults to the type name prefixed with `I`. |
| `Namespace` | Namespace to declare the interface in. Defaults to the type's namespace. |
| `Exclude` | Names to leave out; everything else is kept. |
| `Include` | Names to keep; everything else is left out. |

```csharp
[GenerateInterface(Name = "IGreetingService", Exclude = [nameof(DumpState)])]
public partial class Greeter { /* ... */ }
```

`Include` and `Exclude` are mutually exclusive — setting both is an error (IG0004).

Names are matched **by name**, so a listed name takes every overload sharing it with it. Prefer
`nameof` over a literal: a name matching nothing is reported (IG0005), but `nameof` catches the
typo at the point of the rename instead. An indexer has no `nameof`, so it is listed by its
metadata name, `"Item"` unless `[IndexerName]` says otherwise.

`Include = []` is an explicit empty list, not an unset option: it generates an empty interface.

## Dependency injection

An assembly that references `Microsoft.Extensions.DependencyInjection.Abstractions` also gets an
`IServiceCollection` extension method registering every annotated type against the interface
generated for it:

```csharp
new ServiceCollection()
    .AddAcmeBillingServices()                          // Transient by default
    .AddAcmeBillingServices(ServiceLifetime.Scoped);   // or another default for this assembly
```

The method is named after the assembly (`Acme.Billing` -> `AddAcmeBillingServices`) so that a
library and the application referencing it never offer two identically named extensions. Rename it,
or move it out of `Microsoft.Extensions.DependencyInjection`, with:

```csharp
[assembly: GeneratedServiceRegistration(MethodName = "AddApplicationServices", Namespace = "Acme.Composition")]
```

The lifetime resolves in this order:

1. `Lifetime` on the attribute, if set — `[GenerateInterface(Lifetime = Lifetime.Singleton)]`;
2. otherwise the argument the extension method was called with;
3. otherwise `Transient`.

`Lifetime.None` leaves the type out of the registration entirely. Abstract types are never
registered; setting a lifetime on one is reported (IG0006).

Generic types are registered open — `typeof(IRepository<>)` to `typeof(Repository<>)` — so the
container closes them on demand.

Nothing is generated, and nothing is reported, in an assembly that does not reference the
DI abstractions: the generator stays usable for interfaces alone. Setting a `Lifetime` there is the
one case that warns (IG0007), since it is the only sign a container was expected.

## Requirements

The annotated type must be `partial` and top-level. Records are supported; static types are not.

## Diagnostics

| ID | Severity | Meaning |
| --- | --- | --- |
| IG0001 | Error | The annotated type is not `partial`. |
| IG0002 | Error | The annotated type is nested inside another type. |
| IG0003 | Error | The annotated type is `static`. |
| IG0004 | Error | `Include` and `Exclude` are both set. |
| IG0005 | Warning | A listed name matches no eligible member. |
| IG0006 | Warning | A lifetime is set on an abstract type, which cannot be registered. |
| IG0007 | Warning | A lifetime is set, but the DI abstractions are not referenced. |
