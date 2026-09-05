using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InterfaceGenerator.Tests;

public class ServiceRegistrationTests
{
    private const string TwoServices = """
        using InterfaceGenerator;

        namespace Sample;

        [GenerateInterface]
        public partial class Greeter
        {
            public string Hello() => "";
        }

        [GenerateInterface(Lifetime = Lifetime.Singleton)]
        public partial class Clock
        {
            public int Now() => 0;
        }
        """;

    [Fact]
    public void Every_annotated_type_is_registered_against_its_generated_interface()
    {
        ServiceDescriptor[] descriptors = GeneratorHarness
            .Run(TwoServices, withDependencyInjection: true)
            .Register();

        Assert.Equal(
            [
                ("Sample.IClock", "Sample.Clock", ServiceLifetime.Singleton),
                ("Sample.IGreeter", "Sample.Greeter", ServiceLifetime.Transient),
            ],
            descriptors.Select(d =>
                (d.ServiceType.FullName, d.ImplementationType!.FullName, d.Lifetime)));
    }

    /// <summary>
    /// The two ways of setting a lifetime meet here: the method-level default applies to whatever
    /// did not set one, and never overrides one that did.
    /// </summary>
    [Fact]
    public void The_method_default_applies_only_where_the_attribute_set_nothing()
    {
        ServiceDescriptor[] descriptors = GeneratorHarness
            .Run(TwoServices, withDependencyInjection: true)
            .Register(ServiceLifetime.Scoped);

        Assert.Equal(
            [
                ("Sample.IClock", ServiceLifetime.Singleton),
                ("Sample.IGreeter", ServiceLifetime.Scoped),
            ],
            descriptors.Select(d => (d.ServiceType.FullName, d.Lifetime)));
    }

    [Fact]
    public void The_registered_interface_resolves_to_the_annotated_type()
    {
        ServiceDescriptor[] descriptors = GeneratorHarness
            .Run(TwoServices, withDependencyInjection: true)
            .Register();

        IServiceCollection services = new ServiceCollection();

        foreach (ServiceDescriptor descriptor in descriptors)
        {
            services.Add(descriptor);
        }

        using ServiceProvider provider = services.BuildServiceProvider();

        object service = provider.GetRequiredService(descriptors[1].ServiceType);

        Assert.Equal("Sample.Greeter", service.GetType().FullName);
    }

    [Fact]
    public void Lifetime_None_leaves_the_type_unregistered()
    {
        ServiceDescriptor[] descriptors = GeneratorHarness
            .Run(
                """
                using InterfaceGenerator;

                [GenerateInterface(Lifetime = Lifetime.None)]
                public partial class Helper
                {
                    public string Hello() => "";
                }

                [GenerateInterface]
                public partial class Greeter
                {
                    public string Hello() => "";
                }
                """,
                withDependencyInjection: true)
            .Register();

        Assert.Equal(["IGreeter"], descriptors.Select(d => d.ServiceType.Name));
    }

    /// <summary>
    /// An open generic registration is what lets a container close <c>IRepository&lt;Order&gt;</c>
    /// over the implementation on demand.
    /// </summary>
    [Fact]
    public void A_generic_type_is_registered_open()
    {
        ServiceDescriptor[] descriptors = GeneratorHarness
            .Run(
                """
                using InterfaceGenerator;

                namespace Sample;

                [GenerateInterface(Lifetime = Lifetime.Scoped)]
                public partial class Repository<TEntity> where TEntity : class
                {
                    public TEntity? Find(int id) => null;
                }
                """,
                withDependencyInjection: true)
            .Register();

        ServiceDescriptor descriptor = Assert.Single(descriptors);

        Assert.True(descriptor.ServiceType.IsGenericTypeDefinition);
        Assert.True(descriptor.ImplementationType!.IsGenericTypeDefinition);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void An_abstract_type_is_not_registered_and_an_explicit_lifetime_is_reported()
    {
        GeneratorResult result = GeneratorHarness.Run(
            """
            using InterfaceGenerator;

            [GenerateInterface(Lifetime = Lifetime.Scoped)]
            public abstract partial class Service
            {
                public string Hello() => "";
            }
            """,
            withDependencyInjection: true);

        Assert.Equal(["IG0006"], result.DiagnosticIds);
        Assert.DoesNotContain("ServiceDescriptor", result.Text);
    }

    [Fact]
    public void An_abstract_type_without_a_lifetime_is_skipped_silently()
    {
        GeneratorResult result = GeneratorHarness.Run(
            """
            using InterfaceGenerator;

            [GenerateInterface]
            public abstract partial class Service
            {
                public string Hello() => "";
            }
            """,
            withDependencyInjection: true);

        Assert.Empty(result.DiagnosticIds);
        Assert.DoesNotContain("ServiceDescriptor", result.Text);
    }

    /// <summary>
    /// The generator is usable for interfaces alone, so an assembly that never mentioned a
    /// lifetime must not be told about a package it never asked for.
    /// </summary>
    [Fact]
    public void Without_the_dependency_injection_package_nothing_is_registered_or_reported()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface]
            public partial class Greeter
            {
                public string Hello() => "";
            }
            """);

        Assert.Empty(result.DiagnosticIds);
        Assert.DoesNotContain("IServiceCollection", result.Text);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void An_explicit_lifetime_without_the_dependency_injection_package_is_reported()
    {
        GeneratorResult result = GeneratorHarness.Run("""
            using InterfaceGenerator;

            [GenerateInterface(Lifetime = Lifetime.Singleton)]
            public partial class Greeter
            {
                public string Hello() => "";
            }
            """);

        Assert.Equal(["IG0007"], result.DiagnosticIds);
        Assert.Empty(result.CompilationErrors);
    }

    /// <summary>
    /// Named after the assembly, so that a library and the application referencing it do not both
    /// offer an identically named extension on <c>IServiceCollection</c>.
    /// </summary>
    [Fact]
    public void The_extension_method_is_named_after_the_assembly()
    {
        GeneratorResult result = GeneratorHarness.Run(
            """
            using InterfaceGenerator;

            [GenerateInterface]
            public partial class Greeter
            {
                public string Hello() => "";
            }
            """,
            withDependencyInjection: true);

        Assert.Contains("class TestsGeneratedServiceCollectionExtensions", result.Text);
        Assert.Contains("AddTestsGeneratedServices(", result.Text);
        Assert.Contains("namespace Microsoft.Extensions.DependencyInjection", result.Text);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void The_assembly_attribute_overrides_the_method_name_and_namespace()
    {
        GeneratorResult result = GeneratorHarness.Run(
            """
            using InterfaceGenerator;

            [assembly: GeneratedServiceRegistration(MethodName = "AddApplicationServices", Namespace = "Sample.Composition")]

            [GenerateInterface]
            public partial class Greeter
            {
                public string Hello() => "";
            }
            """,
            withDependencyInjection: true);

        Assert.Contains("namespace Sample.Composition", result.Text);
        Assert.Contains("AddApplicationServices(", result.Text);
        Assert.Empty(result.CompilationErrors);
    }
}
