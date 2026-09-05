using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace InterfaceGenerator;

/// <summary>
/// Emits an interface for every type annotated with <c>[GenerateInterface]</c>, together with a
/// partial declaration that makes the type implement it.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GenerateInterfaceGenerator : IIncrementalGenerator
{
    private const string RootNamespace = "InterfaceGenerator";
    private const string AttributeName = RootNamespace + ".GenerateInterfaceAttribute";
    private const string RegistrationAttributeName = "GeneratedServiceRegistrationAttribute";
    private const string LifetimeName = "Lifetime";

    private const string ServiceCollectionName =
        "Microsoft.Extensions.DependencyInjection.IServiceCollection";

    /// <summary>
    /// Names the transform step so tests can assert it stays cached across unrelated edits.
    /// </summary>
    public const string TransformStepName = "Transform";

    /// <summary>
    /// Names the collection step, for the same reason as <see cref="TransformStepName"/>.
    /// </summary>
    public const string RegistrationsStepName = "Registrations";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ForAttributeWithMetadataName, not CompilationProvider: only files that actually contain
        // the attribute are re-examined. The transform still reduces to plain data inside the
        // lambda, so no symbol reaches the pipeline and unrelated edits stay cached.
        IncrementalValuesProvider<Result> results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeName,
                static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                Transform)
            .WithTrackingName(TransformStepName);

        context.RegisterSourceOutput(
            results.Where(static result => result.Spec is not null)
                .Select(static (result, _) => result.Spec!.Value),
            Emit);

        context.RegisterSourceOutput(
            results.SelectMany(static (result, _) => result.Diagnostics),
            static (production, diagnostic) => production.ReportDiagnostic(diagnostic.ToDiagnostic()));

        // Collect returns a fresh ImmutableArray every run, which compares by reference; the
        // EquatableArray around it is what keeps the registration file from being re-emitted on
        // every keystroke.
        IncrementalValueProvider<EquatableArray<RegistrationRequest>> requests = results
            .Select(static (result, _) => result.Request)
            .Collect()
            .Select(static (collected, _) => new EquatableArray<RegistrationRequest>([.. collected]))
            .WithTrackingName(RegistrationsStepName);

        // Hangs off CompilationProvider, which is invalidated on every keystroke, so it reduces to
        // three plain values right away and downstream stays cached.
        IncrementalValueProvider<RegistrationHost> host = context.CompilationProvider
            .Select(static (compilation, _) => ReadHost(compilation));

        context.RegisterSourceOutput(requests.Combine(host), EmitRegistrations);
    }

    /// <summary>
    /// Emits the registration extension method, if there is anything to register and anything to
    /// register it against.
    /// </summary>
    private static void EmitRegistrations(
        SourceProductionContext context,
        (EquatableArray<RegistrationRequest> Requests, RegistrationHost Host) input)
    {
        List<Registration> registrations = [];
        bool wanted = false;

        foreach (RegistrationRequest request in input.Requests)
        {
            wanted |= request.Explicit;

            if (request.Registration is { } registration)
            {
                registrations.Add(registration);
            }
        }

        if (!input.Host.HasServiceCollection)
        {
            // Only an explicit lifetime is evidence that DI was wanted here. An assembly using the
            // generator for interfaces alone must not be warned about a package it never asked for.
            if (wanted)
            {
                context.ReportDiagnostic(
                    new DiagnosticInfo(Diagnostics.MissingServiceCollection, null).ToDiagnostic());
            }

            return;
        }

        if (registrations.Count == 0)
        {
            return;
        }

        // The collected order follows the syntax provider, which is not guaranteed stable across
        // runs; the generated file has to be.
        registrations.Sort(static (left, right) =>
            string.CompareOrdinal(left.ServiceType, right.ServiceType));

        context.AddSource(
            ServiceRegistrations.HintName,
            SourceText.From(
                ServiceRegistrations.Render(
                    new EquatableArray<Registration>([.. registrations]), input.Host),
                Encoding.UTF8));
    }

    /// <summary>
    /// The extension method is named after the assembly unless the assembly says otherwise, so
    /// that a library and the application referencing it do not both offer an identically named
    /// extension on <c>IServiceCollection</c>.
    /// </summary>
    private static RegistrationHost ReadHost(Compilation compilation)
    {
        string sanitized = Sanitize(compilation.AssemblyName);
        string? methodName = null;
        string? @namespace = null;

        foreach (AttributeData attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != RegistrationAttributeName
                || Namespace(attribute.AttributeClass) != RootNamespace)
            {
                continue;
            }

            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (argument.Value.Value is not string text || text.Trim() is not { Length: > 0 } value)
                {
                    continue;
                }

                if (argument.Key == "MethodName")
                {
                    methodName = value;
                }
                else if (argument.Key == "Namespace")
                {
                    @namespace = value;
                }
            }
        }

        return new RegistrationHost(
            HasServiceCollection: compilation.GetTypeByMetadataName(ServiceCollectionName) is not null,
            // Declared where IServiceCollection lives, so the method needs no extra import.
            Namespace: @namespace ?? "Microsoft.Extensions.DependencyInjection",
            ClassName: sanitized + "ServiceCollectionExtensions",
            MethodName: methodName ?? "Add" + sanitized + "Services");
    }

    /// <summary>
    /// An assembly name is not an identifier: it carries dots, and may carry worse.
    /// </summary>
    private static string Sanitize(string? assemblyName)
    {
        StringBuilder builder = new();

        foreach (char character in assemblyName ?? string.Empty)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                builder.Append(builder.Length == 0 && char.IsDigit(character) ? '_' : character);
            }
        }

        return builder.Length == 0 ? "Generated" : builder.ToString();
    }

    private static Result Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type)
        {
            return default;
        }

        LocationInfo? typeLocation = LocationInfo.From(type);
        LocationInfo? attributeLocation = LocationInfo.From(
            context.Attributes[0].ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation())
            ?? typeLocation;

        Options options = ReadOptions(context.Attributes[0]);

        if (Reject(type, typeLocation, attributeLocation, options) is { } rejection)
        {
            return new Result(null, new EquatableArray<DiagnosticInfo>([rejection]), default);
        }

        List<DiagnosticInfo> diagnostics = [];
        List<string> members = [];

        // The listed names drive both directions: with Include set, a match is kept and
        // everything else dropped; otherwise a match is dropped and everything else kept.
        string[] listed = options.Include ?? options.Exclude ?? [];
        HashSet<string> selectors = new(listed, StringComparer.Ordinal);
        HashSet<string> matched = new(StringComparer.Ordinal);

        foreach (ISymbol member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsEligible(member))
            {
                continue;
            }

            string selector = SelectorName(member);
            bool selected = selectors.Contains(selector);

            if (selected)
            {
                matched.Add(selector);
            }

            if (selected != (options.Include is not null))
            {
                continue;
            }

            if (SymbolFormatter.Member(member) is { } text)
            {
                members.Add(text);
            }
        }

        // A listed name matching nothing is a typo, or a member that stopped being eligible;
        // either way it changes the generated interface silently.
        foreach (string name in listed)
        {
            if (!matched.Contains(name))
            {
                diagnostics.Add(new DiagnosticInfo(
                    Diagnostics.UnmatchedName, attributeLocation, name, type.Name));
            }
        }

        string typeNamespace = Namespace(type);
        string interfaceNamespace = options.Namespace ?? typeNamespace;
        string interfaceName = options.Name ?? "I" + type.Name;

        InterfaceSpec spec = new(
            HintName: HintName(typeNamespace, type),
            TypeNamespace: typeNamespace,
            TypeKeyword: type.IsRecord ? "record" : "class",
            TypeName: type.Name,
            InterfaceNamespace: interfaceNamespace,
            InterfaceName: interfaceName,
            Accessibility: type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
            TypeParameters: SymbolFormatter.TypeParameters(type),
            Constraints: new EquatableArray<string>(SymbolFormatter.Constraints(type)),
            Members: new EquatableArray<string>([.. members]));

        // An abstract type has no instance to hand a container, so it is never registered.
        // Saying so out loud only matters when a lifetime was asked for.
        if (type.IsAbstract && options.Lifetime != Lifetime.None)
        {
            if (options.Lifetime != Lifetime.Unset)
            {
                diagnostics.Add(new DiagnosticInfo(
                    Diagnostics.AbstractRegistration, attributeLocation, type.Name));
            }

            options.Lifetime = Lifetime.None;
        }

        RegistrationRequest request = new(
            Registration: options.Lifetime == Lifetime.None
                ? null
                : new Registration(
                    ServiceType: Qualified(interfaceNamespace, interfaceName, type.TypeParameters.Length),
                    ImplementationType: Qualified(typeNamespace, type.Name, type.TypeParameters.Length),
                    Lifetime: options.Lifetime == Lifetime.Unset ? null : options.Lifetime.ToString()),
            Explicit: options.Lifetime != Lifetime.Unset);

        return new Result(spec, new EquatableArray<DiagnosticInfo>([.. diagnostics]), request);
    }

    /// <summary>
    /// A type as <c>typeof</c> takes it. A generic type is registered open — <c>typeof(IRepo&lt;,&gt;)</c>
    /// — which is what a container binds a generic implementation through.
    /// </summary>
    private static string Qualified(string @namespace, string name, int arity)
    {
        StringBuilder builder = new StringBuilder("global::");

        if (@namespace.Length > 0)
        {
            builder.Append(@namespace).Append('.');
        }

        builder.Append(name);

        if (arity > 0)
        {
            builder.Append('<').Append(new string(',', arity - 1)).Append('>');
        }

        return builder.ToString();
    }

    /// <summary>
    /// The reason the type cannot be generated for, if there is one. Each of these would leave
    /// the generated file uncompilable, so nothing is emitted alongside them.
    /// </summary>
    private static DiagnosticInfo? Reject(
        INamedTypeSymbol type,
        LocationInfo? typeLocation,
        LocationInfo? attributeLocation,
        Options options)
    {
        if (options.Include is not null && options.Exclude is not null)
        {
            return new DiagnosticInfo(Diagnostics.IncludeWithExclude, attributeLocation, type.Name);
        }

        if (type.ContainingType is not null)
        {
            return new DiagnosticInfo(Diagnostics.NestedType, typeLocation, type.Name);
        }

        if (type.IsStatic)
        {
            return new DiagnosticInfo(Diagnostics.StaticType, typeLocation, type.Name);
        }

        if (!IsPartial(type))
        {
            return new DiagnosticInfo(Diagnostics.NotPartial, typeLocation, type.Name);
        }

        return null;
    }

    private static bool IsPartial(INamedTypeSymbol type) => type.DeclaringSyntaxReferences
        .Any(reference => reference.GetSyntax() is TypeDeclarationSyntax declaration
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    /// <summary>
    /// Fields cannot be declared in an interface, and neither can anything non-public: an
    /// interface member is implicitly public, so a member that is not would fail to implement it.
    /// </summary>
    private static bool IsEligible(ISymbol member)
    {
        if (member.IsStatic
            || member.IsImplicitlyDeclared
            || member.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        return member switch
        {
            // Ordinary excludes constructors, operators and destructors, as well as the accessors
            // that the properties and events below already carry.
            IMethodSymbol method => method.MethodKind == MethodKind.Ordinary && !OverridesObject(method),
            IPropertySymbol or IEventSymbol => true,
            _ => false,
        };
    }

    /// <summary>
    /// A hand-written <c>ToString</c> or <c>Equals</c> override is a public instance method like
    /// any other, but declaring it on the interface adds nothing: every implementation inherits
    /// it from <see cref="object"/> already.
    /// </summary>
    private static bool OverridesObject(IMethodSymbol method)
    {
        IMethodSymbol current = method;

        while (current.IsOverride && current.OverriddenMethod is { } overridden)
        {
            current = overridden;
        }

        return current.ContainingType?.SpecialType == SpecialType.System_Object;
    }

    /// <summary>
    /// The name Include and Exclude match against. An indexer is named <c>this[]</c>, which no
    /// <c>nameof</c> can produce, so its metadata name (<c>Item</c> by default) is used instead.
    /// </summary>
    private static string SelectorName(ISymbol member) =>
        member is IPropertySymbol { IsIndexer: true } ? member.MetadataName : member.Name;

    private static Options ReadOptions(AttributeData attribute)
    {
        Options options = default;

        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            switch (argument.Key)
            {
                case nameof(Options.Name):
                    options.Name = NonEmpty(argument.Value);
                    break;

                case nameof(Options.Namespace):
                    options.Namespace = NonEmpty(argument.Value);
                    break;

                case nameof(Options.Include):
                    options.Include = Names(argument.Value);
                    break;

                case nameof(Options.Exclude):
                    options.Exclude = Names(argument.Value);
                    break;

                case LifetimeName when argument.Value.Value is int value:
                    options.Lifetime = (Lifetime)value;
                    break;
            }
        }

        return options;

        static string? NonEmpty(TypedConstant value) =>
            value.Value is string text && text.Trim() is { Length: > 0 } trimmed ? trimmed : null;

        // An explicit empty array stays an empty array: `Include = []` means an empty interface,
        // not an unset option. Only null reads as unset.
        static string[]? Names(TypedConstant value) => value.IsNull
            ? null
            : [.. value.Values
                .Select(item => item.Value as string)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim())];
    }

    private static string Namespace(ITypeSymbol type)
    {
        if (type.ContainingNamespace is not { IsGlobalNamespace: false })
        {
            return string.Empty;
        }

        StringBuilder builder = new();

        // Built from the symbol chain rather than through a display format, which would allocate
        // a list of display parts to produce the same string.
        for (INamespaceSymbol? part = type.ContainingNamespace;
             part is { IsGlobalNamespace: false };
             part = part.ContainingNamespace)
        {
            if (builder.Length > 0)
            {
                builder.Insert(0, '.');
            }

            builder.Insert(0, part.Name);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Qualified by namespace and arity: two same-named types in one assembly must not collide on
    /// the hint name, which has to be unique.
    /// </summary>
    private static string HintName(string @namespace, INamedTypeSymbol type)
    {
        StringBuilder builder = new();

        if (@namespace.Length > 0)
        {
            builder.Append(@namespace).Append('.');
        }

        builder.Append(type.Name);

        if (type.TypeParameters.Length > 0)
        {
            builder.Append('_').Append(type.TypeParameters.Length);
        }

        return builder.Append(".g.cs").ToString();
    }

    private static void Emit(SourceProductionContext context, InterfaceSpec spec) =>
        context.AddSource(spec.HintName, SourceText.From(spec.Render(), Encoding.UTF8));

    private readonly record struct Result(
        InterfaceSpec? Spec,
        EquatableArray<DiagnosticInfo> Diagnostics,
        RegistrationRequest Request);

    private struct Options
    {
        public string? Name;
        public string? Namespace;
        public string[]? Include;
        public string[]? Exclude;
        public Lifetime Lifetime;
    }

    /// <summary>
    /// Mirrors the public <c>InterfaceGenerator.Lifetime</c> enum, which the generator cannot
    /// reference: it is compiled into the assembly being analyzed, not into this one. The values
    /// must stay in the same order.
    /// </summary>
    private enum Lifetime
    {
        Unset = 0,
        None,
        Transient,
        Scoped,
        Singleton,
    }
}
