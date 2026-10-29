#Requires -Version 7

# Validate the package that CI built before it can be published.
param(
    [string] $PackageDirectory = (Join-Path $PSScriptRoot '../artifacts/packages'),
    [string] $Tag,
    [string] $ExpectedCommit
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '../src/MysterNybble.Runx'
$version = dotnet msbuild $projectPath -getProperty:PackageVersion
if ($LASTEXITCODE -ne 0) {
    throw 'Could not determine the package version.'
}
$version = $version.Trim()
if ($Tag -and $Tag -cne "v$version") {
    throw "Tag '$Tag' does not match the package version '$version'. Expected 'v$version'."
}

$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg')
if ($packages.Count -ne 1 -or $packages[0].Name -cne "MysterNybble.Runx.$version.nupkg") {
    throw "Expected exactly one package named MysterNybble.Runx.$version.nupkg."
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $nuspec = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec') })
    if ($nuspec.Count -ne 1) {
        throw 'Expected exactly one package manifest.'
    }
    $reader = [System.IO.StreamReader]::new($nuspec[0].Open())
    try {
        $manifest = [xml]$reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}
finally {
    $archive.Dispose()
}

$metadata = $manifest.package.metadata
if ($metadata.id -cne 'MysterNybble.Runx' -or $metadata.version -cne $version) {
    throw 'The package manifest does not match the expected identity and version.'
}
if ($ExpectedCommit -and $metadata.repository.commit -ne $ExpectedCommit) {
    throw "The package source commit does not match $ExpectedCommit."
}

Write-Output $version
