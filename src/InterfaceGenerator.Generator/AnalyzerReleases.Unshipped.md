; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
IG0001 | InterfaceGenerator | Error | Annotated type must be partial
IG0002 | InterfaceGenerator | Error | Nested types are not supported
IG0003 | InterfaceGenerator | Error | Static types cannot implement an interface
IG0004 | InterfaceGenerator | Error | Include and Exclude are mutually exclusive
IG0005 | InterfaceGenerator | Warning | Listed member name matches nothing
IG0006 | InterfaceGenerator | Warning | An abstract type cannot be registered
IG0007 | InterfaceGenerator | Warning | Service registration needs Microsoft.Extensions.DependencyInjection.Abstractions
