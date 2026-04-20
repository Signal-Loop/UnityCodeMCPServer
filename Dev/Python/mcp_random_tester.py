# /// script
# dependencies = [
#   "mcp>=1.26.0",
# ]
# ///

from __future__ import annotations

import argparse
import asyncio
import json
import os
import random
import re
import sys
import time
import traceback
import unittest
from contextlib import AbstractAsyncContextManager
from dataclasses import dataclass
from datetime import timedelta
from pathlib import Path
from typing import Any, cast

from mcp import ClientSession, types
from mcp.client.stdio import StdioServerParameters, stdio_client

SCRIPT_DIR = Path(__file__).resolve().parent
WORKSPACE_ROOT = SCRIPT_DIR.parent.parent
DEFAULT_BRIDGE_DIR = (
    WORKSPACE_ROOT / "Assets" / "Plugins" / "UnityCodeMcpServer" / "Editor" / "STDIO~"
)
DEFAULT_DOMAIN_RELOAD_FILE = (
    WORKSPACE_ROOT / "Assets" / "Tests" / "Mcp" / "TestDomainReload.cs"
)
RELOAD_TIMESTAMP_PATTERN = re.compile(r"Reload Timestamp:\s*\d+")
TOOL_READ_UNITY_CONSOLE_LOGS = "read_unity_console_logs"
TOOL_GET_UNITY_INFO = "get_unity_info"
TOOL_EXECUTE_CSHARP = "execute_csharp_script_in_unity_editor"
AVAILABLE_OPERATIONS = (
    TOOL_READ_UNITY_CONSOLE_LOGS,
    TOOL_GET_UNITY_INFO,
    "scan_project_with_csharp",
    "run_long_csharp_script",
    "force_domain_reload",
    "force_domain_recompile_and_reload",
)
ANSI_RESET = "\033[0m"
ANSI_COLORS = {
    "red": "\033[31m",
    "green": "\033[32m",
    "yellow": "\033[33m",
    "blue": "\033[34m",
    "magenta": "\033[35m",
    "cyan": "\033[36m",
}
USE_COLOR = sys.stdout.isatty()


@dataclass(frozen=True)
class Operation:
    name: str


def build_server_parameters(bridge_dir: Path) -> StdioServerParameters:
    return StdioServerParameters(
        command="uv",
        args=[
            "run",
            "--directory",
            str(bridge_dir),
            "unity-code-mcp-stdio",
        ],
    )


def update_reload_timestamp(source: str, timestamp: int) -> tuple[str, str, str]:
    match = RELOAD_TIMESTAMP_PATTERN.search(source)
    if match is None:
        raise ValueError("Reload Timestamp marker was not found in the target file")

    previous_marker = match.group(0)
    next_marker = f"Reload Timestamp: {timestamp}"
    updated = RELOAD_TIMESTAMP_PATTERN.sub(next_marker, source, count=1)
    return updated, previous_marker, next_marker


def build_operation_sequence(length: int, seed: int) -> list[Operation]:
    rng = random.Random(seed)
    return [Operation(name=rng.choice(AVAILABLE_OPERATIONS)) for _ in range(length)]


def normalize_for_json(value: Any) -> Any:
    if isinstance(value, Path):
        return str(value)
    if isinstance(value, Operation):
        return {"name": value.name}
    if isinstance(value, dict):
        return {str(key): normalize_for_json(item) for key, item in value.items()}
    if isinstance(value, (list, tuple, set)):
        return [normalize_for_json(item) for item in value]
    if hasattr(value, "model_dump"):
        return normalize_for_json(
            value.model_dump(mode="json", by_alias=True, exclude_none=True)
        )
    return value


def colorize(text: str, color: str) -> str:
    if not USE_COLOR or color not in ANSI_COLORS:
        return text
    return f"{ANSI_COLORS[color]}{text}{ANSI_RESET}"


def log_json(title: str, payload: Any, color: str = "cyan") -> None:
    print(colorize(title, color))
    print(
        json.dumps(
            normalize_for_json(payload), ensure_ascii=True, indent=2, default=str
        )
    )


def build_scan_project_script() -> str:
    return """using System.Text;

var hierarchy = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(UnityEngine.FindObjectsSortMode.None)
    .OrderBy(go => go.name)
    .Take(25)
    .Select(go => $"{go.name} | active={go.activeSelf} | scene={go.scene.name}")
    .ToList();

var assetPaths = UnityEditor.AssetDatabase.FindAssets(string.Empty, new[] { "Assets" })
    .Take(25)
    .Select(guid => UnityEditor.AssetDatabase.GUIDToAssetPath(guid))
    .ToList();

var builder = new StringBuilder();
builder.AppendLine($"Hierarchy object sample count: {hierarchy.Count}");
foreach (var item in hierarchy)
{
    builder.AppendLine($"HIERARCHY {item}");
}

builder.AppendLine($"Asset sample count: {assetPaths.Count}");
foreach (var assetPath in assetPaths)
{
    builder.AppendLine($"ASSET {assetPath}");
}

UnityEngine.Debug.Log(builder.ToString());"""


def build_long_running_script(duration_seconds: int) -> str:
    return f"""var durationSeconds = {duration_seconds}.0;
var startedAt = UnityEditor.EditorApplication.timeSinceStartup;
var nextHeartbeat = 1;
var iterations = 0;

while (UnityEditor.EditorApplication.timeSinceStartup - startedAt < durationSeconds)
{{
    iterations++;
    if (UnityEditor.EditorApplication.timeSinceStartup - startedAt >= nextHeartbeat)
    {{
        UnityEngine.Debug.Log($"Long-running script heartbeat second={{nextHeartbeat}} iterations={{iterations}}");
        nextHeartbeat++;
    }}
}}

var elapsedMilliseconds = (UnityEditor.EditorApplication.timeSinceStartup - startedAt) * 1000.0;
UnityEngine.Debug.Log($"Long-running script completed elapsed_ms={{elapsedMilliseconds:F0}} iterations={{iterations}}");"""


def workspace_to_asset_path(path: Path) -> str:
    resolved_path = path.resolve()
    resolved_root = WORKSPACE_ROOT.resolve()
    relative_path = resolved_path.relative_to(resolved_root)
    if not relative_path.parts or relative_path.parts[0] != "Assets":
        raise ValueError(f"Path is not inside the Unity Assets folder: {path}")
    return relative_path.as_posix()


def build_force_recompile_and_reload_script(asset_path: str) -> str:
    return f'''using UnityEditor.Compilation;

var assetPath = "{asset_path}";
UnityEditor.AssetDatabase.ImportAsset(assetPath, UnityEditor.ImportAssetOptions.ForceUpdate);
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
UnityEngine.Debug.Log($"Requested asset import, script compilation, and domain reload for {{assetPath}}");'''


def atomic_write_text(path: Path, content: str) -> None:
    temporary_path = path.with_name(f".{path.stem}.{time.time_ns()}{path.suffix}.tmp")
    temporary_path.write_text(content, encoding="utf-8")
    os.replace(temporary_path, path)


def build_exception_payload(operation_name: str, exc: Exception) -> dict[str, Any]:
    return {
        "operation": operation_name,
        "exception_type": type(exc).__name__,
        "message": str(exc),
        "exception_repr": repr(exc),
        "traceback": "".join(traceback.format_exception(exc)),
    }


def extract_text_blocks(result: types.CallToolResult) -> list[str]:
    text_blocks: list[str] = []
    for content in result.content:
        if isinstance(content, types.TextContent):
            text_blocks.append(content.text)
    return text_blocks


def call_result_failed(result: types.CallToolResult) -> bool:
    if result.isError:
        return True
    return any(block.startswith("Error:") for block in extract_text_blocks(result))


def should_log_response_payload(result: types.CallToolResult) -> bool:
    return True


def response_log_color(result: types.CallToolResult) -> str:
    return "red" if call_result_failed(result) else "green"


def create_stdio_client(
    server_parameters: StdioServerParameters,
) -> AbstractAsyncContextManager[Any]:
    return cast(AbstractAsyncContextManager[Any], stdio_client(server_parameters))


async def logging_callback(params: types.LoggingMessageNotificationParams) -> None:
    log_json("server logging/message", params, color="magenta")


async def message_handler(
    message: Any,
) -> None:
    if isinstance(message, Exception):
        print(colorize(f"server message exception {message}", "red"))
        return
    log_json("server message", message, color="yellow")


async def call_tool_and_log(
    session: ClientSession,
    tool_name: str,
    arguments: dict[str, Any] | None,
    timeout_seconds: float,
) -> tuple[bool, types.CallToolResult]:
    request_payload = {
        "tool": tool_name,
        "arguments": arguments or {},
        "timeout_seconds": timeout_seconds,
    }
    log_json(f"request {tool_name}", request_payload, color="green")
    result = await session.call_tool(
        tool_name,
        arguments=arguments,
        read_timeout_seconds=timedelta(seconds=timeout_seconds),
    )
    if should_log_response_payload(result):
        log_json(f"response {tool_name}", result, color=response_log_color(result))
    return (not call_result_failed(result), result)


async def execute_operation(
    session: ClientSession,
    operation: Operation,
    args: argparse.Namespace,
) -> tuple[bool, dict[str, Any]]:
    if operation.name == TOOL_READ_UNITY_CONSOLE_LOGS:
        success, result = await call_tool_and_log(
            session,
            TOOL_READ_UNITY_CONSOLE_LOGS,
            {"max_entries": args.console_log_limit},
            args.request_timeout_seconds,
        )
        return success, {
            "operation": operation.name,
            "response": normalize_for_json(result),
        }

    if operation.name == TOOL_GET_UNITY_INFO:
        success, result = await call_tool_and_log(
            session,
            TOOL_GET_UNITY_INFO,
            None,
            args.request_timeout_seconds,
        )
        return success, {
            "operation": operation.name,
            "response": normalize_for_json(result),
        }

    if operation.name == "scan_project_with_csharp":
        success, result = await call_tool_and_log(
            session,
            TOOL_EXECUTE_CSHARP,
            {"script": build_scan_project_script()},
            args.request_timeout_seconds,
        )
        return success, {
            "operation": operation.name,
            "response": normalize_for_json(result),
        }

    if operation.name == "run_long_csharp_script":
        timeout_seconds = max(
            args.request_timeout_seconds, args.long_script_seconds + 5.0
        )
        success, result = await call_tool_and_log(
            session,
            TOOL_EXECUTE_CSHARP,
            {"script": build_long_running_script(args.long_script_seconds)},
            timeout_seconds,
        )
        return success, {
            "operation": operation.name,
            "response": normalize_for_json(result),
        }

    if operation.name == "force_domain_reload":
        source = args.domain_reload_file.read_text(encoding="utf-8")
        timestamp = time.time_ns()
        updated, previous_marker, next_marker = update_reload_timestamp(
            source, timestamp
        )
        atomic_write_text(args.domain_reload_file, updated)
        asset_path = workspace_to_asset_path(args.domain_reload_file)
        success, result = await call_tool_and_log(
            session,
            TOOL_EXECUTE_CSHARP,
            {"script": build_force_recompile_and_reload_script(asset_path)},
            args.request_timeout_seconds,
        )
        response = {
            "operation": operation.name,
            "file": args.domain_reload_file,
            "asset_path": asset_path,
            "previous_marker": previous_marker,
            "new_marker": next_marker,
            "timestamp": timestamp,
            "reload_response": normalize_for_json(result),
        }
        print(
            colorize(
                f"force_domain_reload updated {args.domain_reload_file.name} from {previous_marker} to {next_marker} and requested recompilation for {asset_path}",
                "green" if success else "red",
            )
        )
        return success, normalize_for_json(response)

    if operation.name == "force_domain_recompile_and_reload":
        asset_path = workspace_to_asset_path(args.domain_reload_file)
        success, result = await call_tool_and_log(
            session,
            TOOL_EXECUTE_CSHARP,
            {"script": build_force_recompile_and_reload_script(asset_path)},
            args.request_timeout_seconds,
        )
        response = {
            "operation": operation.name,
            "file": args.domain_reload_file,
            "asset_path": asset_path,
            "response": normalize_for_json(result),
        }
        print(
            colorize(
                f"force_domain_recompile_and_reload requested recompilation for {asset_path}",
                "green" if success else "red",
            )
        )
        return success, normalize_for_json(response)

    raise ValueError(f"Unknown operation: {operation.name}")


def ensure_required_tools(tools_result: types.ListToolsResult) -> None:
    available_tool_names = {tool.name for tool in tools_result.tools}
    required_tools = {
        TOOL_READ_UNITY_CONSOLE_LOGS,
        TOOL_GET_UNITY_INFO,
        TOOL_EXECUTE_CSHARP,
    }
    missing = sorted(required_tools - available_tool_names)
    if missing:
        raise RuntimeError(
            f"The MCP server did not expose required tools: {', '.join(missing)}"
        )


def validate_args(args: argparse.Namespace) -> None:
    if args.sequence_length <= 0:
        raise ValueError("--sequence-length must be greater than zero")
    if args.console_log_limit <= 0:
        raise ValueError("--console-log-limit must be greater than zero")
    if args.long_script_seconds <= 0:
        raise ValueError("--long-script-seconds must be greater than zero")
    if args.request_timeout_seconds <= 0:
        raise ValueError("--request-timeout-seconds must be greater than zero")
    if not args.bridge_dir.exists():
        raise FileNotFoundError(f"Bridge directory does not exist: {args.bridge_dir}")
    if not args.domain_reload_file.exists():
        raise FileNotFoundError(
            f"Domain reload file does not exist: {args.domain_reload_file}"
        )


class ScriptSelfTests(unittest.TestCase):
    def test_build_server_parameters_uses_requested_stdio_command(self) -> None:
        params = build_server_parameters(DEFAULT_BRIDGE_DIR)

        self.assertEqual(params.command, "uv")
        self.assertEqual(
            params.args,
            [
                "run",
                "--directory",
                str(DEFAULT_BRIDGE_DIR),
                "unity-code-mcp-stdio",
            ],
        )

    def test_update_reload_timestamp_replaces_existing_marker(self) -> None:
        original = "// Reload Timestamp: 123456789\n"

        updated, previous_marker, next_marker = update_reload_timestamp(
            original, 987654321
        )

        self.assertEqual(previous_marker, "Reload Timestamp: 123456789")
        self.assertEqual(next_marker, "Reload Timestamp: 987654321")
        self.assertEqual(updated, "// Reload Timestamp: 987654321\n")

    def test_build_operation_sequence_is_reproducible(self) -> None:
        sequence = build_operation_sequence(5, 1234)
        names = [operation.name for operation in sequence]
        expected = [operation.name for operation in build_operation_sequence(5, 1234)]

        self.assertEqual(names, expected)
        self.assertEqual(len(names), 5)
        self.assertTrue(
            set(names).issubset(
                {
                    TOOL_READ_UNITY_CONSOLE_LOGS,
                    TOOL_GET_UNITY_INFO,
                    "scan_project_with_csharp",
                    "run_long_csharp_script",
                    "force_domain_reload",
                    "force_domain_recompile_and_reload",
                }
            )
        )

    def test_workspace_to_asset_path_returns_project_relative_assets_path(self) -> None:
        asset_path = workspace_to_asset_path(DEFAULT_DOMAIN_RELOAD_FILE)

        self.assertEqual(asset_path, "Assets/Tests/Mcp/TestDomainReload.cs")

    def test_build_force_recompile_and_reload_script_requests_import_and_compile(
        self,
    ) -> None:
        script = build_force_recompile_and_reload_script(
            "Assets/Tests/Mcp/TestDomainReload.cs"
        )

        self.assertIn(
            "UnityEditor.AssetDatabase.ImportAsset(assetPath, UnityEditor.ImportAssetOptions.ForceUpdate);",
            script,
        )
        self.assertIn(
            "UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);",
            script,
        )
        self.assertIn(
            "UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();",
            script,
        )
        self.assertIn('assetPath = "Assets/Tests/Mcp/TestDomainReload.cs"', script)

    def test_atomic_write_text_replaces_file_contents(self) -> None:
        temp_dir = Path(self._testMethodName)
        temp_dir.mkdir(exist_ok=True)
        try:
            target = temp_dir / "sample.cs"
            target.write_text("before", encoding="utf-8")

            atomic_write_text(target, "after")

            self.assertEqual(target.read_text(encoding="utf-8"), "after")
        finally:
            for child in temp_dir.iterdir():
                child.unlink()
            temp_dir.rmdir()

    def test_should_log_response_payload_is_true_for_success(self) -> None:
        result = types.CallToolResult(
            content=[types.TextContent(type="text", text="all good")],
            isError=False,
        )

        self.assertTrue(should_log_response_payload(result))

    def test_should_log_response_payload_is_true_for_error(self) -> None:
        result = types.CallToolResult(
            content=[types.TextContent(type="text", text="Error: broken")],
            isError=True,
        )

        self.assertTrue(should_log_response_payload(result))

    def test_response_log_color_is_green_for_success(self) -> None:
        result = types.CallToolResult(
            content=[types.TextContent(type="text", text="all good")],
            isError=False,
        )

        self.assertEqual(response_log_color(result), "green")

    def test_response_log_color_is_red_for_error(self) -> None:
        result = types.CallToolResult(
            content=[types.TextContent(type="text", text="Error: broken")],
            isError=True,
        )

        self.assertEqual(response_log_color(result), "red")

    def test_build_exception_payload_includes_traceback_and_repr(self) -> None:
        try:
            raise RuntimeError("bridge dropped during reload")
        except RuntimeError as exc:
            payload = build_exception_payload("force_domain_reload", exc)

        self.assertEqual(payload["operation"], "force_domain_reload")
        self.assertEqual(payload["exception_type"], "RuntimeError")
        self.assertEqual(payload["message"], "bridge dropped during reload")
        self.assertEqual(
            payload["exception_repr"], "RuntimeError('bridge dropped during reload')"
        )
        self.assertTrue(payload["traceback"])
        self.assertIn(
            "RuntimeError: bridge dropped during reload", payload["traceback"]
        )

    def test_build_exception_payload_captures_connection_reset_error(self) -> None:
        try:
            raise ConnectionResetError(
                "[WinError 64] The specified network name is no longer available"
            )
        except ConnectionResetError as exc:
            payload = build_exception_payload("get_unity_info", exc)

        self.assertEqual(payload["exception_type"], "ConnectionResetError")
        self.assertIn("WinError 64", payload["message"])
        self.assertIn("ConnectionResetError", payload["traceback"])

    def test_build_exception_payload_captures_connection_refused_error(self) -> None:
        try:
            raise ConnectionRefusedError("[WinError 10061] No connection could be made")
        except ConnectionRefusedError as exc:
            payload = build_exception_payload("read_unity_console_logs", exc)

        self.assertEqual(payload["exception_type"], "ConnectionRefusedError")
        self.assertIn("WinError 10061", payload["message"])

    def test_call_result_failed_detects_unity_error_prefix(self) -> None:
        result = types.CallToolResult(
            content=[
                types.TextContent(type="text", text="Error: Unity server unreachable")
            ],
            isError=False,
        )

        self.assertTrue(call_result_failed(result))

    def test_extract_text_blocks_returns_all_text_content(self) -> None:
        result = types.CallToolResult(
            content=[
                types.TextContent(type="text", text="block one"),
                types.TextContent(type="text", text="block two"),
            ],
            isError=False,
        )

        blocks = extract_text_blocks(result)

        self.assertEqual(blocks, ["block one", "block two"])


def run_self_tests() -> int:
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(ScriptSelfTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    return 0 if result.wasSuccessful() else 1


async def run_sequence(_args: argparse.Namespace) -> int:
    args = _args
    validate_args(args)

    if args.seed is None:
        args.seed = int(time.time() * 1000)

    sequence = build_operation_sequence(args.sequence_length, args.seed)
    log_json(
        "startup",
        {
            "workspace_root": WORKSPACE_ROOT,
            "bridge_dir": args.bridge_dir,
            "domain_reload_file": args.domain_reload_file,
            "sequence_length": args.sequence_length,
            "seed": args.seed,
            "delay_between_steps": args.delay_between_steps,
        },
        color="cyan",
    )
    log_json(
        "planned sequence", [operation.name for operation in sequence], color="cyan"
    )

    server_parameters = build_server_parameters(args.bridge_dir)
    async with create_stdio_client(server_parameters) as (read_stream, write_stream):
        client_info = types.Implementation(
            name="unity-mcp-random-tester", version="0.1.0"
        )
        async with ClientSession(
            read_stream,
            write_stream,
            read_timeout_seconds=timedelta(
                seconds=max(
                    args.request_timeout_seconds, args.long_script_seconds + 5.0
                )
            ),
            logging_callback=logging_callback,
            message_handler=message_handler,
            client_info=client_info,
        ) as session:
            initialize_result = await session.initialize()
            server_info = initialize_result.serverInfo
            print(
                colorize(
                    f"initialized server={server_info.name} version={server_info.version} protocol={initialize_result.protocolVersion}",
                    "green",
                )
            )

            tools_result = await session.list_tools()
            print(colorize(f"discovered tools={len(tools_result.tools)}", "green"))
            ensure_required_tools(tools_result)

            failures = 0
            summary_rows: list[tuple[int, str, str, float]] = []

            for index, operation in enumerate(sequence, start=1):
                print(
                    colorize(
                        f"=== step {index}/{len(sequence)} {operation.name} ===", "cyan"
                    )
                )
                started_at = time.perf_counter()
                try:
                    success, response_payload = await execute_operation(
                        session, operation, args
                    )
                except Exception as exc:
                    success = False
                    response_payload = build_exception_payload(operation.name, exc)
                    log_json(
                        f"response {operation.name}", response_payload, color="red"
                    )

                duration_seconds = time.perf_counter() - started_at
                summary_rows.append(
                    (
                        index,
                        operation.name,
                        "ok" if success else "failed",
                        duration_seconds,
                    )
                )

                if not success:
                    failures += 1
                    log_json(
                        f"failed step {index} {operation.name}",
                        response_payload,
                        color="red",
                    )
                    if args.stop_on_error:
                        break

                if args.delay_between_steps > 0:
                    await asyncio.sleep(args.delay_between_steps)

            print(colorize("Operation Summary", "cyan"))
            for index, operation_name, status, duration_seconds in summary_rows:
                print(
                    f"{index:>2}  {operation_name:<28} {status:<7} {duration_seconds:>6.2f}s"
                )

            completion_color = "red" if failures else "green"
            print(
                colorize(
                    f"completed steps={len(summary_rows)} failures={failures} seed={args.seed}",
                    completion_color,
                )
            )
            return 1 if failures else 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Randomized MCP tester for the Unity Code MCP STDIO bridge"
    )
    parser.add_argument(
        "--self-test", action="store_true", help="Run the built-in self-tests and exit"
    )
    parser.add_argument(
        "--sequence-length",
        type=int,
        default=20,
        help="Number of random operations to run",
    )
    parser.add_argument("--seed", type=int, help="Deterministic random seed")
    parser.add_argument(
        "--bridge-dir",
        type=Path,
        default=DEFAULT_BRIDGE_DIR,
        help="Path to the Unity STDIO bridge directory",
    )
    parser.add_argument(
        "--domain-reload-file",
        type=Path,
        default=DEFAULT_DOMAIN_RELOAD_FILE,
        help="Path to the C# file whose timestamp marker is updated to force domain reloads",
    )
    parser.add_argument(
        "--console-log-limit",
        type=int,
        default=2,
        help="Max Unity console entries to request",
    )
    parser.add_argument(
        "--long-script-seconds",
        type=int,
        default=5,
        help="Duration for the long-running C# script",
    )
    parser.add_argument(
        "--request-timeout-seconds",
        type=float,
        default=20.0,
        help="Per-request timeout for MCP tool calls",
    )
    parser.add_argument(
        "--delay-between-steps",
        type=float,
        default=0.0,
        help="Optional delay between operations",
    )
    parser.add_argument(
        "--stop-on-error",
        action="store_true",
        help="Stop the sequence immediately after the first failed operation; without this flag the script records the failure and continues",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.self_test:
        return run_self_tests()
    return asyncio.run(run_sequence(args))


if __name__ == "__main__":
    raise SystemExit(main())
