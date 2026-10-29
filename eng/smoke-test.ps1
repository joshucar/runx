#Requires -Version 7

# Installs only the freshly packed tool and runs it in an isolated workspace.
param(
    [string] $PackageDirectory = (Join-Path $PSScriptRoot '../artifacts/packages')
)

$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param([string] $Command, [string[]] $Arguments)

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

$packageDirectoryPath = (Resolve-Path -LiteralPath $PackageDirectory).Path
$packages = @(Get-ChildItem -LiteralPath $packageDirectoryPath -Filter 'MysterNybble.Runx.*.nupkg')
if ($packages.Count -ne 1) {
    throw "Expected exactly one runx package in $packageDirectoryPath; found $($packages.Count)."
}
$version = $packages[0].BaseName.Substring('MysterNybble.Runx.'.Length)

# A unique directory makes repeated local checks independent of previous installs.
$smokeRoot = Join-Path $PSScriptRoot "../artifacts/smoke/$([Guid]::NewGuid().ToString('N'))"
$workspace = Join-Path $smokeRoot 'workspace'
$toolPath = Join-Path $smokeRoot 'tool'
New-Item -ItemType Directory -Path $workspace -Force | Out-Null
$workspace = (Resolve-Path -LiteralPath $workspace).Path

$config = [xml]'<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear /></packageSources></configuration>'
$source = $config.CreateElement('add')
$source.SetAttribute('key', 'packed-tool')
$source.SetAttribute('value', $packageDirectoryPath)
$config.DocumentElement['packageSources'].AppendChild($source) | Out-Null
$configPath = Join-Path $smokeRoot 'nuget.config'
$config.Save($configPath)

$previousPackages = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = Join-Path $smokeRoot 'cache'
    Invoke-Checked dotnet @('tool', 'install', 'MysterNybble.Runx', '--version', $version,
        '--tool-path', $toolPath, '--configfile', $configPath, '--no-cache')

    $tool = Join-Path $toolPath $(if ($IsWindows) { 'runx.exe' } else { 'runx' })
    $reportedVersion = Invoke-Checked $tool @('--version')
    if (($reportedVersion -split '\+')[0] -ne $version) {
        throw "Expected version $version; the installed tool reported $reportedVersion."
    }

    $taskFile = Join-Path $workspace 'runx.json'
    Invoke-Checked $tool @('--init', '--file', $taskFile)
    Invoke-Checked $tool @('--file', $taskFile, '--check')
    $tasks = Invoke-Checked $tool @('--file', $taskFile, '--list', '--json') | ConvertFrom-Json
    if ($tasks.tasks.name -notcontains 'dotnet-version') {
        throw 'The installed tool did not list the starter task.'
    }
    Invoke-Checked $tool @('--file', $taskFile, 'dotnet-version')
    Write-Output "Package smoke test passed for $version."
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
}
