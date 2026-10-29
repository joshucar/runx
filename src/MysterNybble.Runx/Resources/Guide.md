# runx guide

runx runs a repo's tasks. Tasks come from three places, and `runx --list`
shows them all:

1. Scripts in the repo's task folder, `eng/tasks/` by default. This is the
   preferred way to add a task.
2. The repo's `runx.json`, for tasks that call a command directly, wrap a
   script with fixed arguments, or only run other tasks.
3. The user's personal task files, kept outside the repo, for repos the user
   cannot or does not want to commit to.

## Add a script task

Run `runx --new` from anywhere in the repo. It creates the script in the task
folder with its header filled in:

    runx --new db:seed --type cs --description "Seed the local database" --depends build

Then replace the TODO in the new file with the task's work. Types:

| Type          | File                      | Runs with                  | Arguments arrive in |
|---------------|---------------------------|----------------------------|---------------------|
| `ps1` (default) | `eng/tasks/db/seed.ps1` | `pwsh -File`               | `param()`           |
| `cs`          | `eng/tasks/db/seed.cs`    | `dotnet run --file` (.NET 10 file-based app) | `args` |
| `sh`          | `eng/tasks/db/seed.sh`    | `sh` (or its `#!` line)    | `"$@"`              |
| `cmd`         | `eng/tasks/db/seed.cmd`   | `cmd` (Windows only)       | `%*`                |

Rules for script tasks:

- The path under the task folder is the task name: `eng/tasks/db/seed.cs` is
  `db:seed`, and `eng/tasks/db/_default.ps1` is `db`. Files and folders whose
  names start with `_` or `.` are helpers, not tasks.
- Scripts run from the repo root, so use paths relative to the repo root.
- `runx db:seed -- --count 5` passes everything after `--` to the script.
- A non-zero exit code fails the task and stops any tasks that depend on it.
- The header is comment lines near the top, using the comment marker for the
  language (`#` for ps1 and sh, `//` for cs, `::` or `REM` for cmd):

      // runx description="Seed the local database"
      // runx depends=["build"]
      // runx hide=true

  `description` shows in the picker and `--list`. `depends` names tasks to run
  first. `hide=true` keeps a helper task out of the picker and `--list`; it
  still runs by name and as a dependency. Values are quoted strings, `true` or
  `false`, or lists such as `["build", "test"]`.
- C# scripts can reference packages with `#:package Name@Version` lines after
  the header. The repo's `Directory.Build.props` applies to them, so keep them
  free of warnings.
- If both `build.ps1` and `build.sh` exist, Windows runs the `.ps1` and Linux
  and macOS run the `.sh`.

## Tasks in runx.json

    {
      "$schema": "https://raw.githubusercontent.com/joshucar/runx/master/schemas/v1/runx.schema.json",
      "version": 1,
      "tasks": {
        "build": { "description": "Build the solution.", "command": "dotnet", "args": ["build"] },
        "test": { "command": "dotnet", "args": ["test", "--no-build"], "dependsOn": ["build"] },
        "lint": { "script": "dotnet format --verify-no-changes", "shell": "pwsh" },
        "ci": { "description": "Everything CI runs.", "dependsOn": ["test", "lint"] }
      }
    }

A task has exactly one of `command` (with optional `args`), `script` with
`shell` (`pwsh`, `sh`, or `cmd`), or only `dependsOn`. Optional properties are
`description`, `cwd` (relative to runx.json), `env`, `dependsOn`, and `hide`.
Unknown properties are errors. A `runx.json` task wins over a script with the
same name. `"taskFolders": ["build/tasks"]` at the top level moves the task
folder.

## Personal tasks

Personal task files use the same format as `runx.json` and live under
`~/.runx/` (or `RUNX_HOME`), so nothing is added to the repo. Create one with
`runx --init --personal`, which applies to this repo in every worktree. Add
`--scope global` for every repo, or `--scope origin` for every repo on the
same host as the `origin` remote. `runx --where` shows which files apply.
Personal tasks run from the repo root and can depend on the repo's tasks.

## Check your work

- `runx --check` validates everything without running anything, and warns
  about scripts that were skipped or have header mistakes.
- `runx --info <task>` shows what a task runs, where it comes from, and the
  order its dependencies run in.
- `runx --list --json` and `runx --info <task> --json` give machine-readable
  output.
- `runx --dry-run <task>` prints the exact commands without running them.
