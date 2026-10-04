---
name: runx
description: Add, change, find, or run tasks with runx, the repo task runner (eng/tasks scripts, runx.json, personal tasks). Use when asked to create a project script or task, to wire one task to run before another, or to find and run a repo's tasks.
---

# runx

runx runs a repo's tasks: scripts in `eng/tasks/`, entries in `runx.json`, and the user's personal tasks.

1. Run `runx --guide` first, from inside the repo you are working on, and follow it. It matches the installed version of runx and ends with details about the repo in the current directory, such as where new scripts go. Run every runx command from inside that repo.
2. To add a script task, run `runx --new <name> --type ps1|cs|sh|cmd --description "..."` (add `--depends <task>` if it needs another task first), then write the script's body where the TODO is.
3. Confirm with `runx --check` and `runx --info <name>`, then run it with `runx <name>`.

Use `runx --list --json` to see existing tasks. Pass arguments to a task after `--`: `runx <name> -- <args>`.
