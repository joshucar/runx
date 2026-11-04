# runx guide

runx runs a repo's tasks. Tasks come from three places, and `runx --list`
shows them all:

1. Scripts in the repo's task folder, `eng/tasks/` by default. This is the
   preferred way to add a task.
2. The repo's `runx.json`, for tasks that call a command directly, wrap a
   script with fixed arguments, or only run other tasks.
3. The user's personal task files, kept outside the repo, for repos the user
   cannot or does not want to commit to.

## Find and invoke tasks

Check for an existing .NET tool manifest before choosing an invocation. If it
contains runx, use `dotnet tool restore` and `dotnet tool run runx -- --guide` (and
replace `runx` with `dotnet tool run runx --` in the examples below). A manifest does
not redirect a bare global `runx` command. Keep the outer `--` after `runx` in
the local invocation so the .NET CLI passes through runx flags and task argument
delimiters unchanged. Repos can optionally pin runx with
`dotnet new tool-manifest --output .config` when no manifest exists and
`dotnet tool install --local MysterNybble.Runx --version <version>`. Commit the
manifest. Global installation remains supported. The manifest pins runx, not the
SDK or programs tasks launch; update it deliberately when upgrading.

Start discovery with `runx --list --json`, then `runx --info <name>` or
`runx --info <name> --json`. These report task definitions without executing task
code. `usage`, when supplied, documents accepted options and examples. `args`
contains configured arguments and is not a list of all options the task accepts.
Pass task-specific arguments after `--`; they reach the requested task only.

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
      // runx usage="runx db:seed -- [--count 5]"
      // runx depends=["build"]
      // runx hide=true

  `description` shows in the picker and `--list`. Optional `usage` is free-form
  quoted text displayed by `--info` and JSON discovery; use `\n` for additional
  lines. It does not constrain the task's arguments or get validated against its
  implementation. `depends` names tasks to run
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
`description`, `usage`, `cwd` (relative to runx.json), `env`, `dependsOn`, and `hide`.
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

## Unattended input and effects

Tasks inherit the caller's terminal and stdin. Agents, pipes, CI, and Git hooks
may not provide a terminal. Task authors should detect unavailable prompts and
fail with a clear stderr message and nonzero status before performing writes.
Accept supplied input first; only prompt when input is still needed and a
terminal is available. For a C# console prompt, check `Console.IsInputRedirected`
before using console key input; in a shell, check the input source required by
that prompt (for stdin, `test -t 0`). A missing terminal never means approval.

A task may support an explicit input option such as `--secret-stdin`, passed as
`runx credentials:set -- --secret-stdin`. Read the selected source without echo;
keep secret values out of arguments, config, and logs. In ordinary task runs,
dependencies inherit the same stdin and can consume it first. Account for that
when designing the script. Prompt helpers belong to the consuming repo, if it
chooses to have them. runx has no universal `--secret-stdin`, `--yes`, or
noninteractive task option.

`runx --dry-run deploy` skips every task in the plan. In contrast,
`runx deploy -- --dry-run` runs dependencies normally and passes `--dry-run` only
to `deploy`; the task must implement the option. The runner's preview shows
launch commands, not the operations those commands perform internally.

Use descriptions and usage text to disclose significant effects: publishing,
deleting files, changing remote resources, and requiring credentials. Inspect
`--info` for the source and dependency plan, then review relevant code and
arguments when assessing effects. Author-provided metadata is not a safety
guarantee or permission grant; execution permissions belong to the host agent.

## Bind Git hooks

Add an optional `hooks` object to the root `runx.json`, mapping Git events to
existing repo tasks, for example `"hooks": { "pre-commit": "check" }`.
`runx --install-hooks` explicitly activates those bindings; `--uninstall-hooks`
removes runx's registrations, even when the task config is broken. Install or
update the tool before installing hooks. Use the manifest invocation when the
repo pins runx. Installation verifies the selected tool; restore errors never
cause a silent fallback to a global version. With `dotnet tool run`, `dnx`, or a
DLL invocation, a manifest must be visible from the worktree root. Otherwise,
invoke an installed runx executable directly; installation does not substitute
another runx from PATH.

Hook installation needs Git 2.54+ and uses named registrations. It leaves
`core.hooksPath`, other registrations, and hook files alone. Conflicting names
cause an error without applying changes. Avoid wiring the same check through two
managers. Explicit Git `enabled` preferences, including global and worktree
settings, survive removal and reinstallation.
Registrations are local to a clone and shared by its linked worktrees.
Each event reads the current worktree's root config and repo script folders,
without personal files or personal folder overrides. A missing binding does
nothing even when unrelated task definitions are invalid; malformed JSON still
fails because binding presence cannot be determined. Installing adds or updates
the current bindings and preserves other worktrees' registrations. No bindings
or no root config means no installation changes. Install after adding events;
changing a target or removing a binding takes effect immediately in that worktree.
Use `--install-hooks --prune` explicitly to remove registrations absent from this
worktree across the clone, or `--uninstall-hooks` to remove all runx registrations.
The selected launcher must remain available in every worktree. Local-tool
launchers need a visible manifest and restored package even without a binding.

Supported events: `applypatch-msg`, `pre-applypatch`, `post-applypatch`,
`pre-commit`, `pre-merge-commit`, `prepare-commit-msg`, `commit-msg`, `post-commit`,
`pre-rebase`, `post-checkout`, `post-merge`, `pre-push`, and `post-rewrite`.

Git arguments go only to the entry task. Events with arguments need a command
or script-file entry point, not an inline shell or dependency-only task. For
example, a `commit-msg` entry takes the message-file path; a `pre-push` entry takes
the remote name and location and reads ref updates from stdin. Only the entry
receives stdin for `pre-push` and `post-rewrite`; dependencies receive EOF. Other
hook runs receive EOF. Ordinary task invocation keeps its existing input behavior.

Inspect with `runx --info <task>`, JSON discovery, and `runx --check`. Check reports
registration problems without executing task code; it does not certify that the
task or its tools will succeed. Invalid root hook definitions fail `--check`, even
from a nested config. Ordinary commands, including `--file` and `--new`, can still
load and create tasks while hook targets are missing. Hook inspection runs only
for `--check`, `--info`, `--list --json`, and hook actions; ordinary task runs and
scaffolding do not perform the extra Git lookup or root task scan. Hook validation
uses repo script folders, independently of personal folder overrides. Discovery reports
root bindings with their `hookConfig` source and JSON `hookErrors`; same-named
tasks from other sources do not inherit hook labels. Git's root discovery,
including its environment overrides, is shared by all hook-related operations.
Preview with `runx --dry-run --hook pre-commit`;
for argument-bearing hooks, supply their Git arguments after `--`, for example
`runx --dry-run --hook commit-msg -- <message-file>`. Hook actions always use the
worktree root, so `--file`, parent search, and personal options do not apply.

Return nonzero on failure; Git determines whether that hook can block its
operation. A post-commit failure cannot undo the commit. Keep mandatory checks in
CI because local hooks can be bypassed. File filtering, restaging, and commit
message policy remain task behavior. GUI Git clients need the SDK and task tools
available in their environment too.
