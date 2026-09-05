using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InterfaceGenerator.Tests;

/// <summary>
/// Runs the generator over a snippet of source and exposes what it produced.
/// </summary>
internal static class GeneratorHarness
{
    private static readonly string[] DependencyInjectionAssemblies =
    [
        "Microsoft.Extensions.DependencyInjection.dll",
        "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
    ];

    /// <summary>
    /// The framework the test host runs on. Taken from the host rather than from whatever happens
    /// to be loaded, so the reference set does not depend on test execution order.
    /// </summary>
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            // The test project references these so that the assertions can talk about
            // ServiceDescriptor, which puts them on the host's assembly list. Leaving them in
            // would reference them from every test compilation, and the registration method —
            // emitted exactly when IServiceCollection resolves — would appear in all of them.
            .Where(path => !DependencyInjectionAssemblies.Contains(Path.GetFileName(path)))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),

        MetadataReference.CreateFromFile(typeof(GenerateInterfaceAttribute).Assembly.Location),
    ];

    /// <summary>
    /// Added back only on request, so that a test can compile with or without a container in
    /// reach and see what the generator does in each case.
    /// </summary>
    private static readonly ImmutableArray<MetadataReference> DependencyInjection =
    [
        MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ServiceCollection).Assembly.Location),
    ];

    public static CSharpCompilation Compile(string source, bool withDependencyInjection = false) =>
        CSharpCompilation.Create(
            assemblyName: "Tests.Generated",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: withDependencyInjection ? References.AddRange(DependencyInjection) : References,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

    /// <summary>
    /// A driver that records why each step ran, for incremental caching assertions.
    /// </summary>
    public static GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(
        [new GenerateInterfaceGenerator().AsSourceGenerator()],
        driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

    public static GeneratorResult Run(string source, bool withDependencyInjection = false)
    {
        CSharpCompilation compilation = Compile(source, withDependencyInjection);

        GeneratorDriver driver = CreateDriver()
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);

        return new GeneratorResult(
            driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).ToImmutableArray(),
            driver.GetRunResult().Diagnostics,
            updated);
    }
}

internal sealed record GeneratorResult(
    ImmutableArray<GeneratedSourceResult> Generated,
    ImmutableArray<Diagnostic> Diagnostics,
    Compilation Updated)
{
    public bool EmittedNothing => Generated.Length == 0;

    public string[] DiagnosticIds => [.. Diagnostics.Select(d => d.Id)];

    public string Text => string.Join(Environment.NewLine, Generated.Select(g => g.SourceText.ToString()));

    /// <summary>
    /// The declared members of the generated interface, read from the syntax tree the driver
    /// already produced rather than from raw text, so assertions do not depend on formatting.
    /// </summary>
    public string[] Members =>
    [
        .. Generated
            .SelectMany(g => g.SyntaxTree.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            .SelectMany(i => i.Members)
            .Select(m => m.ToString()),
    ];

    public string[] InterfaceNames =>
    [
        .. Generated
            .SelectMany(g => g.SyntaxTree.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            .Select(i => i.Identifier.Text),
    ];

    /// <summary>
    /// The compiler errors of the original source plus everything the generator added. The point
    /// of the generator is that the annotated type ends up implementing the interface, and only
    /// compiling the result proves it.
    /// </summary>
    public string[] CompilationErrors =>
    [
        .. Updated.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}"),
    ];

    /// <summary>
    /// Emits the generated assembly, loads it, and calls the generated registration method, so
    /// that the assertions read the descriptors a container would actually get.
    /// </summary>
    /// <remarks>
    /// The alternative is asserting on the emitted text, which passes just as happily when the
    /// lifetime lands on the wrong registration.
    /// </remarks>
    public ServiceDescriptor[] Register(ServiceLifetime? defaultLifetime = null)
    {
        using MemoryStream stream = new();
        EmitResult emitted = Updated.Emit(stream);

        Assert.True(
            emitted.Success,
            string.Join(Environment.NewLine, emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        MethodInfo method = Assembly.Load(stream.ToArray())
            .GetTypes()
            .Single(type => type.Name.EndsWith("ServiceCollectionExtensions", StringComparison.Ordinal))
            .GetMethods()
            .Single(candidate => candidate.Name.StartsWith("Add", StringComparison.Ordinal));

        IServiceCollection services = new ServiceCollection();
        method.Invoke(null, [services, defaultLifetime ?? (object)Type.Missing]);

        return [.. services];
    }
}
