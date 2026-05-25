# runx

`runx` is an experimental repo command runner for .NET projects.

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
```

## Pack As A Tool

```powershell
dotnet pack src\MysterNybble.Runx
```
