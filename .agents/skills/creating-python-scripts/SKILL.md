---
name: creating-python-scripts
description: Use when creating or editing a Python script, utility, CLI helper, one-off automation, or small standalone Python program that should stay as a script instead of becoming a multi-file project.
---

# Creating Single-File UV Python Scripts

## Overview

Use this skill to keep Python script work script-shaped.

The default outcome is one `.py` file that can be run with `uv`. If third-party packages are needed, declare them inside the script with inline metadata instead of creating separate dependency files.

## When To Use

Use this skill when the user asks for:

- a Python script
- a small Python utility
- a one-off automation
- a standalone CLI helper
- a quick prototype that does not need to become a package

Do not use this skill when the user explicitly asks for:

- a multi-file Python project
- a package or library
- a framework app
- a repository scaffold with `pyproject.toml`

If the user asks for a script but the requested architecture clearly requires multiple files, say that the single-file constraint conflicts with the requested design and ask whether to keep the script constraint or expand to a project.

## Core Rules

1. Produce a single `.py` file.
2. Do not create helper modules, packages, `requirements.txt`, or `pyproject.toml` for script tasks.
3. Use `uv` commands when showing how to run the script.
4. If the script needs third-party packages, use inline metadata in the script.
5. If the script only uses the standard library, omit the dependency metadata block.
6. If you are asked to execute, test, or verify the script, do it with `uv run` rather than raw `python`.

## Required Workflow

### 1. Confirm the work is really script-sized

Prefer a single-file script when the task is automation, data processing, API calls, file transformation, or a small CLI.

If the request starts drifting toward a package, service, or app structure, stop and surface the tradeoff instead of quietly creating extra files.

### 2. Keep everything in one file

Put imports, configuration constants, helper functions, CLI parsing, and the main entry point in the same file.

Avoid splitting code into separate modules unless the user explicitly relaxes the single-file requirement.

### 3. Declare third-party dependencies inline

Use a PEP 723 script metadata block near the top of the file when external packages are required.

Template:

```python
# /// script
# dependencies = [
#   "requests>=2.32",
# ]
# ///
```

Keep the block minimal. Only list packages the script actually imports.

### 4. Use `uv` for execution examples

Default run form:

```bash
uv run script.py
```

If the script takes arguments:

```bash
uv run script.py --input data.json
```

If a package must be added while editing an existing script, prefer a script-scoped command or update the inline metadata block directly:

```bash
uv add --script script.py requests
```

Do not tell the user to run `python script.py` for these script tasks unless they explicitly ask for a non-`uv` workflow.

If you are able to verify the script in the workspace, run it with `uv run script.py ...` so the inline metadata is exercised instead of bypassed.

## Output Pattern

When you create the script, aim for this shape:

```python
# /// script
# dependencies = [
#   "requests>=2.32",
# ]
# ///

from __future__ import annotations

import argparse

import requests


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("url")
    args = parser.parse_args()

    response = requests.get(args.url, timeout=30)
    response.raise_for_status()
    print(response.json())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

This is a pattern, not a rigid template. Keep the result single-file and executable with `uv`.

## Common Mistakes

### Creating project scaffolding for a script request

Wrong:

- `pyproject.toml`
- `requirements.txt`
- `src/` package layout
- extra helper modules

Correct:

- one script file
- inline dependency metadata only when needed

### Forgetting dependency metadata

If the script imports a third-party package, the metadata block should exist.

### Adding metadata for standard-library-only code

Do not add an empty or unnecessary metadata block.

### Using raw `python` commands in instructions

Prefer `uv run script.py` so the script can resolve its inline dependencies consistently.

### Verifying with `python` instead of `uv`

If the script is supposed to be run or checked, use `uv run` so the metadata path is actually validated.

## Bottom Line

For Python script tasks, keep the deliverable to one file, keep dependencies inside that file when needed, and show `uv` as the execution path.