# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                      # whole solution, 0 warnings is the expected state
dotnet test                       # xunit, drives the generator through a Roslyn driver
dotnet test --filter "Include_combined_with_Exclude_is_reported_and_nothing_is_emitted"
dotnet run --project samples/InterfaceGenerator.Sample
./tests/Packaging/run.ps1         # end-to-end over a real NuGet package (see below)
```

`TreatWarningsAsErrors` is on repo-wide, so a warning fails the build.

`git mv` and directory renames fail with `Permission denied` while MSBuild node processes hold
handles on `bin`/`obj`. Run `dotnet build-server shutdown` first.

Generated sources land in `obj/GeneratedFiles/` (`EmitCompilerGeneratedFiles`) — read them there
rather than reasoning about what the emitter should have produced.

## What this generates

`[GenerateInterface]` on a partial class or record emits, in one file: a second partial declaration
adding the interface to the type's base list, and the interface itself, carrying the public
instance members the type **declares**.

Three decisions are settled, not open:

**Declared members only.** Base-class members are never carried over. The interface describes what
the annotated type declares.

**`Include` and `Exclude` are mutually exclusive** and are `string[]` on the attribute rather than
per-member attributes. Matching is by name, so all overloads of a listed name move together; that
is the accepted cost of keeping one public attribute in the package. An unmatched name is a warning
(IG0005) precisely because the alternative is a silent change in the generated interface.

**Top-level types only** (IG0002). Supporting nested types means re-emitting every enclosing type as
a partial declaration, each of which must itself be partial — left out rather than half-supported.

## Service registration

A second output, emitted **only** when `Microsoft.Extensions.DependencyInjection.IServiceCollection`
resolves in the compilation: an extension method holding one `ServiceDescriptor` per annotated type.
`ServiceDescriptor` rather than `AddTransient`/`AddScoped`/`AddSingleton` because the lifetime is a
value here, not a method name.

- `InterfaceGenerator.Lifetime` is **not** `Microsoft.Extensions.DependencyInjection.ServiceLifetime`.
  Using the latter would put a dependency on every consumer of the package, and its `Singleton = 0`
  leaves no value meaning "not set". The name omits the `Service` prefix so that importing both
  namespaces is not ambiguous. The generator mirrors the enum privately — it cannot reference the
  attributes assembly, which is compiled into the assembly being analyzed — so **the two orderings
  must stay in sync**.
- The method is named after the assembly (`Add{Sanitized}Services`), overridable through
  `[assembly: GeneratedServiceRegistration]`. A fixed name would make the call ambiguous in an
  application referencing a library that also uses the generator.
- IG0007 fires only on an **explicit** lifetime. The generator is usable for interfaces alone, and
  an assembly that never mentioned DI must not be warned about a package it never asked for. The
  test harness has to strip the DI assemblies out of `TRUSTED_PLATFORM_ASSEMBLIES` for this to be
  testable at all — the test project references them for its own assertions.
- `ServiceRegistrationTests` emits the compilation, loads it, and **calls** the generated method.
  Asserting on the emitted text passes just as happily when a lifetime lands on the wrong
  registration.

## Incremental generator invariants

- **No Roslyn object may enter a pipeline value.** `ISymbol`, `Compilation`, `SyntaxNode` and
  `Location` all pin the compilation in memory and compare by reference or by syntax-tree identity,
  which re-runs the generator on every edit. `InterfaceSpec` is therefore all strings;
  `EquatableArray<T>`, `LocationInfo` and `DiagnosticInfo` exist for the same reason.
- The `ForAttributeWithMetadataName` transform reduces to plain data **inside** the lambda.
- `Collect()` returns a fresh `ImmutableArray` every run, which compares by reference. The
  `EquatableArray` wrapped around it is what keeps the registration file from being re-emitted on
  every keystroke.
- `IncrementalCachingTests` guards this. `A_reported_diagnostic_does_not_break_caching` uses
  `ReplaceSyntaxTree`, not `AddSyntaxTrees` — adding a file leaves the original tree instance
  intact and the test would pass even when broken.

`ToDisplayString` is used here, unlike in sibling generators that avoid it: it allocates per symbol
visited, but this work hangs off `ForAttributeWithMetadataName` (only annotated types in a changed
file), and re-implementing C# signature rendering by hand would be far worse.

## Emitter constraints

- The generated partial uses a **brace body**, not `: IFoo;`. A semicolon-bodied type declaration
  needs C# 12, and the generated file compiles under the *consumer's* `LangVersion`.
- Constraints are omitted on the partial type declaration and written only on the interface: a
  partial declaration may leave them out, and repeating them means keeping two renderings in sync.
- Two `namespace` blocks are always emitted, even when they are the same namespace. One code path
  covers the `Namespace = "..."` override; the compiler is indifferent to the repetition.
- Everything is fully qualified with `global::`, so the generated file never depends on the usings
  in scope where the annotated type is declared.
- Hint names are `{Namespace}.{Type}[_{arity}].g.cs`: two same-named types in one assembly must not
  collide.

## netstandard2.0 constraints (generator project only)

The generator must target netstandard2.0 to be loadable by Roslyn. Consequences:

- List patterns need `System.Index`, which is absent.
- `record struct` init setters need `Polyfills/IsExternalInit.cs`.
- `AnalyzerReleases.{Shipped,Unshipped}.md` are required by RS2008 whenever a `DiagnosticDescriptor`
  is added; the separator row format is strict and RS2007 rejects a malformed one.

## Testing

`GeneratorResult.CompilationErrors` compiles the original source **plus** everything the generator
added. The point of this generator is that the annotated type ends up implementing the interface,
and only compiling the result proves it — assert on it in any test that emits.

## Packaging

`src/InterfaceGenerator.Abstractions` is the packaging project (`PackageId` is
`lucaslgt.InterfaceGenerator`; the namespaces stay `InterfaceGenerator`): its own assembly goes to
`lib/`, and `PackGeneratorAsAnalyzer` packs the generator to `analyzers/dotnet/cs` from the
`@(Analyzer)` item that its `ProjectReference` populates.

A library that uses the generator needs **no** `PrivateAssets` for its consumers to get it too:
the analyzer flows transitively at the default setting. `PrivateAssets="all"` does break consumers
— it also hides the attributes (CS0012).

`tests/Packaging/run.ps1` is what proves this. It packs to a local feed under `.artifacts/`, packs
`Demo.Services` against it, and builds `Demo.App`, which references only `Demo.Services`. Demo.App
compiles only if the analyzer flowed through, and only if the interface generated *inside*
Demo.Services is usable from another assembly. It also calls both generated registration methods,
which is what proves the per-assembly naming keeps them from colliding. The script wipes the feed,
an isolated package cache and both demo `obj`/`bin` on each run, because a repacked package at an
unchanged version would otherwise be served from cache and the test would pass without testing
anything.

`tests/Packaging/Demo.App/` is force-tracked in `.gitignore`: the stock template's `*.app` rule
(macOS bundles) matches the directory name and silently excluded it.

## MSBuild layout

- `Directory.Build.props` — shared compiler settings plus `InterfaceGeneratorVersion` /
  `DemoServicesVersion`, the single source for package versions. `run.ps1` reads the former via
  `dotnet msbuild -getProperty`.
- `Directory.Build.targets` — `EmitCompilerGeneratedFiles` lives here, **not** in `.props`:
  `BaseIntermediateOutputPath` is unset that early, and generated files would land in the project
  root where the default globs compile them a second time.
- `Directory.Packages.props` — central package management. The Roslyn version is shared by the
  generator and the tests on purpose; bumping one alone makes the tests exercise a different API
  surface than the one shipped.

## Release

Pushing a `v*` tag runs `.github/workflows/release.yml`: the tag is the only source of the
published version (`v1.2.3` -> `1.2.3`), so nothing in the repo is bumped to release. The workflow
packs at that version through `run.ps1`, and pushes **that same validated artifact** to nuget.org —
never a separately packed one.

Release notes come from `CHANGELOG.md` (Keep a Changelog). Before tagging, rename `## [Unreleased]`
to `## [1.2.3] - YYYY-MM-DD` and update the compare links at the bottom. The workflow extracts that
section, fails before building if it is missing or empty, exposes it as the `ReleaseNotesFile`
environment variable (read by the `.csproj` into `PackageReleaseNotes`), and finally creates the
GitHub release with the same text — after the NuGet push, hence `contents: write`.

Publishing uses nuget.org trusted publishing (OIDC), not a stored API key. The job needs
`id-token: write`, and the `NuGet/login@v1` step must stay immediately before the push: it trades
one OIDC token for one API key valid for a single hour. The only repository secret is `NUGET_USER`
(the nuget.org profile name). The matching policy lives on nuget.org and names the repository owner,
the repository, and the workflow file name alone — `release.yml`, without its directory.
