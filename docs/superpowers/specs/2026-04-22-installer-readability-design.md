# Installer Readability Design

**Goal:** Simplify installer orchestration so `PackageInit` only coordinates installer execution, while `PackageInstaller` and `SkillsInstaller` each own their own install decisions and implementation details.

## Problem

The current install flow is difficult to read because orchestration and policy are split across multiple classes:

- `PackageInit` decides STDIO-specific behavior instead of only coordinating installers.
- `PackageInstaller` contains a generic `InstallContent` orchestration helper even though it should only manage package file copying.
- `PackageInit` still knows too much about how to interpret skill installation results.

That structure makes the main startup path harder to follow and obscures the actual install policy.

## Design

### PackageInit as the coordinator

`PackageInit` will coordinate the install workflow in one readable path:

1. Resolve package root.
2. Construct the installers it needs.
3. Run package installation.
4. Run skills installation.
5. Return whether anything changed.

This keeps startup orchestration in one place and removes callback-based control flow without pushing installer-specific policy into `PackageInit`.

### PackageInstaller as a focused file copier

`PackageInstaller` will own the full STDIO installation decision and file-copy workflow.

- Remove the generic `InstallContent` helper.
- Move STDIO-specific source and target resolution behind the package installer boundary.
- Move the skip decision for identical source and target paths behind the package installer boundary.
- Keep path normalization, hash comparison, and file copy decisions inside the class.
- Keep the public surface centered on a single `Install` operation.

### SkillsInstaller as a focused skills installer

`SkillsInstaller` will remain responsible for installing and relocating skill files.

- Keep recursive file copy and relocation logic internal.
- Keep the result object owned by `SkillsInstaller`.
- Reduce the need for callers to understand installer internals beyond success and change state.

`PackageInit` may still inspect returned results, but only to combine change state, not to reproduce installer policy.

## Testing

Tests should follow the new boundaries:

- Remove or replace callback-oriented coordination tests that exist only because of `InstallContent` or `RunInstallSteps`.
- Add focused tests around the `PackageInit` install workflow behavior.
- Keep `PackageInstaller` tests focused on package file copying.
- Keep `SkillsInstaller` tests focused on skills installation and relocation behavior.

Minimum coordinator coverage:

- If package installation determines STDIO copying should be skipped, skills install still runs.
- Combined result is `true` when either installer reports changes.
- Combined result is `false` when neither installer reports changes.

## Non-Goals

- No change to the actual files copied for STDIO installation.
- No change to skills relocation behavior.
- No broader installer architecture split beyond improving readability and responsibility boundaries.
