# runx

`runx` is a repo command runner for .NET projects, currently in beta. It is meant to
be the command palette/front door for a repo, not a replacement for NUKE, Cake,
FAKE, MSBuild, or other heavier build systems.

## Install

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
PowerShell tasks also require [PowerShell 7](https://aka.ms/powershell).

Install the beta from NuGet:

```powershell
dotnet tool install --global MysterNybble.Runx --version 1.0.0-beta.1
```

Update to the latest version, including betas:

```powershell
dotnet tool update --global MysterNybble.Runx --prerelease
```

Remove it with `dotnet tool uninstall --global MysterNybble.Runx`.

## Repo-local installation

Global installation is convenient across repos. To pin a version for a team,
create a tool manifest if the repo does not already have one, then install the
chosen version locally:

```powershell
dotnet new tool-manifest --output .config
dotnet tool install --local MysterNybble.Runx --version <version>
```

Commit `.config/dotnet-tools.json`. Contributors restore and invoke it with:

```powershell
dotnet tool restore
dotnet tool run runx -- --guide
dotnet tool run runx -- build
```

The first `--` belongs to `dotnet tool run`; it preserves runx options and any
second `--` used to pass task arguments. Use that explicit invocation in hooks
and automation too. A manifest does not
redirect bare `runx` away from a global installation. It pins the runner package;
the .NET SDK, PowerShell, and programs tasks invoke have their own requirements.
Updates to the pinned version are intentional changes to the manifest. The
examples below use `runx`; substitute `dotnet tool run runx --` for a local tool.

## Quick Start

From a repo that does not yet have a `runx.json`, create and run a starter task:

```powershell
runx --init
runx dotnet-version
runx --list
```

The starter task prints your .NET SDK version. Edit `runx.json` to add your
repo's build and test commands, or add scripts under `eng/tasks/` as described
below. Run `runx` to choose a task interactively, or `runx --guide` for the
built-in task authoring guide.

## Task Files

`runx` reads `runx.json`:

```json
{
  "$schema": "https://raw.githubusercontent.com/joshucar/runx/master/schemas/v1/runx.schema.json",
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
- `usage`: optional free-form text shown by `--info` and JSON discovery. Document
  accepted arguments and examples, using `\n` for line breaks in JSON. This is
  author-provided help, not an argument schema; runx does not validate it or run
  the task to obtain it. `args` contains fixed arguments, not all accepted options.
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

## Unattended tasks and task effects

Tasks inherit the caller's terminal and stdin. Agent commands, hooks, pipes, and
CI may have no terminal. A task that needs input should accept its own explicit
unattended input option or fail clearly before doing work when it cannot prompt.
runx does not translate a missing terminal into approval or invent `--yes` and
noninteractive options for the programs it launches.

For example, a task may implement `--secret-stdin`, invoked as
`runx credentials:set -- --secret-stdin`. This is a task option, not a runx flag.
Read that selected input source without echoing it; keep secrets out of command
arguments, task configuration, and logs. Ordinary task dependencies inherit the
same stdin and may consume it first, so design the task's input flow explicitly.
Resolve required inputs before making changes. Shared prompt helpers, if a repo
has them, are that repo's code rather than a runx API.

`runx --dry-run deploy` prints the dependency plan without executing its tasks.
`runx deploy -- --dry-run` executes dependencies normally and passes an option
only to `deploy`; the task must implement its meaning. A runner preview shows
launch commands, not every operation those commands perform internally.

Document significant effects, such as publishing, deleting files, changing remote
resources, or requiring credentials, in descriptions and usage text. Agents
should inspect `--info` for the source and dependency plan and review relevant
implementation and arguments. Metadata describes author intent; it is not a
safety guarantee or a permission grant.

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
  # runx usage="runx release:pack -- [-Configuration Debug]"
  # runx depends=["build"]
  # runx hide=true
  ```

  `description` shows in the picker, `usage` appears in `--info` and JSON discovery,
  `depends` works like `dependsOn`, and
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

## Git hooks

Bind Git events to existing repo tasks in the root `runx.json`:

```json
{
  "version": 1,
  "hooks": {
    "pre-commit": "check",
    "commit-msg": "git:check-message",
    "pre-push": "git:check-push"
  },
  "tasks": {
    "check": { "command": "dotnet", "args": ["format", "--verify-no-changes"] }
  }
}
```

In this example, add `eng/tasks/git/check-message.cs` and
`eng/tasks/git/check-push.cs` to implement the other two tasks. They are normal
script tasks: `check-message` receives the message-file path in `args[0]`, and
`check-push` receives the remote name and location in `args`, plus Git's ref
updates on stdin. Describe their arguments in `usage` headers.

Activate the bindings explicitly in each clone:

```powershell
runx --install-hooks
runx --check
runx --info check
runx --uninstall-hooks
```

Hook installation requires **Git 2.54 or newer**. It uses named registrations
in the shared local Git config, without changing `core.hooksPath` or existing
hook files. Other managers and hook registrations continue to run; avoid
registering the same check twice. Installation adds or updates the current
bindings and preserves other registrations, including runx events used by other
worktrees. With no bindings or no root config, installation leaves Git config
unchanged. Use `runx --install-hooks --prune` to explicitly remove runx events
absent from this worktree, or `runx --uninstall-hooks` to remove all runx events.
Both removals affect the whole clone and remove only runx-owned fields.
Conflicting registrations are reported before changes are applied. Git `enabled`
preferences, including global and worktree settings, survive installation and
removal. Uninstall works even if `runx.json` is missing or invalid.

Installation prefers a local tool manifest visible from the worktree root and
verifies the restored tool's hook protocol. Otherwise it uses the installed
runx executable you invoked. Invocations through `dotnet tool run`, `dnx`, or a
DLL need a manifest visible from the root; they fail rather than substitute a
different runx from PATH. The selected launcher is reported and stored in Git
config. Install or update the tool first; running new source code does not
upgrade an older installed tool. Git GUI clients also need access to the SDK and
any commands the tasks use. Missing tools fail visibly.

Registrations apply to **all linked worktrees of a clone**. When Git fires an
event, runx reads that worktree's root `runx.json` and repo script folders,
ignoring personal task files and their folder overrides. Bindings cannot target
personal tasks. An unbound event does nothing, even when unrelated task
definitions are invalid. Malformed JSON still fails because runx cannot determine
whether the event is bound.
The selected launcher must still be available in that worktree; a local tool
needs a visible manifest and its restored package, even on branches without hooks.
Changing a target or removing a binding takes effect immediately in that worktree.
Install after adding an event. Prune only when the removed registrations are no
longer needed by any worktree. Installation and removal affect every worktree.

Supported events: `applypatch-msg`, `pre-applypatch`, `post-applypatch`,
`pre-commit`, `pre-merge-commit`, `prepare-commit-msg`, `commit-msg`, `post-commit`,
`pre-rebase`, `post-checkout`, `post-merge`, `pre-push`, and `post-rewrite`.
Hooks receiving arguments need a command or script-file entry task; inline shell
and dependency-only tasks work for events with no arguments. Git's arguments go
only to the entry task. For `pre-push` and `post-rewrite`, only that entry task
inherits hook stdin; dependencies receive EOF. Other hook executions receive EOF
on stdin. Ordinary `runx <task>` input behavior is unchanged.

Use `runx --dry-run --hook pre-commit` to inspect the exact hook plan, or
`runx --hook commit-msg -- <message-file>` to invoke an entry with explicit hook
arguments. These actions use the current worktree's root config and do not take
`--file` or personal-task options. Hook-related operations use Git's root discovery,
including `GIT_DIR` and `GIT_WORK_TREE` overrides.

Hook validation uses the root config and its repo scripts independently of ordinary
task discovery. `--file`, personal folder overrides, and `--new` remain usable when
hook targets are absent from their task view. `--check` fails for invalid root hook
definitions, including when invoked from a nested config. Missing, retained,
conflicting, or disabled registrations are reported separately as warnings.
Checking does not execute hooks or prove that their commands will work.

`--info` and JSON discovery expose root bindings and their `hookConfig` source;
JSON also includes `hookErrors`. A task is labelled with a hook only when its
source matches the root task, not merely its name. Bindings in nested or personal
configs are inactive. Hook inspection is limited to `--check`, `--info`,
`--list --json`, and hook actions. Ordinary task runs, previews, and scaffolding
do not inspect hooks or reload the root task configuration for hook diagnostics.

runx returns the task's exit status to Git. Git decides whether that status can
block the operation: pre-commit can reject a commit, while post-commit runs after
the commit exists. Hooks are local and can be bypassed; keep required checks in
CI. Staged-file selection, formatting/restaging, and commit-message policies
belong to tasks, and are not added automatically by installation.

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

## Install From Source

Clone this repo and, from its root, run:

```powershell
dotnet run --project src/MysterNybble.Runx -- tool:install
runx --list
```

`tool:install` is the PowerShell 7 script `eng/tasks/tool/install.ps1`. It
packs the tool into `artifacts/packages` and reinstalls it as a global tool.
Run it through `dotnet run` rather than an installed `runx`, because Windows
locks the installed tool while it runs. This also supports replacing a local
`1.0.0` development build with the beta.

## Build and Test

This repo uses its own `runx.json`. Build and test tasks use Release configuration;
`ci` builds once, runs the tests, packs the tool, and smoke tests the package:

```powershell
runx build
runx test
runx ci
```

Without `runx` installed, use `dotnet build`, `dotnet test`, or
`dotnet run --project src/MysterNybble.Runx -- <task>`. Tests run on
Microsoft.Testing.Platform, which `global.json` opts `dotnet test` into.

GitHub Actions bootstraps runx from the checkout into `artifacts/bootstrap`,
then uses that build to validate the repo tasks with `--check` and run `ci` on
Windows, Linux, and macOS. The separate bootstrap output lets runx rebuild
itself without replacing its running assemblies on Windows. Successful jobs
retain their NuGet packages as workflow artifacts for 14 days. To run the same
tasks locally without installing runx (requires PowerShell 7):

```powershell
dotnet build src/MysterNybble.Runx --configuration Release --output artifacts/bootstrap
dotnet artifacts/bootstrap/MysterNybble.Runx.dll --check
dotnet artifacts/bootstrap/MysterNybble.Runx.dll ci
```

The smoke test expects exactly one runx package in `artifacts/packages`;
move any older packages out before packing. It installs into an isolated
folder under `artifacts/smoke` and leaves your global tool installation alone.

## Releasing

The `Release` workflow reuses CI to build, test, and install the package on all
three operating systems. A manual run is a dry run: it also downloads the
Linux package and checks its version and source commit, without publishing.

Publishing uses [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing).
Sign in as `joshucar` and configure the policy with:

| Setting | Value |
|---|---|
| NuGet package owner | `mysternybble` (organization) |
| GitHub repository owner | `joshucar` |
| GitHub repository | `runx` |
| Workflow file | `release.yml` |
| GitHub environment | `nuget` |
| Package glob | `MysterNybble.Runx` |
| Scope | Push new packages and package versions |

The workflow authenticates as `joshucar`; the policy selects `mysternybble`
as the package owner. No long-lived NuGet API key is required. The GitHub
`nuget` environment allows deployment from `v*` tags.

Once the policy is configured and the release commit is tested, push a tag
matching the version in the project file:

```powershell
git tag v1.0.0-beta.1
git push origin v1.0.0-beta.1
```

The tag run checks that the version and tag match, publishes the verified
Linux-built package to NuGet, then creates a GitHub release with that package
attached. Versions containing a prerelease suffix produce a GitHub prerelease.
For later releases, update the project version and use the corresponding tag.
Published versions are immutable; use a new version and tag for any changes.
