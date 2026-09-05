using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace InterfaceGenerator.Tests;

/// <summary>
/// The transform reduces symbols to plain strings before anything reaches the pipeline. Without
/// that, every keystroke anywhere in the assembly would re-run the generator and re-emit the file.
/// </summary>
public class IncrementalCachingTests
{
    private const string Service = """
        using InterfaceGenerator;

        [GenerateInterface]
        public partial class Service
        {
            public string Kept() => "";
        }
        """;

    private const string ServiceWithDiagnostic = """
        using InterfaceGenerator;

        [GenerateInterface(Exclude = ["Typo"])]
        public partial class Service
        {
            public string Kept() => "";
        }
        """;

    [Fact]
    public void An_edit_elsewhere_in_the_assembly_keeps_the_output_cached()
    {
        CSharpCompilation compilation = GeneratorHarness.Compile(Service);
        GeneratorDriver driver = GeneratorHarness.CreateDriver().RunGenerators(compilation);

        driver = driver.RunGenerators(compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("public sealed class Unrelated { public int Value = 1; }")));

        AssertAllUnchanged(driver);
    }

    /// <summary>
    /// Diagnostics travel in the same pipeline value as the members. Holding a Roslyn
    /// <see cref="Location"/> there would compare unequal on every edit, so a single unmatched
    /// name would silently disable caching for the whole generator.
    /// </summary>
    [Fact]
    public void A_reported_diagnostic_does_not_break_caching()
    {
        CSharpCompilation compilation = GeneratorHarness.Compile(ServiceWithDiagnostic);
        GeneratorDriver driver = GeneratorHarness.CreateDriver().RunGenerators(compilation);

        // An edit inside the very file the diagnostic points at, changing nothing it reports on.
        // This is what a keystroke does: it replaces the syntax tree instance. ReplaceSyntaxTree,
        // not AddSyntaxTrees: adding a file leaves the original tree instance intact, and the
        // test would pass even when broken.
        SyntaxTree original = compilation.SyntaxTrees.Single();
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(
            original,
            CSharpSyntaxTree.ParseText(ServiceWithDiagnostic + Environment.NewLine + "// edited")));

        AssertAllUnchanged(driver);
    }

    /// <summary>
    /// The collected registrations are their own step: <c>Collect</c> hands back a fresh
    /// <c>ImmutableArray</c> every run, which compares by reference, so the registration file
    /// would be re-emitted on every keystroke without the <c>EquatableArray</c> around it.
    /// </summary>
    [Fact]
    public void The_collected_registrations_stay_cached_across_an_unrelated_edit()
    {
        CSharpCompilation compilation = GeneratorHarness.Compile(Service, withDependencyInjection: true);
        GeneratorDriver driver = GeneratorHarness.CreateDriver().RunGenerators(compilation);

        driver = driver.RunGenerators(compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("public sealed class Unrelated { public int Value = 1; }")));

        AssertAllUnchanged(driver, GenerateInterfaceGenerator.RegistrationsStepName);
    }

    private static void AssertAllUnchanged(
        GeneratorDriver driver,
        string stepName = GenerateInterfaceGenerator.TransformStepName)
    {
        IncrementalGeneratorRunStep[] steps =
        [
            .. driver.GetRunResult().Results.SelectMany(result => result.TrackedSteps[stepName]),
        ];

        Assert.NotEmpty(steps);

        // Cached counts too, and is the stronger of the two: the step did not run at all, where
        // Unchanged means it ran and produced a value equal to the previous one.
        Assert.All(
            steps.SelectMany(step => step.Outputs),
            output => Assert.Contains(
                output.Reason,
                (IncrementalStepRunReason[])[IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Cached]));
    }
}
