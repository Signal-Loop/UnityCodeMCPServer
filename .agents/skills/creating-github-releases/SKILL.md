---
name: creating-github-releases
description: Create professional, user-focused release notes from Git history and publish verified GitHub releases with git and the gh CLI. Use when Codex needs to analyze a release branch or commit range, write release notes, merge a release branch into its target branch, derive a v{version} tag from a package manifest, create a GitHub release, or verify an existing release workflow.
---

# Creating GitHub Releases

Create accurate release notes first, then publish only when the user explicitly asks for a release. Treat drafting notes and publishing a release as separate authorization boundaries.

## Establish the Release Inputs

Determine these values from the request and repository before changing anything:

- release branch or source ref
- target branch, usually the repository default branch
- exact release commit range
- version source, such as `package.json`, `pyproject.toml`, or a project manifest
- tag format, defaulting to `v{version}` when the repository has no other convention
- release-notes path and naming convention
- GitHub repository resolved by `gh repo view`

Inspect the closest `AGENTS.md`, the working tree, remotes, branches, recent tags, previous release notes, and package metadata. Prefer existing repository conventions over examples in this skill. Ask the user only when a material input remains ambiguous.

Never discard or overwrite unrelated working-tree changes. Do not merge, push, tag, or publish while the tree is unexpectedly dirty.

## Analyze the Release

Fetch remote state before relying on branch or tag information:

```text
git fetch origin --prune --tags
git status --short
git branch --show-current
git remote -v
git tag --sort=-v:refname
```

Choose a comparison range that contains exactly this release:

- Before a release branch is merged, normally compare `<target>...<release-branch>` and confirm the merge base.
- After a release is merged, normally compare `<previous-release-tag>..<target>`.
- For maintenance branches or unusual histories, inspect the graph and select the range explicitly instead of assuming the newest tag is the correct base.

Use both summaries and targeted diffs:

```text
git merge-base <target> <release-branch>
git rev-list --left-right --count <target>...<release-branch>
git log --oneline --decorate <range>
git diff --stat <range>
git diff --name-status <range>
```

Then inspect the actual changes behind important commits. Commit subjects and file counts are clues, not sufficient evidence. Pay special attention to:

- public APIs and behavior
- installation, dependencies, and compatibility
- configuration and defaults
- workflows visible to users or integrators
- deprecations, removals, and migrations
- reliability, performance, and security
- documentation that reveals intended usage
- tests that clarify supported scenarios

Do not claim a benefit, migration path, or test result unless the repository evidence supports it.

## Write the Release Notes

Write from the user's perspective. Lead with outcomes such as simpler setup, more reliable behavior, or new capabilities; use implementation details only to explain why an outcome matters.

Follow prior release-note structure when one exists. Otherwise use this compact structure:

```markdown
# <Product> <Release> Release Changes

## TL;DR

- <Two or three highest-value outcomes>

## ✨ What's New for You?

<One short paragraph defining the release theme.>

- **Benefit-led heading:** Explain the user-visible result and relevant scope.

## ⚠️ Breaking Changes & Migration

<Include only when users need to act. State who is affected and exactly what to change.>

## 🚀 Key Improvements

### <User-relevant category>

- **Concise heading:** Describe the improvement and why it matters.
```

Apply these writing rules:

- Keep the TL;DR to two or three distinct points.
- Group related changes by user concern, not by commit or file.
- Use active voice, parallel bullets, and concrete language.
- Expand acronyms or internal names only when users need them.
- Avoid raw commit lists, duplicate points, hype, and unsupported superlatives.
- Omit empty sections. Never invent breaking changes to fill the template.
- Make migration instructions precise enough to execute.
- Mention regression coverage as evidence, not as the main feature.
- Keep emoji usage restrained and consistent with previous notes.

Save the notes at the requested path. If none is given, infer the location from previous releases, such as `Dev/Releases/<release>_Release_Changes.md`. Re-read the final file against the diff to catch omissions, duplication, and implementation-centric wording.

If the user requested only release notes, stop after writing and reviewing them. Do not merge or publish.

## Preflight a Published Release

Proceed only when the user explicitly asked to create or publish the release.

1. Confirm the notes are final and included in the release branch when repository policy expects them to be versioned.
2. Read the version from the authoritative manifest; do not infer it from the branch name.
3. Construct the requested tag, typically `v<version>`, and validate that it is non-empty and plausible.
4. Confirm `gh auth status` succeeds and resolve the canonical repository with `gh repo view --json nameWithOwner,url`.
5. Check both the remote tag and GitHub release before creating anything:

```text
git ls-remote --tags origin refs/tags/<tag>
gh release view <tag> --json tagName,url,isDraft,isPrerelease
```

Stop if the tag or release already exists unless the user explicitly asked to repair or replace it. Never silently move an existing release tag.

## Merge the Release Branch into `main`, Then Tag

For a published release, the release branch must be merged into `main` before a tag or GitHub release is created. Do not tag the release branch, and do not create a release that targets it. If the repository uses a different default branch, substitute that branch only after confirming the repository convention.

Synchronize `main` and preserve the repository's merge convention. For repositories that retain release merge commits:

```text
git switch main
git pull --ff-only origin main
git merge --no-ff <release-branch> -m "Merge branch '<release-branch>'"
git push origin main
```

If the repository normally fast-forwards or requires pull requests, follow that policy instead. After pushing, verify that local and remote `main` refs identify the intended commit. Do not tag or publish a release from an unpushed local commit.

Create the tag at the pushed `main` commit, then push it:

```text
git rev-parse HEAD
git rev-parse origin/main
git tag -a <tag> -m "<Product> <Release>"
git push origin <tag>
```

Confirm the local tag, remote tag, and `origin/main` all resolve to the intended release commit before publishing.

## Create and Verify the GitHub Release from `main`

Create the GitHub release from the already-pushed tag created at `main`:

```text
gh release create <tag> \
  --title "<tag>" \
  --notes-file <release-notes-path>
```

Use the exact notes file rather than copying its contents into the command. Set the release title to the exact tag name. Do not pass `--target` here: the existing remote tag is the release target. This guarantees the release is created from the commit already merged and pushed to `main`.

If creation fails, inspect whether GitHub created either a tag or a release before retrying. Retry only after identifying the cause; do not create alternate tags to work around an error.

Verify all published state:

```text
gh release view <tag> --json name,tagName,targetCommitish,isDraft,isPrerelease,url
git ls-remote --tags origin refs/tags/<tag>
git rev-parse origin/main
git status --short
```

Confirm that:

- the release is published unless draft or prerelease status was requested
- the tag has the exact manifest-derived name
- the tag resolves to the intended `main` commit
- the release body came from the final notes file
- the working tree remains clean

Report the merge commit, tag, release URL, publication state, and verification result. If any check fails, describe the actual state precisely instead of claiming completion.
