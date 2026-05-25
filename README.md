# runx

`runx` is an experimental repo command runner for .NET projects. It is meant to
be the command palette/front door for a repo, not a replacement for NUKE, Cake,
FAKE, MSBuild, or other heavier build systems.

Current development identity:

- Package ID: `MysterNybble.Runx`
- Tool command: `runx`
- Root namespace: `MysterNybble.Runx`

## Build

```powershell
dotnet restore
dotnet build
dotnet test
```

## Run Locally

```powershell
dotnet run --project src\MysterNybble.Runx -- --help
dotnet run --project src\MysterNybble.Runx -- --version
dotnet run --project src\MysterNybble.Runx -- --init
dotnet run --project src\MysterNybble.Runx -- --list
dotnet run --project src\MysterNybble.Runx -- dotnet-version
```

## Task Files

`runx` reads `runx.json`:

```json
{
  "$schema": "https://schemas.mysternybble.com/runx/v1/runx.schema.json",
  "version": 1,
  "tasks": {
    "build": {
      "description": "Build the repo.",
      "command": "dotnet",
      "args": ["build"]
    },
    "smoke": {
      "description": "Run a PowerShell smoke check.",
      "script": "Write-Output smoke",
      "shell": "pwsh"
    }
  }
}
```

Structured tasks use `command` plus optional `args`. Shell tasks use `script`
plus an explicit `shell`; v1 supports `cmd`, `pwsh`, and `sh`.

By default, config discovery stays inside the current Git repo. Outside a Git
repo, only the current directory is checked. Use `--file <path>` for an explicit
config file or `--allow-parent-search` to permit walking beyond that boundary.

Useful commands:

```powershell
runx --init
runx --list
runx build
runx build -- --configuration Release
runx --dry-run build
```

## Pack As A Tool

```powershell
dotnet pack src\MysterNybble.Runx
```
