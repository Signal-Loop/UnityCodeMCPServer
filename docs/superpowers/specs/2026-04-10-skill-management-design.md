# Skill Management Design

## Goal

Streamline skill management so package install and update automatically keep bundled skill files synchronized into a configurable agent skills directory, while preserving existing custom paths and avoiding unnecessary file copies.

## Scope

This change covers package initialization, skill install destination settings, settings inspector UX, edit-mode tests, and README guidance. It does not change the skill file contents themselves or add deletion/sync-pruning behavior for files that no longer exist in the package.

## Architecture

The existing installer split remains intact:

- `PackageInit` stays the package lifecycle entry point.
- `PackageInstaller` continues to sync the packaged Python bridge assets.
- `SkillsInstaller` continues to sync bundled markdown skill files recursively and skip unchanged files via content hashing.

Package initialization becomes a two-phase sync:

1. Sync the STDIO package files into the project as it does today.
2. Resolve the effective skills target directory from settings and run the skills installer automatically.

Asset refresh remains conditional. Unity should refresh the AssetDatabase only when either the STDIO sync or the skills sync actually copied at least one file.

## Settings Model

Settings become the source of truth for the skill install destination.

Add an enum representing the selected target type:

- `GitHub` for `.github/skills/`
- `Claude` for `.claude/skills/`
- `Agents` for `.agents/skills/`
- `Custom` for a user-selected directory

Persist both:

- the selected target type
- the backing `SkillsTargetPath`

The backing path stores the resolved directory path currently in use. For preset targets this should resolve to the workspace-relative absolute path. For custom targets it stores the user-selected absolute path.

Settings also need an initialization/migration helper:

- Fresh install: when no target selection and no path have been initialized, default to `Agents` and set `SkillsTargetPath` to the resolved `.agents/skills/` absolute path.
- Existing installs with a non-empty absolute `SkillsTargetPath` and no explicit selection should migrate to `Custom` and keep the saved path unchanged.
- Existing installs already pointing at a known preset path may be mapped to the matching preset when possible, but preserving custom paths takes precedence.

## Path Resolution Rules

Provide a single settings helper that returns the effective install directory.

- `GitHub` resolves to `Path.GetFullPath(".github/skills/")`
- `Claude` resolves to `Path.GetFullPath(".claude/skills/")`
- `Agents` resolves to `Path.GetFullPath(".agents/skills/")`
- `Custom` resolves to the stored `SkillsTargetPath`

This helper should normalize to forward slashes where existing installer code expects stable cross-platform comparisons.

## Inspector UX

The manual install action is removed.

The settings inspector should show:

- a dropdown for the selected target option
- a label with the currently selected resolved target directory
- a folder-picker UI only when `Custom` is selected

Expected behavior:

- Choosing `GitHub`, `Claude`, or `Agents` updates the stored target path to the corresponding resolved directory immediately.
- Choosing `Custom` preserves the existing custom path if present; otherwise it starts from the current resolved path or project root.
- The folder picker appears only in `Custom` mode and updates the stored path when the user selects a folder.
- Any changes mark the settings asset dirty and are saved by the existing inspector flow.

The info text should describe automatic install/update behavior rather than instructing the user to press a button.

## Installer Behavior

`SkillsInstaller` keeps the existing hash-based copy semantics: it only copies new or changed markdown files and skips unchanged files. No file deletion is added.

`PackageInstaller` may stay focused on STDIO files, but its return value should remain usable by `PackageInit` so `PackageInit` can combine both install phases into a single `anyChanges` decision.

`PackageInit` should:

1. Resolve package root.
2. Run STDIO installation.
3. Resolve skills source path.
4. Ensure settings are initialized/migrated.
5. Resolve the effective skills target path.
6. Run skills installation when the source path exists and the target path is valid.
7. Refresh the AssetDatabase if either installation copied files.

If the skills source folder cannot be resolved, log a warning and continue without failing the package bootstrap.

## Testing

Update or add edit-mode tests to cover:

- default first-run target selection is `Agents`
- legacy absolute `SkillsTargetPath` migrates to `Custom`
- preset target resolution returns the expected absolute paths
- custom selection preserves the chosen path
- `PackageInit` or extracted install orchestration refreshes only when either installer changes files
- `SkillsInstaller` still copies only changed/new files and skips unchanged ones

Tests should remain focused on behavior, not UI rendering details. Inspector-facing logic that can be expressed as pure helpers should be tested through those helpers.

## Documentation

Update `README.md` so the setup flow explains that bundled skills are installed automatically during package install/update, `.agents/skills/` is the default first-run target, users can change the target from settings, and only new or changed skill files are copied.

## Non-Goals

- No deletion of skill files from the target directory.
- No changes to bundled skill markdown contents.
- No background watcher or continuous sync outside package install/update.