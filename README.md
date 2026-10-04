# runx

`runx` is an experimental repo command runner for .NET projects. It is meant to
be the command palette/front door for a repo, not a replacement for NUKE, Cake,
FAKE, MSBuild, or other heavier build systems.

Current development identity:

- Package ID: `MysterNybble.Runx`
- Tool command: `runx`
- Root namespace: `MysterNybble.Runx`

## Install From Source

```powershell
dotnet run --project src\MysterNybble.Runx -- tool:install
runx --list
```

`tool:install` is the script `eng/tasks/tool/install.ps1` (see Script Tasks). It
packs the tool into `artifacts/packages` and reinstalls it as a global tool. Run
it through `dotnet run` rather than an installed `runx`, because Windows locks
the installed tool while it runs. Remove it with
`dotnet tool uninstall --global MysterNybble.Runx`.

## Build

This repo uses its own `runx.json`:

```powershell
runx build
runx test
runx ci
```

Without `runx` installed, use `dotnet build`, `dotnet test`, or
`dotnet run --project src\MysterNybble.Runx -- <task>`. Tests run on
Microsoft.Testing.Platform, which `global.json` opts `dotnet test` into.

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
    "test": {
      "description": "Build, then run the tests.",
      "command": "dotnet",
      "args": ["test", "--no-build"],
      "dependsOn": ["build"]
    },
    "smoke": {
      "description": "Run a PowerShell smoke check.",
      "script": "Write-Output smoke",
      "shell": "pwsh"
    },
    "ci": {
      "description": "Test and smoke check.",
      "dependsOn": ["test", "smoke"]
    }
  }
}
```

Each task defines one of:

- `command` plus optional `args`: launches the executable directly, without a
  shell. Bare names are looked up only on `PATH`, including a `PATH` set in the
  task's `env` (on Windows this includes `.cmd` and `.bat` shims such as `npm`).
  Relative paths such as `./build.sh` resolve from the task's `cwd`.
- `script` plus `shell`: runs the script text with `cmd`, `pwsh`, or `sh`. A
  one-line `cmd` script runs like `cmd /c`; a multi-line one runs as a temporary
  batch file, so `for` loops need `%%i`. As in a batch file or `sh` script, a
  failing line does not stop the script; add `|| exit /b` (cmd) or `set -e`
  (sh) where that matters.
- only `dependsOn`: runs its dependencies and nothing else.

Optional task properties:

- `description`: shown by `runx --list` and the picker.
- `dependsOn`: tasks to run first, in the order listed. Dependencies of
  dependencies run first too, and each task runs at most once. The run stops at
  the first task that fails, and Ctrl+C stops it even if the interrupted task
  exits cleanly. Cycles and unknown task names are config errors.
- `cwd`: working directory, relative to the directory containing `runx.json`.
  Defaults to that directory.
- `env`: environment variables added to the task's process.

Unknown properties are errors. The JSON schema lives in
[`schemas/v1/runx.schema.json`](schemas/v1/runx.schema.json).

Tasks run attached to the current console, so colors, progress output, and
prompts behave as if the command were run directly. `runx` exits with the task's
exit code. On Windows, `.cmd` and `.bat` commands receive their arguments
escaped for `cmd.exe`, so characters such as `&`, `|`, `%`, and `"` arrive
intact instead of being interpreted.

## For People and AI Agents

```powershell
runx --guide            # how to add tasks, plus where they go in this repo
runx --check            # validate everything without running anything
runx --info <task>      # what a task runs, where it comes from, its plan
runx --list --json      # every task, including hidden ones, as JSON
runx --install-skill    # install the runx skill for Claude Code
```

`--guide` is built into the tool, so it always matches the installed version,
and it ends with details for the current repo, such as where new scripts go.
`--check` also warns about scripts that were skipped and header mistakes such
as a misspelled key, and `--check --json` and `--info <task> --json` give the
same information as JSON.

`runx --install-skill` writes `~/.claude/skills/runx/SKILL.md` (or under
`CLAUDE_CONFIG_DIR`). Claude Code then uses it whenever it is asked to add or
run tasks, and the skill tells it to start with `runx --guide`, so a fresh
session can handle "add a runx task that does X" without further
instructions. Run it again after updating runx; it reports when the skill is
already up to date.

## Usage

```powershell
runx
runx --list
runx build
runx build -- --configuration Release
runx --dry-run build
runx --init
```

Running `runx` with no task opens a picker: use the arrow keys or type to
filter by name or description, press Enter to run the task, or Esc to clear the
filter and then cancel. A status bar under the list shows what the highlighted
task runs: its command line or script, plus its dependencies, `cwd`, and `env`
names. When input or output is redirected (CI, pipes), `runx` prints the task
list instead. `runx --list` always prints the list, and `runx --dry-run` picks a
task and shows what would run, including the full dependency plan.

Arguments after `--` are appended to the requested `command` task's `args`, not
to its dependencies. Shell tasks and `dependsOn`-only tasks do not accept extra
arguments.

By default, config discovery stays inside the current Git repo. Outside a Git
repo, only the current directory is checked. Use `--file <path>` for an explicit
config file or `--allow-parent-search` to permit walking beyond that boundary.

## Script Tasks

Scripts in a repo's `eng/tasks/` folder are tasks without any config. The
quickest way to add one is `runx --new`, which creates the script with its
header filled in and a TODO body that fails until you replace it:

```powershell
runx --new db:seed --type cs --description "Seed the local database" --depends build
```

`--type` is `ps1` (the default), `cs`, `sh`, or `cmd`, and `--hide` marks a
helper task. `runx --new` refuses names that would not be discovered, names
`runx.json` already uses, and dependencies that do not exist. Creating
`build.sh` next to an existing `build.ps1` makes one task that runs the right
script on each operating system.

```
eng/tasks/
  build.ps1          -> build
  build.sh           -> build (used on Linux and macOS)
  hello.cs           -> hello (run with 'dotnet run --file')
  release/
    pack.ps1         -> release:pack
  db/
    _default.cmd     -> db
  lib/
    _helpers.ps1     (names starting with '_' or '.' are helpers, not tasks)
```

- Subfolders become name prefixes separated by `:`, and a file named `_default`
  is its folder's task.
- `.ps1` runs with `pwsh -File`, `.cs` with `dotnet run --file` (a .NET 10
  file-based app), `.cmd` and `.bat` with `cmd` on Windows, and `.sh` with `sh`.
  Other files run through their `#!` line, or directly when executable on Linux
  and macOS. Files that are none of these, such as a README, are ignored.
- When several scripts share a name, Windows prefers `.ps1`, `.cmd`, `.bat`,
  `.cs`, then `.sh`; Linux and macOS prefer `.sh`, then `.ps1` and `.cs`.
- Scripts run from the repo root, and arguments after `--` go to the script:
  `runx release:pack -- -Configuration Debug`.
- Header comments near the top of a script start with the comment marker for
  its language (`#`, `//`, `::`, or `REM`), then `runx` and a `key=value`:

  ```powershell
  # runx description="Pack the NuGet packages"
  # runx depends=["build"]
  # runx hide=true
  ```

  `description` shows in the picker, `depends` works like `dependsOn`, and
  `hide` keeps the task out of the picker and `--list` while it still runs by
  name and as a dependency. Values are quoted strings, `true` or `false`, or
  `["a", "b"]` lists.
- Script tasks are marked `(script)`. A task in `runx.json` with the same name
  wins, which lets `runx.json` wrap a script.

To look somewhere else, set `taskFolders` in `runx.json` or in a personal tasks
file. The most specific file that sets it wins (the repo's personal file, then
`runx.json`, then broader personal files), and it replaces `eng/tasks`. When it
lists several folders, the first one that defines a name wins:

```json
{
  "version": 1,
  "taskFolders": ["build/tasks"],
  "tasks": {}
}
```

Folders in `runx.json` are relative to it; folders in personal files are
relative to the repo root. `"taskFolders": []` turns script discovery off.
`runx --where` shows the folders in use. `--file` uses exactly the file given,
without script tasks.

Any task in `runx.json` can also set `"hide": true`.

## Personal Tasks

Personal tasks are tasks for a repo that live outside it, so you can use `runx`
in repos where you can't or don't want to commit a `runx.json`.

```powershell
runx --init --personal                  # tasks for this repo, in every worktree
runx --init --personal --scope origin   # tasks for every repo on origin's host
runx --init --personal --scope global   # tasks for every repo
runx --where                            # show which task files apply here
```

Personal files live under `%USERPROFILE%\.runx\` (`~/.runx/` on Linux and
macOS). Set `RUNX_HOME` to keep them somewhere else, such as a dotfiles
checkout. For a repo whose `origin` is `git@github.com:acme/api.git`, these
files apply, broadest first:

| Scope | File | Applies to |
|---|---|---|
| `global` | `global.json` | every repo |
| `origin` | `repos\github.com.json` | every repo on GitHub |
| `origin/acme` | `repos\github.com\acme.json` | every repo under `acme` |
| `repo` | `repos\github.com\acme\api.json` | this repo, in every worktree |

Scopes are named after the remote, so they read the same in every repo; the
files are named after the remote's address, so `https://github.com/acme/api`
finds the same ones. A repo without a remote is keyed by the folder of its main
checkout. `runx --where` lists the files for the current repo and which exist.

The files use the same format as `runx.json`. Their tasks:

- are added to the repo's `runx.json` tasks, or used on their own when the repo
  has none;
- run from the root of the worktree you are in, with `cwd` relative to it;
- override tasks of the same name from broader personal files;
- give way to the repo's own tasks and script tasks of the same name, except
  that the `repo` scope file must not reuse one of those names;
- can depend on the repo's tasks, but repo tasks cannot depend on them. A task
  from a broader file whose dependencies a repo lacks is hidden in that repo, so
  a global `ship` that depends on `build` and `test` only appears where both
  exist;
- are marked with their scope in the picker and in `runx --list`, such as
  `(global)`, `(origin)`, or `(personal)` for the repo's own file.

Personal tasks apply only inside Git repos. `--file` uses exactly the file
given, without personal tasks.
