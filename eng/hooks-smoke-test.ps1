#Requires -Version 7

# Runs only in an isolated workspace against a freshly installed tool.
param(
    [Parameter(Mandatory)] [string] $Tool,
    [Parameter(Mandatory)] [string] $Workspace,
    [Parameter(Mandatory)] [string] $PackageVersion,
    [Parameter(Mandatory)] [string] $NuGetConfig
)

$ErrorActionPreference = 'Stop'
$Tool = (Resolve-Path -LiteralPath $Tool).Path
$NuGetConfig = (Resolve-Path -LiteralPath $NuGetConfig).Path
$gitVersion = & git --version
if ($LASTEXITCODE -ne 0 -or $gitVersion -notmatch '(\d+\.\d+\.\d+)' -or [version]$Matches[1] -lt [version]'2.54.0') {
    Write-Output 'Git hook smoke test skipped: Git 2.54+ is required.'
    return
}

function Invoke-HookCheck {
    param([string] $Command, [string[]] $Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

$root = Join-Path $Workspace "hook repo's files"
$linked = Join-Path $Workspace 'linked worktree'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$oldGlobal = $env:GIT_CONFIG_GLOBAL
$oldSystem = $env:GIT_CONFIG_NOSYSTEM
$oldExit = $env:RUNX_HOOK_TEST_EXIT
try {
    $env:GIT_CONFIG_GLOBAL = Join-Path $Workspace 'empty-git-config'
    [IO.File]::WriteAllText($env:GIT_CONFIG_GLOBAL, '')
    $env:GIT_CONFIG_NOSYSTEM = '1'
    Push-Location $root
    try {
        Invoke-HookCheck git @('init', '--quiet')
        Invoke-HookCheck git @('config', 'user.name', 'runx smoke test')
        Invoke-HookCheck git @('config', 'user.email', 'runx-test@example.invalid')
        New-Item -ItemType Directory -Path 'eng/tasks' -Force | Out-Null
        @'
# runx description="Receive Git hook input"
# runx usage="Git supplies hook-specific arguments and stdin."
# runx depends=["setup"]
$record = @{ arguments = @($args); input = [Console]::In.ReadToEnd(); cwd = (Get-Location).Path } | ConvertTo-Json -Compress
[IO.File]::AppendAllText((Join-Path (Get-Location) 'hook-results.jsonl'), $record + [Environment]::NewLine)
if ($env:RUNX_HOOK_TEST_EXIT) { exit ([int]$env:RUNX_HOOK_TEST_EXIT) }
'@ | Set-Content 'eng/tasks/entry.ps1'
        @'
# runx description="Verify dependencies cannot consume hook stdin"
if ([Console]::In.ReadToEnd().Length -ne 0) { throw 'A dependency consumed hook input.' }
'@ | Set-Content 'eng/tasks/setup.ps1'
        @'
{"version":1,"hooks":{"pre-commit":"entry","commit-msg":"entry","pre-push":"entry"},"tasks":{}}
'@ | Set-Content 'runx.json'
        # These files belong to Git checks, not to the fixture commit.
        "hook-results.jsonl`nother-hook.txt`nrefs.txt`n" | Set-Content '.gitignore'
        New-Item -ItemType Directory -Path 'custom-hooks' -Force | Out-Null
        "#!/bin/sh`nprintf 'other hook\n' >> other-hook.txt`n" | Set-Content -NoNewline 'custom-hooks/pre-commit'
        if (-not $IsWindows) { Invoke-HookCheck chmod @('+x', 'custom-hooks/pre-commit') }
        Invoke-HookCheck git @('config', 'core.hooksPath', 'custom-hooks')
        Invoke-HookCheck $Tool @('--install-hooks')
        $firstInstall = Get-Content -Raw '.git/config'
        if ((Invoke-HookCheck $Tool @('--hook-protocol')).Trim() -ne '1') { throw 'Hook protocol was not reported.' }
        Invoke-HookCheck $Tool @('--install-hooks')
        if ((Get-Content -Raw '.git/config') -cne $firstInstall) { throw 'Hook install was not idempotent.' }
        $check = Invoke-HookCheck $Tool @('--check', '--json') | ConvertFrom-Json
        if (-not $check.valid -or @($check.warnings).Count) { throw 'Installed hooks failed --check.' }
        $info = Invoke-HookCheck $Tool @('--info', 'entry', '--json') | ConvertFrom-Json
        if ($info.hooks.Count -ne 3 -or -not $info.usage) { throw 'Hook and usage metadata was not reported.' }
        Invoke-HookCheck git @('add', '.')
        Invoke-HookCheck git @('commit', '--quiet', '-m', 'Test installed hooks')
        $records = @(Get-Content 'hook-results.jsonl' | ForEach-Object { $_ | ConvertFrom-Json })
        if ($records.Count -ne 2 -or $records[0].arguments.Count -ne 0 -or $records[1].arguments.Count -ne 1) {
            throw 'Commit hooks did not receive the expected arguments.'
        }
        if (-not (Test-Path 'other-hook.txt')) { throw 'Existing hook did not run.' }
        $payload = "refs/heads/main 123 refs/heads/main 456`n"
        [IO.File]::WriteAllText((Join-Path $root 'refs.txt'), $payload)
        Invoke-HookCheck git @('hook', 'run', '--to-stdin=refs.txt', 'pre-push', '--', 'origin', 'ssh://example.invalid/a repo')
        $record = Get-Content 'hook-results.jsonl' -Tail 1 | ConvertFrom-Json
        if ($record.input -cne $payload -or $record.arguments[1] -cne 'ssh://example.invalid/a repo') {
            throw 'Pre-push input or arguments were changed or consumed.'
        }
        $head = & git rev-parse HEAD
        $env:RUNX_HOOK_TEST_EXIT = '7'
        & git commit --allow-empty --quiet -m 'Must be rejected'
        if ($LASTEXITCODE -eq 0 -or (& git rev-parse HEAD) -ne $head) { throw 'A failing hook did not reject the commit.' }
        $env:RUNX_HOOK_TEST_EXIT = $null
        Invoke-HookCheck git @('worktree', 'add', '--quiet', '--detach', $linked)
        Push-Location $linked
        try {
            Invoke-HookCheck git @('hook', 'run', 'pre-commit')
            $record = Get-Content 'hook-results.jsonl' -Tail 1 | ConvertFrom-Json
            if ($record.cwd -ne (Get-Location).Path) { throw 'Hook used the wrong worktree.' }
            Remove-Item 'runx.json'
            $count = @(Get-Content 'hook-results.jsonl').Count
            Invoke-HookCheck git @('hook', 'run', 'pre-commit')
            if (@(Get-Content 'hook-results.jsonl').Count -ne $count) { throw 'A missing worktree binding executed a task.' }
            $sharedBefore = Invoke-HookCheck git @('config', '--get', 'hook.runx-pre-commit.command')
            Invoke-HookCheck $Tool @('--install-hooks')
            if ((Invoke-HookCheck git @('config', '--get', 'hook.runx-pre-commit.command')) -ne $sharedBefore) {
                throw 'Installing without bindings removed another worktree registration.'
            }
            '{"version":1,"tasks":{"broken":{"command":"dotnet","script":"invalid","shell":"sh"}}}' | Set-Content 'runx.json'
            Invoke-HookCheck git @('hook', 'run', 'pre-commit')
            if (@(Get-Content 'hook-results.jsonl').Count -ne $count) { throw 'An unbound event executed tasks from an invalid config.' }
            Invoke-HookCheck $Tool @('--install-hooks', '--prune')
        }
        finally { Pop-Location }
        if ((& git config --get core.hooksPath) -ne 'custom-hooks') { throw 'Uninstall changed core.hooksPath.' }
        & git config --get hook.runx-pre-commit.command | Out-Null
        if ($LASTEXITCODE -eq 0) { throw 'Uninstall left a runx command registered.' }
        if (-not (Test-Path 'custom-hooks/pre-commit')) { throw 'Uninstall removed another hook.' }
        Invoke-HookCheck git @('config', '--global', 'hook.runx-pre-commit.enabled', 'false')
        $preferences = Get-Content -Raw $env:GIT_CONFIG_GLOBAL
        Invoke-HookCheck $Tool @('--install-hooks')
        $disabled = Invoke-HookCheck $Tool @('--check', '--json') | ConvertFrom-Json
        if (-not ($disabled.warnings -like '*disabled*') -or ($disabled.warnings -like '*overrides*')) { throw 'Global disablement was treated as a conflict.' }
        Invoke-HookCheck $Tool @('--uninstall-hooks')
        if ((Get-Content -Raw $env:GIT_CONFIG_GLOBAL) -cne $preferences) { throw 'Uninstall changed global preferences.' }
        Invoke-HookCheck git @('config', '--global', '--unset', 'hook.runx-pre-commit.enabled')
        Invoke-HookCheck dotnet @('new', 'tool-manifest', '--output', '.config')
        Invoke-HookCheck dotnet @('tool', 'install', '--local', 'MysterNybble.Runx', '--version', $PackageVersion, '--configfile', $NuGetConfig)
        Invoke-HookCheck dotnet @('tool', 'run', 'runx', '--', '--install-hooks')
        $command = & git config --get hook.runx-pre-commit.command
        if ($command -notlike '* tool run runx -- --hook pre-commit --') { throw 'Installation did not select the local tool.' }
        Invoke-HookCheck git @('hook', 'run', 'pre-commit')
        Invoke-HookCheck dotnet @('tool', 'run', 'runx', '--', '--uninstall-hooks')
        New-Item -ItemType Directory -Path 'nested' -Force | Out-Null
        Move-Item '.config' 'nested/.config'
        $before = Get-Content -Raw '.git/config'
        Push-Location 'nested'
        try {
            & dotnet tool run runx -- --install-hooks
            if ($LASTEXITCODE -eq 0) { throw 'A nested local-tool invocation silently chose a different launcher.' }
        }
        finally { Pop-Location }
        if ((Get-Content -Raw '.git/config') -cne $before) { throw 'Failed launcher selection changed Git config.' }
        Move-Item 'nested/.config' '.config'
        $manifestPath = '.config/dotnet-tools.json'
        $manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
        $manifest.tools.'mysternybble.runx'.version = '99.0.0-unrestored'
        $manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath
        $before = Get-Content -Raw '.git/config'
        & $Tool --install-hooks
        if ($LASTEXITCODE -eq 0 -or (Get-Content -Raw '.git/config') -cne $before) {
            throw 'An unrestored local tool silently fell back or changed Git config.'
        }
        Write-Output 'Git hook smoke test passed: installation, coexistence, arguments, stdin, failure, worktrees, removal, and manifest selection.'
    }
    finally { Pop-Location }
}
finally {
    $env:GIT_CONFIG_GLOBAL = $oldGlobal
    $env:GIT_CONFIG_NOSYSTEM = $oldSystem
    $env:RUNX_HOOK_TEST_EXIT = $oldExit
}
