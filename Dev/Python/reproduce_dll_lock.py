"""
DLL Lock Issue Reproduction Script

This script reproduces the DLL locking error that occurs when:
1. A script change triggers Unity to rebuild Assembly-CSharp.dll
2. The MCP server's script execution tool loads assemblies into memory (via Roslyn)
3. Unity cannot copy the DLL because it's being held by the Roslyn script execution

Error reproduced:
"Library/ScriptAssemblies/Assembly-CSharp.dll: Copying the file failed:
The process cannot access the file because it is being used by another process."

Steps:
1. Modify DllLock.cs (linearVelocity -> velocity) to trigger Unity's API update dialog
2. Immediately call MCP tool to execute a script (loads DLLs via Roslyn)
3. Unity attempts to rebuild but the DLL is locked

Usage:
    uv run reproduce_dll_lock [--host HOST] [--port PORT] [--delay DELAY]
"""

import argparse
import asyncio
import os
from datetime import datetime
from pathlib import Path

from mcp import ClientSession
import pygetwindow as gw
from mcp.client.stdio import stdio_client, StdioServerParameters

# Paths relative to this script's location
SCRIPT_DIR = Path(__file__).parent.resolve()
WORKSPACE_ROOT = SCRIPT_DIR.parent.parent  # Go up to UnityCodeMcpServer root
DLL_LOCK_CS_PATH = WORKSPACE_ROOT / "Assets" / "Scripts" / "DllLock" / "DllLock.cs"
STDIO_BRIDGE_PATH = (
    WORKSPACE_ROOT / "Assets" / "Plugins" / "UnityCodeMcpServer" / "Editor" / "STDIO~"
)

# Original and modified content patterns
ORIGINAL_CODE = "rb.linearVelocity = Vector3.zero;"
MODIFIED_CODE = "rb.velocity = Vector3.zero;"
COMMENT_PREFIX = "// dll-lock-timestamp: "


def get_server_params(host: str, port: int) -> StdioServerParameters:
    """Create MCP server parameters for the Unity STDIO bridge."""
    return StdioServerParameters(
        command="uv",
        args=[
            "run",
            "--directory",
            str(STDIO_BRIDGE_PATH),
            "unity-code-mcp-stdio",
            "--host",
            host,
            "--port",
            str(port),
        ],
    )


def modify_dll_lock_file(to_velocity: bool = True) -> bool:
    """
    Modify DllLock.cs to trigger Unity rebuild.

    Args:
        to_velocity: If True, change linearVelocity -> velocity (triggers rebuild + API update dialog)
                     If False, revert velocity -> linearVelocity

    Returns:
        True if modification was successful, False otherwise.
    """
    if not DLL_LOCK_CS_PATH.exists():
        print(f"[ERROR] DllLock.cs not found at: {DLL_LOCK_CS_PATH}")
        return False

    try:
        content = DLL_LOCK_CS_PATH.read_text(encoding="utf-8")

        if to_velocity:
            if MODIFIED_CODE in content:
                print(f"[INFO] DllLock.cs already contains '{MODIFIED_CODE}'")
                return True
            if ORIGINAL_CODE not in content:
                print(f"[WARNING] Expected code not found in DllLock.cs")
                print(f"  Looking for: {ORIGINAL_CODE}")
                return False
            new_content = content.replace(ORIGINAL_CODE, MODIFIED_CODE)
            print(f"[MODIFY] Changed '{ORIGINAL_CODE}' -> '{MODIFIED_CODE}'")
        else:
            if ORIGINAL_CODE in content:
                print(f"[INFO] DllLock.cs already contains '{ORIGINAL_CODE}'")
                return True
            if MODIFIED_CODE not in content:
                print(f"[WARNING] Expected code not found in DllLock.cs")
                print(f"  Looking for: {MODIFIED_CODE}")
                return False
            new_content = content.replace(MODIFIED_CODE, ORIGINAL_CODE)
            print(f"[MODIFY] Changed '{MODIFIED_CODE}' -> '{ORIGINAL_CODE}'")

        DLL_LOCK_CS_PATH.write_text(new_content, encoding="utf-8")
        print(f"[OK] DllLock.cs modified successfully")
        return True

    except Exception as e:
        print(f"[ERROR] Failed to modify DllLock.cs: {e}")
        return False


def revert_dll_lock_file() -> bool:
    """Revert DllLock.cs to original state (linearVelocity)."""
    return modify_dll_lock_file(to_velocity=False)


def add_timestamp_comment() -> bool:
    """Add or update a timestamp comment to trigger a rebuild without code changes."""
    if not DLL_LOCK_CS_PATH.exists():
        print(f"[ERROR] DllLock.cs not found at: {DLL_LOCK_CS_PATH}")
        return False

    try:
        content = DLL_LOCK_CS_PATH.read_text(encoding="utf-8")
        timestamp = datetime.now().isoformat(sep=" ", timespec="seconds")
        comment_line = f"{COMMENT_PREFIX}{timestamp}"

        if COMMENT_PREFIX in content:
            lines = content.splitlines()
            updated_lines = [
                comment_line if line.startswith(COMMENT_PREFIX) else line
                for line in lines
            ]
            new_content = "\n".join(updated_lines)
            if content.endswith("\n"):
                new_content += "\n"
            print(f"[MODIFY] Updated timestamp comment: {comment_line}")
        else:
            new_content = content
            if not new_content.endswith("\n"):
                new_content += "\n"
            new_content += f"{comment_line}\n"
            print(f"[MODIFY] Added timestamp comment: {comment_line}")

        DLL_LOCK_CS_PATH.write_text(new_content, encoding="utf-8")
        print("[OK] Timestamp comment updated successfully")
        return True
    except Exception as e:
        print(f"[ERROR] Failed to update timestamp comment: {e}")
        return False


def focus_unity_window() -> bool:
    """Focus the Unity Editor window."""
    try:
        candidates = gw.getWindowsWithTitle("UnityCodeMcpServer - SampleScene")
        if not candidates:
            print("[ERROR] No Unity windows found.")
            return False

        window = candidates[0]
        if window.isMinimized:
            window.restore()
        window.activate()
        print(f"[OK] Focused Unity window: {window.title}")
        return True
    except Exception as e:
        print(f"[ERROR] Failed to focus Unity window: {e}")
        return False


async def execute_test_script(session: ClientSession) -> str:
    """
    Execute a simple test script via MCP to trigger DLL loading.

    This causes Roslyn/ExecuteCSharpScriptInUnityEditor to load Assembly-CSharp.dll
    and other project assemblies into memory, potentially locking them.
    """
    script = 'Debug.Log("DLL Lock Test: Script executed successfully at " + System.DateTime.Now);'

    print(f"\n[EXECUTE] Running script via MCP tool...")
    print(f"  Script: {script}")

    result = await session.call_tool(
        "execute_csharp_script_in_unity_editor",
        {"script": script},
    )

    # Extract text content from result
    output_parts = []
    for content in result.content:
        if hasattr(content, "text"):
            output_parts.append(content.text)

    return "\n".join(output_parts) if output_parts else "(no output)"


async def execute_rebuild_script(session: ClientSession) -> str:
    """
    Execute a script that forces Unity to start script recompilation.

    This increases the chance of overlapping compilation with tool execution.
    """
    script = (
        "UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();"
        "UnityEditor.AssetDatabase.Refresh();"
        'Debug.Log("DLL Lock Test: Requested script compilation at " + System.DateTime.Now);'
    )

    print(f"\n[EXECUTE] Forcing script compilation via MCP tool...")
    print(f"  Script: {script}")

    result = await session.call_tool(
        "execute_csharp_script_in_unity_editor",
        {"script": script},
    )

    output_parts = []
    for content in result.content:
        if hasattr(content, "text"):
            output_parts.append(content.text)

    return "\n".join(output_parts) if output_parts else "(no output)"


async def list_available_tools(session: ClientSession) -> list[str]:
    """List all available tools from the MCP server."""
    result = await session.list_tools()
    return [tool.name for tool in result.tools]


async def reproduce_dll_lock(host: str, port: int, delay: float, revert: bool = False):
    """
    Main reproduction sequence:
    1. Modify DllLock.cs to trigger Unity rebuild
    2. After a short delay, call MCP tool to execute script (loads DLLs)
    3. Unity should show error about DLL being locked

    Args:
        host: Unity MCP Server host
        port: Unity MCP Server port
        delay: Delay in seconds between file modification and MCP call
        revert: If True, revert the file instead of modifying it
    """
    print("=" * 70)
    print("DLL Lock Issue Reproduction Script")
    print("=" * 70)
    print()

    server_params = get_server_params(host, port)
    print(f"[INFO] Using STDIO bridge at: {STDIO_BRIDGE_PATH}")
    print(f"[INFO] Connecting to Unity at {host}:{port}")
    print()

    # Redirect stderr to devnull to suppress MCP library noise
    devnull = open(os.devnull, "w")

    try:
        async with stdio_client(server_params, errlog=devnull) as (read, write):
            async with ClientSession(read, write) as session:
                # Initialize the session
                await session.initialize()
                print("[OK] Connected to Unity MCP Server via STDIO bridge")

                # List available tools to verify connection
                tools = await list_available_tools(session)
                print(f"[INFO] Available tools: {len(tools)}")
                if "execute_csharp_script_in_unity_editor" not in tools:
                    print(
                        "[ERROR] Required tool 'execute_csharp_script_in_unity_editor' not found!"
                    )
                    return False

                # Step 1: Modify DllLock.cs to trigger Unity rebuild
                print("\n" + "-" * 70)
                print("STEP 1: Modify DllLock.cs to trigger Unity rebuild")
                print("-" * 70)

                if revert:
                    if not revert_dll_lock_file():
                        print("[FAILED] Could not revert DllLock.cs")
                        return False
                else:
                    if not modify_dll_lock_file(to_velocity=True):
                        print("[FAILED] Could not modify DllLock.cs")
                        return False

                # Step 2: Force script compilation via MCP tool
                print("\n" + "-" * 70)
                print("STEP 2: Force script compilation")
                print("-" * 70)

                await execute_rebuild_script(session)

                # Step 3: Wait a short delay (Unity starts detecting file changes)
                print(f"\n[WAIT] Waiting {delay}s for Unity to detect file change...")
                await asyncio.sleep(delay)

                # Step 4: Execute MCP tool to load DLLs via Roslyn
                print("\n" + "-" * 70)
                print("STEP 3: Execute MCP tool (loads DLLs via Roslyn)")
                print("-" * 70)

                output = await execute_test_script(session)

                # Step 4: Display results
                print("\n" + "-" * 70)
                print("RESULT")
                print("-" * 70)

                # Print first 2000 chars to avoid flooding console
                if len(output) > 2000:
                    print(output[:2000])
                    print(f"... (truncated, {len(output)} total chars)")
                else:
                    print(output)

                print("\n" + "=" * 70)
                print("NEXT STEPS")
                print("=" * 70)
                print("""
If the DLL lock issue was reproduced, you should see in Unity:
  - An API Update dialog (asking to update 'velocity' -> 'linearVelocity')
  - An error: "Library\\ScriptAssemblies\\Assembly-CSharp.dll: Copying the
    file failed: The process cannot access the file because it is being
    used by another process."

To revert the file change, run:
  uv run reproduce_dll_lock --revert
""")

                return True

    except Exception as e:
        print(f"\n[ERROR] Failed: {e}")
        import traceback

        traceback.print_exc()
        return False
    finally:
        devnull.close()


async def run_modify_only(revert: bool = False) -> bool:
    """Only modify or revert the file without calling MCP tools."""
    print("Running file modification only...")
    if revert:
        return revert_dll_lock_file()
    return modify_dll_lock_file(to_velocity=True)


async def run_timestamp_only() -> bool:
    """Only update timestamp comment without calling MCP tools."""
    print("Running timestamp comment update only...")
    return add_timestamp_comment()


async def run_rebuild_only(host: str, port: int) -> bool:
    """Only trigger a rebuild via MCP tool without modifying files."""
    print("Running rebuild only...")
    server_params = get_server_params(host, port)
    devnull = open(os.devnull, "w")

    try:
        async with stdio_client(server_params, errlog=devnull) as (read, write):
            async with ClientSession(read, write) as session:
                await session.initialize()
                print("[OK] Connected to Unity MCP Server")
                await execute_rebuild_script(session)
                return True
    except Exception as e:
        print(f"[ERROR] Failed: {e}")
        import traceback

        traceback.print_exc()
        return False
    finally:
        devnull.close()


async def run_tool_only(host: str, port: int) -> bool:
    """Only execute MCP tool without modifying files."""
    print("Executing MCP tool without file modification...")

    server_params = get_server_params(host, port)
    devnull = open(os.devnull, "w")

    try:
        async with stdio_client(server_params, errlog=devnull) as (read, write):
            async with ClientSession(read, write) as session:
                await session.initialize()
                print("[OK] Connected to Unity MCP Server")

                output = await execute_test_script(session)
                print("\n" + "-" * 70)
                print("OUTPUT")
                print("-" * 70)
                print(output)
                return True
    except Exception as e:
        print(f"[ERROR] Failed: {e}")
        import traceback

        traceback.print_exc()
        return False
    finally:
        devnull.close()


async def run_steps(
    steps: list[str],
    host: str,
    port: int,
    delay: float,
    revert: bool,
) -> bool:
    """Run selected steps in the requested order."""
    valid_steps = {"modify", "rebuild", "wait", "execute", "timestamp", "focus"}
    unknown = [step for step in steps if step not in valid_steps]
    if unknown:
        print(f"[ERROR] Unknown steps: {', '.join(unknown)}")
        print("Valid steps: modify,rebuild,wait,execute,timestamp")
        return False

    requires_mcp = any(step in {"rebuild", "execute"} for step in steps)

    if not requires_mcp:
        for step in steps:
            if step == "modify":
                print("\n" + "-" * 70)
                print("STEP: Modify DllLock.cs")
                print("-" * 70)
                if revert:
                    if not revert_dll_lock_file():
                        print("[FAILED] Could not revert DllLock.cs")
                        return False
                else:
                    if not modify_dll_lock_file(to_velocity=True):
                        print("[FAILED] Could not modify DllLock.cs")
                        return False

            elif step == "timestamp":
                print("\n" + "-" * 70)
                print("STEP: Update timestamp comment")
                print("-" * 70)
                if not add_timestamp_comment():
                    print("[FAILED] Could not update timestamp comment")
                    return False

            elif step == "focus":
                print("\n" + "-" * 70)
                print("STEP: Focus Unity window")
                print("-" * 70)
                if not focus_unity_window():
                    print("[FAILED] Could not focus Unity window")
                    return False

            elif step == "wait":
                print("\n" + "-" * 70)
                print("STEP: Wait")
                print("-" * 70)
                print(f"[WAIT] Waiting {delay}s...")
                await asyncio.sleep(delay)

        return True

    server_params = get_server_params(host, port)
    devnull = open(os.devnull, "w")

    try:
        async with stdio_client(server_params, errlog=devnull) as (read, write):
            async with ClientSession(read, write) as session:
                await session.initialize()
                print("[OK] Connected to Unity MCP Server via STDIO bridge")

                tools = await list_available_tools(session)
                if "execute_csharp_script_in_unity_editor" not in tools:
                    print(
                        "[ERROR] Required tool 'execute_csharp_script_in_unity_editor' not found!"
                    )
                    return False

                for step in steps:
                    if step == "modify":
                        print("\n" + "-" * 70)
                        print("STEP: Modify DllLock.cs")
                        print("-" * 70)
                        if revert:
                            if not revert_dll_lock_file():
                                print("[FAILED] Could not revert DllLock.cs")
                                return False
                        else:
                            if not modify_dll_lock_file(to_velocity=True):
                                print("[FAILED] Could not modify DllLock.cs")
                                return False

                    elif step == "rebuild":
                        print("\n" + "-" * 70)
                        print("STEP: Force script compilation")
                        print("-" * 70)
                        await execute_rebuild_script(session)

                    elif step == "timestamp":
                        print("\n" + "-" * 70)
                        print("STEP: Update timestamp comment")
                        print("-" * 70)
                        if not add_timestamp_comment():
                            print("[FAILED] Could not update timestamp comment")
                            return False

                    elif step == "focus":
                        print("\n" + "-" * 70)
                        print("STEP: Focus Unity window")
                        print("-" * 70)
                        if not focus_unity_window():
                            print("[FAILED] Could not focus Unity window")
                            return False

                    elif step == "wait":
                        print("\n" + "-" * 70)
                        print("STEP: Wait")
                        print("-" * 70)
                        print(f"[WAIT] Waiting {delay}s...")
                        await asyncio.sleep(delay)

                    elif step == "execute":
                        print("\n" + "-" * 70)
                        print("STEP: Execute MCP tool (loads DLLs via Roslyn)")
                        print("-" * 70)
                        output = await execute_test_script(session)
                        print("\n" + "-" * 70)
                        print("RESULT")
                        print("-" * 70)
                        if len(output) > 2000:
                            print(output[:2000])
                            print(f"... (truncated, {len(output)} total chars)")
                        else:
                            print(output)

                return True
    except Exception as e:
        print(f"\n[ERROR] Failed: {e}")
        import traceback

        traceback.print_exc()
        return False
    finally:
        devnull.close()
    unknown = [step for step in steps if step not in valid_steps]
    if unknown:
        print(f"[ERROR] Unknown steps: {', '.join(unknown)}")
        print("Valid steps: modify,rebuild,wait,execute")
        return False

    server_params = get_server_params(host, port)
    devnull = open(os.devnull, "w")

    try:
        async with stdio_client(server_params, errlog=devnull) as (read, write):
            async with ClientSession(read, write) as session:
                await session.initialize()
                print("[OK] Connected to Unity MCP Server via STDIO bridge")

                tools = await list_available_tools(session)
                if "execute_csharp_script_in_unity_editor" not in tools:
                    print(
                        "[ERROR] Required tool 'execute_csharp_script_in_unity_editor' not found!"
                    )
                    return False

                for step in steps:
                    if step == "modify":
                        print("\n" + "-" * 70)
                        print("STEP: Modify DllLock.cs")
                        print("-" * 70)
                        if revert:
                            if not revert_dll_lock_file():
                                print("[FAILED] Could not revert DllLock.cs")
                                return False
                        else:
                            if not modify_dll_lock_file(to_velocity=True):
                                print("[FAILED] Could not modify DllLock.cs")
                                return False

                    elif step == "rebuild":
                        print("\n" + "-" * 70)
                        print("STEP: Force script compilation")
                        print("-" * 70)
                        await execute_rebuild_script(session)

                    elif step == "timestamp":
                        print("\n" + "-" * 70)
                        print("STEP: Update timestamp comment")
                        print("-" * 70)
                        if not add_timestamp_comment():
                            print("[FAILED] Could not update timestamp comment")
                            return False

                    elif step == "wait":
                        print("\n" + "-" * 70)
                        print("STEP: Wait")
                        print("-" * 70)
                        print(f"[WAIT] Waiting {delay}s...")
                        await asyncio.sleep(delay)

                    elif step == "execute":
                        print("\n" + "-" * 70)
                        print("STEP: Execute MCP tool (loads DLLs via Roslyn)")
                        print("-" * 70)
                        output = await execute_test_script(session)
                        print("\n" + "-" * 70)
                        print("RESULT")
                        print("-" * 70)
                        if len(output) > 2000:
                            print(output[:2000])
                            print(f"... (truncated, {len(output)} total chars)")
                        else:
                            print(output)

                return True
    except Exception as e:
        print(f"\n[ERROR] Failed: {e}")
        import traceback

        traceback.print_exc()
        return False
    finally:
        devnull.close()


async def main_async():
    """Async main entry point."""
    parser = argparse.ArgumentParser(
        description="Reproduce Unity DLL lock issue with MCP server",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "--host",
        default="localhost",
        help="Unity MCP Server host (default: localhost)",
    )
    parser.add_argument(
        "--port",
        type=int,
        default=21088,
        help="Unity MCP Server port (default: 21088)",
    )
    parser.add_argument(
        "--delay",
        type=float,
        default=0.5,
        help="Delay in seconds between file modification and MCP call (default: 0.5)",
    )
    parser.add_argument(
        "--revert",
        action="store_true",
        help="Revert DllLock.cs to original state instead of modifying it",
    )
    parser.add_argument(
        "--no-modify",
        action="store_true",
        help="Skip file modification, only execute MCP tool",
    )
    parser.add_argument(
        "--modify-only",
        action="store_true",
        help="Only modify/revert the file without calling MCP tools",
    )
    parser.add_argument(
        "--rebuild-only",
        action="store_true",
        help="Only trigger a rebuild via MCP tool (no file modifications)",
    )
    parser.add_argument(
        "--comment-timestamp",
        "--timestamp",
        action="store_true",
        help="Only update a timestamp comment to trigger rebuild without code changes",
    )
    parser.add_argument(
        "--steps",
        default="modify,rebuild,wait,execute",
        help="Comma-separated steps to run in order: modify,rebuild,wait,execute,timestamp,focus",
    )

    args = parser.parse_args()

    if args.modify_only:
        await run_modify_only(args.revert)
    elif args.comment_timestamp:
        await run_timestamp_only()
    elif args.rebuild_only:
        await run_rebuild_only(args.host, args.port)
    elif args.no_modify:
        await run_tool_only(args.host, args.port)
    else:
        steps = [step.strip() for step in args.steps.split(",") if step.strip()]
        await run_steps(steps, args.host, args.port, args.delay, args.revert)


def main():
    """Main entry point."""
    asyncio.run(main_async())


if __name__ == "__main__":
    main()
