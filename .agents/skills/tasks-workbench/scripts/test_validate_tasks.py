# /// script
# requires-python = ">=3.12"
# ///

"""Exercise every validation rule with isolated temporary task files.

Run with: uv run .agents/skills/kanban-task-workflow/scripts/test_validate_tasks.py
"""

from __future__ import annotations

import subprocess
import tempfile
from pathlib import Path


SCRIPT_DIRECTORY = Path(__file__).parent
VALIDATOR = SCRIPT_DIRECTORY / "validate_tasks.py"
STATUSES = ("Backlog", "Planning", "InProgress", "Completed")


def task(
    *,
    h1: bool = True,
    status: str | None = "Backlog",
    order: str | None = "100",
    goal: str | None = None,
) -> str:
    lines = ["# Example task", ""] if h1 else []
    if status is not None:
        lines.append(f"- status: {status}")
    if order is not None:
        lines.append(f"- order: {order}")
    if goal is not None:
        lines.append(f"- goal: {goal}")
    return "\n".join(lines) + "\n"


def run(root: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(["uv", "run", str(VALIDATOR), str(root)], capture_output=True, text=True, check=False)


def run_default(workdir: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(["uv", "run", str(VALIDATOR)], cwd=workdir, capture_output=True, text=True, check=False)


def expect(name: str, result: subprocess.CompletedProcess[str], code: int, message: str) -> None:
    output = result.stdout + result.stderr
    if result.returncode != code or message not in output:
        raise AssertionError(f"{name}: expected exit {code} and {message!r}; got exit {result.returncode}:\n{output}")
    print(f"PASS {name}")


def write(root: Path, content: str, name: str = "task.md") -> None:
    root.mkdir()
    (root / name).write_text(content, encoding="utf-8")


def main() -> int:
    with tempfile.TemporaryDirectory(prefix="kanban-validator-") as temporary_directory:
        temporary_root = Path(temporary_directory)

        expect("missing task root", run(temporary_root / "missing"), 2, "Kanban task root does not exist")

        empty = temporary_root / "empty"
        empty.mkdir()
        expect("empty task root", run(empty), 0, "No task files found")

        valid = temporary_root / "valid"
        valid.mkdir()
        for index, status in enumerate(STATUSES, start=1):
            (valid / f"{index:02d}-{status}.md").write_text(task(status=status, order=str(index * 100), goal="Auditable outcome."), encoding="utf-8")
        (valid / "README.md").write_text(task(status="Backlog", order="500", goal="README is a task record."), encoding="utf-8")
        (valid / "index.md").write_text(task(status="Planning", order="600", goal="Index is a task record."), encoding="utf-8")
        nested = valid / "nested"
        nested.mkdir()
        (nested / "ignored.md").write_text("not a task\n", encoding="utf-8")
        (valid / "ignored.txt").write_text("not a task\n", encoding="utf-8")
        expect("valid statuses and arbitrary Markdown names", run(valid), 0, "6 task file(s) checked")

        default_root = temporary_root / "default-root"
        default_tasks = default_root / ".workbench" / "tasks"
        default_tasks.mkdir(parents=True)
        (default_tasks / "task.md").write_text(task(status="Backlog", order="100", goal="Auditable outcome."), encoding="utf-8")
        expect("repository default task root", run_default(default_root), 0, "1 task file(s) checked")

        loose_cases = (
            ("property-free note", "An idea without task properties.\n"),
            ("missing status", task(status=None)),
            ("partial Backlog", task(status="Backlog", order=None)),
            ("invalid Backlog order", task(status="Backlog", order="first")),
        )
        for index, (name, content) in enumerate(loose_cases, start=1):
            root = temporary_root / f"loose-{index}"
            write(root, content)
            expect(name, run(root), 0, "1 task file(s) checked")

        invalid_cases = (
            ("missing H1 title", task(h1=False, status="Planning"), "missing required H1 task title"),
            ("empty status", task(status=""), "invalid `status: `"),
            ("invalid status", task(status="Blocked"), "invalid `status: Blocked`"),
            ("missing order", task(status="Planning", order=None), "missing required positive-integer `order`"),
            ("zero order", task(status="Planning", order="0"), "invalid `order: 0`"),
            ("negative order", task(status="Planning", order="-1"), "invalid `order: -1`"),
            ("non-numeric order", task(status="Planning", order="first"), "invalid `order: first`"),
            ("legacy status", task(status="Ready", goal="Auditable outcome."), "invalid `status: Ready`"),
        )
        for index, (name, content, message) in enumerate(invalid_cases, start=1):
            root = temporary_root / f"invalid-{index}"
            write(root, content)
            expect(name, run(root), 1, message)

        for status in STATUSES[1:]:
            root = temporary_root / f"missing-goal-{status}"
            write(root, task(status=status))
            expect(f"goal required for {status}", run(root), 1, f"`goal` is required when status is `{status}`")

    print("All validator feature checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
