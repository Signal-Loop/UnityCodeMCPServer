# AGENTS.md

Agent guidance for contributors working in this repository.

## Scope

- This file is for work inside this repo, not for downstream Unity projects using the package.
- Follow the closest AGENTS.md if a nested file is added later.
- Explicit user instructions override this file.

## Project Shape

- Unity package and test project live under `Assets/Plugins/UnityCodeMcpServer`.
- Unity tests live under `Assets/Tests` and related test assemblies in the repo root.
- Python STDIO bridge code lives under `Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~/src`.

## Editing Rules

- Make the smallest change that satisfies the request.
- Implement only the changes needed for the task, and finish the requested work before stopping.
- Do not change unrelated code, scenes, assets, or settings.
- Add tests for logic changes unless the change is trivial documentation or rename-only work.
- Keep existing style and naming conventions. In Unity C# code, use snake_case for private fields.
- Prefer latest supported Unity APIs already used by the repo.

## Unity Workflow

- Before relying on Unity editor tooling, check for compilation problems in Unity logs and fix them first.
- If you edit C# source, validate with relevant Unity tests and re-check logs for compile errors.
- Use `run_unity_tests` for Unity test verification and `read_unity_console_logs` to inspect compile/runtime issues.
- When using `execute_csharp_script_in_unity_editor`, read the bundled `executing-csharp-scripts-in-unity-editor` skill first and treat script execution, console-log checks, and test runs as one workflow.
- Do not use editor-executed C# scripts to edit source files or plain text files; use normal file editing tools for that.

## Verification

- For C# or Python behavior changes, run the narrowest relevant test first, then broaden only if needed.
- For Unity test runs, fail fast on compile issues before assuming a test failure is about logic.
- Do not claim success without fresh verification evidence.

## Documentation

- Update `README.md`, `README_STDIO.md`, release notes, or skill docs when behavior or workflow changes make existing guidance inaccurate.
- Keep AGENTS.md concise. Put detailed end-user explanations in README or skill files instead.
