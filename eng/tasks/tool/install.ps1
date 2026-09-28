#Requires -Version 7
# runx description="Pack and reinstall runx as a global tool (run it through 'dotnet run')."

# Packs runx and (re)installs exactly that build as a global tool.
# Windows locks an installed runx while it runs, so update it through 'dotnet run':
#   dotnet run --project src/MysterNybble.Runx -- tool:install

$ErrorActionPreference = 'Stop'

$packageId = 'MysterNybble.Runx'
# This script lives in eng/tasks/tool.
$repoRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$projectPath = Join-Path $repoRoot 'src' $packageId
$packageOutput = Join-Path $repoRoot 'artifacts' 'packages'

# Start from an empty folder so an older or higher-versioned package cannot be picked up.
# -LiteralPath matters: with -Path, brackets in the checkout path act as wildcards and can match other folders.
if (Test-Path -LiteralPath $packageOutput) {
    Remove-Item -LiteralPath $packageOutput -Recurse -Force
}

dotnet pack $projectPath --configuration Release --output $packageOutput
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$version = dotnet msbuild $projectPath -getProperty:Version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Restrict restore to the freshly packed folder so a published package with the same version cannot win.
# The XML is built with the DOM so paths containing characters such as '&' are escaped.
$config = [xml]'<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear /></packageSources></configuration>'
$source = $config.CreateElement('add')
$source.SetAttribute('key', 'local')
$source.SetAttribute('value', $packageOutput)
$config.DocumentElement['packageSources'].AppendChild($source) | Out-Null
$configPath = Join-Path $packageOutput 'nuget.config'
$config.Save($configPath)

# Everything that can fail has been prepared above, before the installed tool is touched.
$installedVersion = dotnet tool list --global $packageId |
    Select-Object -Skip 2 |
    ForEach-Object { ($_.Trim() -split '\s+')[1] } |
    Select-Object -First 1

if ($installedVersion -eq $version) {
    # 'dotnet tool update' skips a version that is already installed, so reinstall it.
    dotnet tool uninstall --global $packageId
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet tool install --global $packageId --version $version --configfile $configPath
}
else {
    # Update installs the new version before removing the old one, and installs it if missing.
    dotnet tool update --global $packageId --version $version --configfile $configPath --allow-downgrade
}

exit $LASTEXITCODE
