#!/usr/bin/env pwsh
<#
.SYNOPSIS
    End-to-end check that the InterfaceGenerator package reaches a consumer transitively.

.DESCRIPTION
    Packs InterfaceGenerator into a local feed, packs Demo.Services (a library that uses the
    generator) against it, then builds Demo.App, which references Demo.Services and nothing else.
    Demo.App compiles only if the analyzer flowed through Demo.Services, and only if the interface
    generated inside Demo.Services is usable from another assembly.

.PARAMETER Version
    Overrides the package version. Defaults to InterfaceGeneratorVersion from
    Directory.Build.props, so this script never carries a second copy of it.
#>
[CmdletBinding()]
param(
    [string] $Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$repo = Resolve-Path (Join-Path $root '..' '..')
$package = Join-Path $repo 'src' 'InterfaceGenerator.Abstractions'
$artifacts = Join-Path $root '.artifacts'
$feed = Join-Path $artifacts 'feed'

function Invoke-Step {
    param([string] $Name, [scriptblock] $Action)

    Write-Host "==> $Name" -ForegroundColor Cyan
    $result = & $Action

    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }

    $result
}

if (-not $Version) {
    $Version = (dotnet msbuild $package -getProperty:InterfaceGeneratorVersion).Trim()
}

# A stale package or restore cache would silently invalidate the result.
$stale = @(
    $artifacts
    Join-Path $root 'Demo.Services' 'obj'
    Join-Path $root 'Demo.Services' 'bin'
    Join-Path $root 'Demo.App' 'obj'
    Join-Path $root 'Demo.App' 'bin'
)

Remove-Item $stale -Recurse -Force -ErrorAction Ignore

Invoke-Step 'Pack InterfaceGenerator' {
    dotnet pack $package -c Release -o $feed "-p:InterfaceGeneratorVersion=$Version"
} | Out-Host

Invoke-Step 'Pack Demo.Services' {
    dotnet pack (Join-Path $root 'Demo.Services') -c Release -o $feed "-p:InterfaceGeneratorVersion=$Version"
} | Out-Host

$output = Invoke-Step 'Run Demo.App' {
    dotnet run --project (Join-Path $root 'Demo.App') | Select-Object -Last 1
}

# Demo.App only compiles because the generator ran in both assemblies, and only prints 2 because
# the container resolved both generated interfaces. A successful run is most of the proof.
if ($output -ne '2') {
    throw "Demo.App printed '$output', expected '2'."
}

$generated = Get-ChildItem (Join-Path $root 'Demo.App' 'obj' 'GeneratedFiles') -Recurse -Filter '*.g.cs'

if (-not $generated) {
    throw 'Nothing was emitted in Demo.App.'
}

$content = ($generated | ForEach-Object { Get-Content $_.FullName -Raw }) -join [Environment]::NewLine

$expectations = @(
    'public interface IGreeter'                             # the interface, generated in Demo.App
    'partial class Greeter : global::IGreeter'              # applied to the annotated class
    'AddDemoAppServices('                                   # the registration method, named after this assembly
    'typeof(global::IGreeter), typeof(global::Greeter)'     # registered against its implementation
)

foreach ($expected in $expectations) {
    if ($content -notmatch [regex]::Escape($expected)) {
        throw "Generated files are missing '$expected'.`n$content"
    }
}

Write-Host ''
Write-Host "PASS: InterfaceGenerator $Version reached Demo.App through Demo.Services." -ForegroundColor Green
Write-Host $content
