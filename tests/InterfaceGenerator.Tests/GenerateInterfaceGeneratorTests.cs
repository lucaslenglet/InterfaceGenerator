using Xunit;

namespace InterfaceGenerator.Tests;

public class GenerateInterfaceGeneratorTests
{
    [Fact]
    public void Public_instance_members_become_interface_members()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            namespace Sample;

            [GenerateInterface]
            public partial class Greeter
            {
                public string Hello(string name) => $"Hi {name}";
                public int Count { get; set; }
                public string? Last { get; private set; }
                public event System.EventHandler? Greeted;
            }
            """);

        Assert.Equal(
            [
                "string Hello(string name);",
                "int Count { get; set; }",
                "string? Last { get; }",
                "event global::System.EventHandler? Greeted;",
            ],
            result.Members);

        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void The_annotated_type_implements_the_generated_interface()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            namespace Sample;

            [GenerateInterface]
            public partial class Greeter
            {
                public string Hello(string name) => $"Hi {name}";
            }

            public static class Consumer
            {
                // Does not compile unless the generated partial adds IGreeter to the base list.
                public static string Use(IGreeter greeter) => greeter.Hello("world");
            }
            """);

        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void Non_public_static_and_object_overrides_are_left_out()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface]
            public partial class Service
            {
                public string Kept() => "";
                internal string Internal() => "";
                protected string Protected() => "";
                public static string Static() => "";
                public int Field;
                public Service() { }
                public override string ToString() => "";
                public override bool Equals(object? obj) => false;
                public override int GetHashCode() => 0;
                public static Service operator +(Service a, Service b) => a;
            }
            """);

        Assert.Equal(["string Kept();"], result.Members);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void Exclude_drops_the_listed_members()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface(Exclude = [nameof(Service.Dump)])]
            public partial class Service
            {
                public string Kept() => "";
                public void Dump() { }
            }
            """);

        Assert.Equal(["string Kept();"], result.Members);
    }

    /// <summary>
    /// Matching is by name, so a listed name takes every overload sharing it with it.
    /// </summary>
    [Fact]
    public void Exclude_drops_every_overload_of_a_listed_name()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface(Exclude = ["Dump"])]
            public partial class Service
            {
                public string Kept() => "";
                public void Dump() { }
                public void Dump(int depth) { }
            }
            """);

        Assert.Equal(["string Kept();"], result.Members);
    }

    [Fact]
    public void Include_keeps_only_the_listed_members()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface(Include = ["Kept", "Value"])]
            public partial class Service
            {
                public string Kept() => "";
                public int Value { get; set; }
                public void Dropped() { }
            }
            """);

        Assert.Equal(["string Kept();", "int Value { get; set; }"], result.Members);
    }

    [Fact]
    public void An_empty_Include_keeps_nothing()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface(Include = [])]
            public partial class Service
            {
                public string Dropped() => "";
            }
            """);

        Assert.Empty(result.Members);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void Include_combined_with_Exclude_is_reported_and_nothing_is_emitted()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface(Include = ["Kept"], Exclude = ["Dropped"])]
            public partial class Service
            {
                public string Kept() => "";
                public void Dropped() { }
            }
            """);

        Assert.Equal(["IG0004"], result.DiagnosticIds);
        Assert.True(result.EmittedNothing);
    }

    [Fact]
    public void A_listed_name_matching_nothing_is_reported()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface(Exclude = ["Typo"])]
            public partial class Service
            {
                public string Kept() => "";
            }
            """);

        Assert.Equal(["IG0005"], result.DiagnosticIds);
        Assert.Equal(["string Kept();"], result.Members);
    }

    [Fact]
    public void A_non_partial_type_is_reported_and_nothing_is_emitted()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface]
            public class Service
            {
                public string Kept() => "";
            }
            """);

        Assert.Equal(["IG0001"], result.DiagnosticIds);
        Assert.True(result.EmittedNothing);
    }

    [Fact]
    public void A_nested_type_is_reported_and_nothing_is_emitted()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            public partial class Outer
            {
                [GenerateInterface]
                public partial class Inner
                {
                    public string Kept() => "";
                }
            }
            """);

        Assert.Equal(["IG0002"], result.DiagnosticIds);
        Assert.True(result.EmittedNothing);
    }

    [Fact]
    public void A_static_type_is_reported_and_nothing_is_emitted()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface]
            public static partial class Service
            {
                public static string Kept() => "";
            }
            """);

        Assert.Equal(["IG0003"], result.DiagnosticIds);
        Assert.True(result.EmittedNothing);
    }

    [Fact]
    public void Name_and_Namespace_override_where_the_interface_lands()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            namespace Sample;

            [GenerateInterface(Name = "IGreetingService", Namespace = "Sample.Abstractions")]
            public partial class Greeter
            {
                public string Hello(string name) => $"Hi {name}";
            }

            public static class Consumer
            {
                public static string Use(Sample.Abstractions.IGreetingService greeter) => greeter.Hello("world");
            }
            """);

        Assert.Equal(["IGreetingService"], result.InterfaceNames);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void Type_parameters_and_their_constraints_are_carried_over()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;
            using System.Collections.Generic;

            namespace Sample;

            [GenerateInterface]
            public partial class Repository<TEntity, TKey>
                where TEntity : class, new()
                where TKey : notnull
            {
                public TEntity? Find(TKey key) => null;
                public IReadOnlyList<TEntity> All() => [];
            }

            public static class Consumer
            {
                public static string Use(IRepository<object, int> repository) => repository.Find(1)?.ToString() ?? "";
            }
            """);

        Assert.Equal(
            [
                "TEntity? Find(TKey key);",
                "global::System.Collections.Generic.IReadOnlyList<TEntity> All();",
            ],
            result.Members);

        Assert.Contains("where TEntity : class, new()", result.Text);
        Assert.Contains("where TKey : notnull", result.Text);
        Assert.Empty(result.CompilationErrors);
    }

    /// <summary>
    /// Every parameter form has its own rendering rules, and getting one wrong produces a file
    /// that does not compile rather than a wrong interface.
    /// </summary>
    [Fact]
    public void Parameter_modifiers_defaults_and_generic_methods_are_rendered()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface]
            public partial class Service
            {
                public bool TryGet(string key, out int value) { value = 0; return false; }
                public void Add(in int value) { }
                public void Sum(params int[] values) { }
                public string Format(string value, int width = 8, string? prefix = null) => value;
                public T Map<T>(object source) where T : class, new() => new();
                public ref readonly int Ref() => ref _slot;
                private readonly int _slot;
            }
            """);

        Assert.Equal(
            [
                "bool TryGet(string key, out int value);",
                "void Add(in int value);",
                "void Sum(params int[] values);",
                "string Format(string value, int width = 8, string? prefix = null);",
                "T Map<T>(object source) where T : class, new();",
                "ref readonly int Ref();",
            ],
            result.Members);

        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void Indexers_and_init_accessors_are_rendered()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface]
            public partial class Bag
            {
                public string this[int index] { get => ""; set { } }
                public int Size { get; init; }
            }
            """);

        Assert.Equal(
            [
                "string this[int index] { get; set; }",
                "int Size { get; init; }",
            ],
            result.Members);

        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void An_indexer_is_excluded_by_its_metadata_name()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface(Exclude = ["Item"])]
            public partial class Bag
            {
                public string this[int index] { get => ""; set { } }
                public int Size { get; init; }
            }
            """);

        Assert.Equal(["int Size { get; init; }"], result.Members);
        Assert.Empty(result.DiagnosticIds);
    }

    [Fact]
    public void A_record_keeps_its_keyword_and_drops_its_synthesized_members()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface]
            public partial record Person(string First, string Last)
            {
                public string Full() => $"{First} {Last}";
            }
            """);

        Assert.Equal(
            [
                "string First { get; init; }",
                "string Last { get; init; }",
                "string Full();",
            ],
            result.Members);

        Assert.Contains("partial record Person", result.Text);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void An_internal_type_gets_an_internal_interface()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface]
            internal partial class Service
            {
                public string Kept() => "";
            }
            """);

        Assert.Contains("internal interface IService", result.Text);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void A_type_in_the_global_namespace_is_emitted_without_a_namespace_block()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface]
            public partial class Service
            {
                public string Kept() => "";
            }
            """);

        Assert.DoesNotContain("namespace", result.Text);
        Assert.Empty(result.CompilationErrors);
    }

    /// <summary>
    /// Inherited members are deliberately left out: the interface describes what the annotated
    /// type declares, not what its bases happen to expose.
    /// </summary>
    [Fact]
    public void Inherited_members_are_not_carried_over()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            public class BaseService
            {
                public string Inherited() => "";
            }

            [GenerateInterface]
            public partial class Service : BaseService
            {
                public string Declared() => "";
            }
            """);

        Assert.Equal(["string Declared();"], result.Members);
    }

    [Fact]
    public void Two_same_named_types_in_different_namespaces_do_not_collide()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            namespace First
            {
                [GenerateInterface]
                public partial class Service
                {
                    public string Kept() => "";
                }
            }

            namespace Second
            {
                [GenerateInterface]
                public partial class Service
                {
                    public string Kept() => "";
                }
            }
            """);

        Assert.Equal(2, result.Generated.Length);
        Assert.Empty(result.CompilationErrors);
    }
}
