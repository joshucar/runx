# Completed follow-up work

- [x] Improve agent guidance for tasks that run without a terminal.
  - Explain in the built-in guide that tasks inherit the caller's terminal and
    stdin, and agent execution may have no terminal available.
  - Explain that task authors must handle unavailable prompts, fail clearly when
    required input is missing, and provide task-specific unattended input options.
  - Show how task options pass through `runx <task> -- <options>`.
  - Explain secret input without echoing values or placing them in arguments, and
    note that dependencies can consume inherited stdin before the requested task.
  - Keep the installed agent skill directing agents to this guidance.
  - Scope: documentation and agent guidance; no new runx noninteractive flag,
    secret channel, native prompt, or shared-helper API. Fixes to individual
    prompts belong in the consuming repo's scripts or commands.

- [x] Improve task discovery guidance and add optional usage text.
  - Help agents discover the existing `--list --json`, `--info <task>`,
    `--info <task> --json`, and `--guide` capabilities.
  - Add an optional free-form `usage` field to JSON tasks and script headers.
  - Display usage in `--info` and include it in JSON discovery, while keeping
    picker descriptions short.
  - Explain that usage is author-provided documentation; configured `args` are
    not a declaration of every argument the task accepts.
  - Scope: no argument schema or validation, reserved `help` command, automatic
    argument inference, or execution of task help during discovery.

- [x] Document optional version pinning with a local .NET tool manifest.
  - Explain global installation for use across repos and local installation with
    a committed `.config/dotnet-tools.json` for repos that want a fixed version.
  - Document `dotnet tool restore` and explicit local invocation with
    `dotnet tool run runx -- <task>`, including use in hooks and automation.
  - Explain that a manifest does not redirect bare `runx` to the local version,
    and pins the runner package rather than the SDK or programs tasks invoke.
  - Teach agents to recognize and respect an existing tool manifest.
  - Scope: installation documentation and agent guidance; keep manifests
    optional, preserve global installation, and add no automatic version
    redirection. Keep runx's own CI testing the checked-out source.

- [x] Clarify runner dry runs and task-specific execution options.
  - Contrast `runx --dry-run <task>`, which displays the plan without executing
    tasks or dependencies, with `runx <task> -- --dry-run`, which executes the
    task and relies on that task to interpret the option.
  - Explain that passthrough arguments reach only the requested task, so its
    dependencies still execute normally in the second example.
  - Explain that the runner preview shows commands to launch, not every
    operation those commands would perform internally.
  - Direct agents and authors to task usage metadata for task-specific options.
  - Scope: documentation and agent guidance; preserve existing dry-run
    semantics and add no universal task flags or execution-mode environment
    variables. Confirmation and unattended behavior remain task-owned.

- [x] Improve guidance for describing and reviewing task effects.
  - Encourage authors to document significant effects in descriptions and usage
    text, including publishing, changing remote resources, deleting files, and
    requiring credentials.
  - Teach agents to use `--info` to locate the implementation and inspect the
    dependency plan, accounting for effects that vary with arguments and
    dependencies.
  - Explain that task metadata is author-provided documentation, not a safety
    guarantee or permission grant. Permission decisions remain with the agent's
    execution environment.
  - Scope: documentation and agent guidance; defer dedicated safety/effects
    fields and automatic approval integration until a concrete consumer and
    well-defined requirements justify them.

- [x] Add first-party Git hook bindings to ordinary repo tasks.
  - Map supported client-side Git events to task names in the root `runx.json`.
  - Explicitly install, prune, and uninstall runx-owned registrations with
    Git 2.54+ named hooks, preserving other hook managers and `core.hooksPath`.
  - Prefer the repo's local tool manifest, verify the selected launcher, and
    fail clearly when it needs restoring or upgrading.
  - Expose bindings in discovery and inspect installation with `--check`.
  - Forward Git arguments only to the entry task; reserve hook stdin for that
    entry on events with stdin, leaving dependencies at EOF.
  - Resolve the current worktree's root config without personal tasks. Shared
    registrations affect every worktree; each needs its launcher available.
  - Keep file selection, restaging, commit policy, and mandatory CI checks in
    consuming repos. Hooks remain ordinary tasks with ordinary dependencies.
  - Cover installation, coexistence, input, failures, worktrees, uninstall, and
    manifest selection in the package smoke test.

- [x] Correct hook loading, installation, and discovery after review.
  - Use one Git-resolved root and a separate hook configuration snapshot.
  - Keep ordinary tasks and scaffolding usable while hook targets are missing.
  - Limit hook inspection to reports and hook actions, avoiding extra Git calls,
    root task scans, and hook warnings during ordinary runs and scaffolding.
  - Make root hook errors fail checks and report source-aware bindings.
  - Preserve other worktrees' registrations unless pruning is explicitly requested.
  - Preserve enablement preferences across config scopes and batch status reads.
  - Require a stable launcher and a versioned protocol instead of probing help text.
  - Share captured process handling and safe executable resolution; support
    uninstall without the newer rev-parse path-format option.
