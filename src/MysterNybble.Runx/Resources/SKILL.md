---
name: runx
description: Add, change, find, or run tasks with runx, the repo task runner (eng/tasks scripts, runx.json, personal tasks). Use when asked to create a project script or task, to wire one task to run before another, or to find and run a repo's tasks.
---

# runx

runx runs a repo's tasks: scripts in `eng/tasks/`, entries in `runx.json`, and the user's personal tasks.

1. Check for an existing .NET tool manifest containing runx. If present, restore it with `dotnet tool restore` and use `dotnet tool run runx --` in place of bare `runx` below; do not silently switch to a global version. Run `runx --guide` first, from inside the repo you are working on, and follow it. It matches the installed version of runx and ends with details about the repo in the current directory, such as where new scripts go. Run every runx command from inside that repo.
2. To add a script task, run `runx --new <name> --type ps1|cs|sh|cmd --description "..."` (add `--depends <task>` if it needs another task first), then write the script's body where the TODO is. Add optional `usage` header text for accepted arguments and examples.
3. Confirm with `runx --check` and `runx --info <name>`, then run it with `runx <name>`.

Use `runx --list --json` to see existing tasks. Pass arguments to a task after `--`: `runx <name> -- <args>`.

Consult the guide for unattended input, secrets, dry-run semantics, and task effects.
Agent execution may have no terminal; prompt handling belongs in tasks. Metadata
is author-provided documentation, not a safety guarantee or permission grant.

Git hooks can map events to existing repo tasks in the root `runx.json`.
Use `--install-hooks` only when enabling hooks is requested; `--check` reports
registration problems and `--info` shows bindings. Follow the guide's hook input,
worktree, installation, and version requirements.
Installation preserves other worktrees' registrations. Use `--install-hooks --prune`
only when removal of unbound registrations across the clone is intended.
