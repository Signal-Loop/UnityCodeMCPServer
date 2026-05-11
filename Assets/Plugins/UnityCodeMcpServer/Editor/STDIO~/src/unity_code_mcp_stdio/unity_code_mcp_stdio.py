"""Unity Code MCP STDIO bridge backed by file-based transport."""

from __future__ import annotations

import argparse
import asyncio
import itertools
import json
import logging
from logging.handlers import RotatingFileHandler
import os
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
import sys
from typing import Any, Protocol
from uuid import uuid4

import anyio
from anyio import create_memory_object_stream, create_task_group
from mcp import types
from mcp.server import Server
from mcp.shared.message import SessionMessage
from mcp.types import JSONRPCMessage
from pydantic import AnyUrl

# Keep stderr silent so JSON-RPC stdio output is never corrupted.
sys.stderr = open(os.devnull, "w")

script_dir = os.path.dirname(os.path.abspath(__file__))
log_file_path = os.path.join(script_dir, "unity-code-mcp-stdio.log")

logger = logging.getLogger("unity-code-mcp-stdio")
logger.setLevel(logging.INFO)
logger.propagate = False

LOG_MAX_BYTES = 5 * 1024 * 1024
LOG_BACKUP_COUNT = 3
LOG_VALUE_PREVIEW_LIMIT = 160
REQUEST_UNAVAILABLE_ERROR_CODE = -32000
DEFAULT_FILE_REQUEST_TIMEOUT = 180.0
RESPONSE_POLL_INTERVAL_SECONDS = 0.1

formatter = logging.Formatter(
    "%(asctime)s - pid=%(process)d - %(levelname)s - %(message)s"
)

_REQUEST_TRACE_SEQUENCE = itertools.count(1)


class FlushingHandler(RotatingFileHandler):
    """File handler that flushes immediately after each log message."""

    def emit(self, record):
        super().emit(record)
        self.flush()


def _truncate_for_log(value: Any, limit: int = LOG_VALUE_PREVIEW_LIMIT) -> str:
    """Render a compact log-safe preview for structured values."""
    text = str(value).replace("\n", "\\n")
    if len(text) <= limit:
        return text
    return f"{text[:limit]}..."


def _build_rotating_handler(log_path: str | Path) -> FlushingHandler:
    """Create the bridge log handler with explicit retention settings."""
    handler = FlushingHandler(
        str(log_path),
        maxBytes=LOG_MAX_BYTES,
        backupCount=LOG_BACKUP_COUNT,
        encoding="utf-8",
    )
    handler.setLevel(logging.DEBUG)
    handler.setFormatter(formatter)
    return handler


def _configure_logger() -> None:
    """Attach a single rotating file handler to the bridge logger."""
    if logger.handlers:
        return
    logger.addHandler(_build_rotating_handler(log_file_path))


def _describe_request(request_payload: dict[str, Any]) -> str:
    """Summarize a JSON-RPC request for diagnostic logging."""
    params = request_payload.get("params")
    fragments = [
        f"id={request_payload.get('id', 'unknown')}",
        f"method={request_payload.get('method', 'unknown')}",
    ]

    if isinstance(params, dict):
        if "name" in params:
            fragments.append(f"tool={params['name']}")
        if "uri" in params:
            fragments.append(f"uri={_truncate_for_log(params['uri'])}")
        arguments = params.get("arguments")
        if isinstance(arguments, dict):
            argument_keys = ",".join(sorted(arguments.keys())) or "<none>"
            fragments.append(f"argument_keys={argument_keys}")
        else:
            param_keys = ",".join(sorted(params.keys()))
            if param_keys:
                fragments.append(f"param_keys={param_keys}")

    return " ".join(fragments)


def _describe_response(response: dict[str, Any]) -> str:
    """Summarize a JSON-RPC response for diagnostic logging."""
    fragments = [f"id={response.get('id', 'unknown')}"]

    error_payload = response.get("error")
    if isinstance(error_payload, dict):
        fragments.append(f"error_code={error_payload.get('code', 'unknown')}")
        fragments.append(
            "error_message="
            + _truncate_for_log(error_payload.get("message", "Unknown error"))
        )
        return " ".join(fragments)

    result = response.get("result")
    if isinstance(result, dict):
        result_keys = ",".join(sorted(result.keys())) or "<none>"
        fragments.append(f"result_keys={result_keys}")
        for key in ("tools", "prompts", "resources", "content", "messages", "contents"):
            value = result.get(key)
            if isinstance(value, list):
                fragments.append(f"{key}_count={len(value)}")

    return " ".join(fragments)


def _next_request_trace_id() -> str:
    """Return a monotonic bridge-local trace id for correlating log lines."""
    return f"bridge-{next(_REQUEST_TRACE_SEQUENCE):06d}"


_configure_logger()


def _write_text_atomically(path: Path, content: str) -> None:
    """Publish a text file only after its full contents are written."""
    temporary_path = path.with_name(f".{path.name}.{uuid4().hex}.tmp")
    temporary_path.write_text(content, encoding="utf-8")
    os.replace(temporary_path, path)


def get_project_root() -> Path:
    """Resolve the Unity project root from this module's fixed package location."""
    return Path(__file__).resolve().parents[7]


def _timestamp_now() -> str:
    return datetime.now().strftime("%Y%m%d%H%M%S%f")[:-3]


@dataclass(frozen=True)
class FileBridgePaths:
    project_root: Path
    client_id: str

    @property
    def messages_dir(self) -> Path:
        return self.project_root / ".unityCodeMcpServer" / "messages"

    def ensure_messages_dir(self) -> Path:
        created = not self.messages_dir.exists()
        self.messages_dir.mkdir(parents=True, exist_ok=True)
        if created:
            logger.info("Created Unity message directory path=%s", self.messages_dir)
        return self.messages_dir

    def build_request_path(self, timestamp: str) -> Path:
        return self.messages_dir / f"{timestamp}_request_{self.client_id}.json"

    def build_response_path(self, timestamp: str) -> Path:
        return self.messages_dir / f"{timestamp}_response_{self.client_id}.json"


class UnityBridgeClient(Protocol):
    """Minimal client interface shared by the bridge transport."""

    async def send_request(self, request_payload: dict[str, Any]) -> dict[str, Any]: ...

    async def disconnect(self, reason: str = "manual") -> None: ...


class UnityFileClient(UnityBridgeClient):
    """Single-flight file transport client for Unity MCP requests."""

    def __init__(
        self,
        project_root: Path,
        client_id: str | None = None,
        request_timeout: float = DEFAULT_FILE_REQUEST_TIMEOUT,
        response_poll_interval: float = RESPONSE_POLL_INTERVAL_SECONDS,
    ):
        self.paths = FileBridgePaths(
            project_root=Path(project_root),
            client_id=client_id or uuid4().hex,
        )
        self.request_timeout = request_timeout
        self.response_poll_interval = response_poll_interval
        self._lock = asyncio.Lock()

    @staticmethod
    def _build_error(
        request_payload: dict[str, Any], code: int, message: str
    ) -> dict[str, Any]:
        return {
            "jsonrpc": "2.0",
            "id": request_payload.get("id"),
            "error": {"code": code, "message": message},
        }

    async def disconnect(self, reason: str = "manual") -> None:
        logger.info("File bridge reset reason=%s", reason)

    async def _wait_for_response(self, response_path: Path) -> None:
        with anyio.fail_after(self.request_timeout):
            while not response_path.exists():
                logger.debug(
                    "Waiting for Unity file response response=%s",
                    response_path.name,
                )
                await anyio.sleep(self.response_poll_interval)

    async def send_request(self, request_payload: dict[str, Any]) -> dict[str, Any]:
        trace_id = _next_request_trace_id()
        request_summary = _describe_request(request_payload)

        async with self._lock:
            self.paths.ensure_messages_dir()
            timestamp = _timestamp_now()
            request_path = self.paths.build_request_path(timestamp)
            response_path = self.paths.build_response_path(timestamp)

            logger.info(
                "trace=%s Unity file request started %s request=%s response=%s",
                trace_id,
                request_summary,
                request_path.name,
                response_path.name,
            )

            _write_text_atomically(request_path, json.dumps(request_payload))
            logger.debug(
                "trace=%s Unity file request written request=%s bytes=%s",
                trace_id,
                request_path.name,
                request_path.stat().st_size,
            )

            try:
                await self._wait_for_response(response_path)
                logger.debug(
                    "trace=%s Unity file response detected response=%s",
                    trace_id,
                    response_path.name,
                )
                response = json.loads(response_path.read_text(encoding="utf-8"))
            except TimeoutError:
                request_path.unlink(missing_ok=True)
                logger.warning(
                    "trace=%s Unity file request timed out %s request=%s",
                    trace_id,
                    request_summary,
                    request_path.name,
                )
                return self._build_error(
                    request_payload,
                    REQUEST_UNAVAILABLE_ERROR_CODE,
                    f"Timed out waiting for Unity file response after {self.request_timeout} seconds",
                )
            except json.JSONDecodeError as exc:
                logger.error(
                    "trace=%s Unity file response json decode failed %s response=%s error=%s",
                    trace_id,
                    request_summary,
                    response_path.name,
                    exc,
                )
                return self._build_error(
                    request_payload,
                    -32603,
                    f"Invalid Unity file response JSON: {exc}",
                )
            except Exception as exc:
                logger.error(
                    "trace=%s Unity file request failed unexpectedly %s",
                    trace_id,
                    request_summary,
                    exc_info=True,
                )
                return self._build_error(
                    request_payload,
                    -32603,
                    f"Internal error: {exc}",
                )

            request_path.unlink(missing_ok=True)
            response_path.unlink(missing_ok=True)
            logger.info(
                "trace=%s Unity file request completed %s response=%s",
                trace_id,
                request_summary,
                _describe_response(response),
            )
            return response


def _convert_resource_contents(
    resource: dict[str, Any],
) -> types.TextResourceContents:
    """Convert Unity resource payload to an MCP TextResourceContents object."""
    return types.TextResourceContents(
        uri=resource.get("uri", ""),
        mimeType=resource.get("mimeType"),
        text=resource.get("text", ""),
    )


def _convert_content_item(
    item: dict[str, Any],
) -> types.TextContent | types.ImageContent | types.EmbeddedResource | None:
    """Convert a Unity content item to an MCP SDK content item."""
    item_type = item.get("type")
    if item_type == "text":
        return types.TextContent(type="text", text=item.get("text", ""))

    if item_type == "image":
        return types.ImageContent(
            type="image",
            data=item.get("data", ""),
            mimeType=item.get("mimeType", "image/png"),
        )

    if item_type == "resource":
        resource = item.get("resource", {})
        return types.EmbeddedResource(
            type="resource",
            resource=_convert_resource_contents(resource),
        )

    return None


async def _build_call_tool_result(
    unity_client: UnityBridgeClient,
    name: str,
    arguments: dict[str, Any],
) -> types.CallToolResult:
    """Forward a tool call to Unity and preserve the resulting MCP error flag."""
    response = await unity_client.send_request(
        {
            "jsonrpc": "2.0",
            "id": f"call_tool_{name}",
            "method": "tools/call",
            "params": {"name": name, "arguments": arguments},
        }
    )

    if "error" in response:
        error_payload = response["error"]
        return types.CallToolResult(
            content=[
                types.TextContent(
                    type="text",
                    text=f"Error: {error_payload.get('message', 'Unknown error')}",
                )
            ],
            isError=True,
        )

    result = response.get("result", {})
    content = result.get("content", [])
    mcp_content: list[types.ContentBlock] = []
    for item in content:
        converted = _convert_content_item(item)
        if converted is not None:
            mcp_content.append(converted)

    if not mcp_content:
        mcp_content = [types.TextContent(type="text", text="No content returned")]

    return types.CallToolResult(
        content=mcp_content,
        isError=bool(result.get("isError", False)),
    )


def create_server(unity_client: UnityBridgeClient) -> Server:
    """Create MCP server that proxies requests to Unity."""
    server = Server("unity-code-mcp-stdio")

    @server.list_tools()
    async def list_tools() -> list[types.Tool]:
        response = await unity_client.send_request(
            {
                "jsonrpc": "2.0",
                "id": "list_tools",
                "method": "tools/list",
                "params": {},
            }
        )

        if "error" in response:
            logger.error("Error listing tools: %s", response["error"])
            return []

        result = response.get("result", {})
        tools = result.get("tools", [])
        return [
            types.Tool(
                name=tool["name"],
                description=tool.get("description", ""),
                inputSchema=tool.get("inputSchema", {"type": "object"}),
            )
            for tool in tools
        ]

    @server.call_tool()
    async def call_tool(name: str, arguments: dict[str, Any]) -> types.CallToolResult:
        return await _build_call_tool_result(unity_client, name, arguments)

    @server.list_prompts()
    async def list_prompts() -> list[types.Prompt]:
        response = await unity_client.send_request(
            {
                "jsonrpc": "2.0",
                "id": "list_prompts",
                "method": "prompts/list",
                "params": {},
            }
        )

        if "error" in response:
            logger.error("Error listing prompts: %s", response["error"])
            return []

        result = response.get("result", {})
        prompts = result.get("prompts", [])
        return [
            types.Prompt(
                name=prompt["name"],
                description=prompt.get("description"),
                arguments=[
                    types.PromptArgument(
                        name=arg["name"],
                        description=arg.get("description"),
                        required=arg.get("required", False),
                    )
                    for arg in prompt.get("arguments", [])
                ],
            )
            for prompt in prompts
        ]

    @server.get_prompt()
    async def get_prompt(
        name: str, arguments: dict[str, str] | None = None
    ) -> types.GetPromptResult:
        response = await unity_client.send_request(
            {
                "jsonrpc": "2.0",
                "id": f"get_prompt_{name}",
                "method": "prompts/get",
                "params": {"name": name, "arguments": arguments or {}},
            }
        )

        if "error" in response:
            error_payload = response["error"]
            return types.GetPromptResult(
                description=f"Error: {error_payload.get('message', 'Unknown error')}",
                messages=[],
            )

        result = response.get("result", {})
        messages = result.get("messages", [])
        return types.GetPromptResult(
            description=result.get("description"),
            messages=[
                types.PromptMessage(
                    role=message["role"],
                    content=types.TextContent(
                        type="text",
                        text=message.get("content", {}).get("text", ""),
                    ),
                )
                for message in messages
            ],
        )

    @server.list_resources()
    async def list_resources() -> list[types.Resource]:
        response = await unity_client.send_request(
            {
                "jsonrpc": "2.0",
                "id": "list_resources",
                "method": "resources/list",
                "params": {},
            }
        )

        if "error" in response:
            logger.error("Error listing resources: %s", response["error"])
            return []

        result = response.get("result", {})
        resources = result.get("resources", [])
        return [
            types.Resource(
                uri=resource["uri"],
                name=resource.get("name", ""),
                description=resource.get("description"),
                mimeType=resource.get("mimeType"),
            )
            for resource in resources
        ]

    @server.read_resource()
    async def read_resource(uri: AnyUrl) -> str:
        uri_str = str(uri)
        response = await unity_client.send_request(
            {
                "jsonrpc": "2.0",
                "id": f"read_resource_{uri_str}",
                "method": "resources/read",
                "params": {"uri": uri_str},
            }
        )

        if "error" in response:
            error_payload = response["error"]
            return f"Error: {error_payload.get('message', 'Unknown error')}"

        result = response.get("result", {})
        contents = result.get("contents", [])
        if contents and "text" in contents[0]:
            return contents[0]["text"]

        return ""

    return server


async def run_server(request_timeout: float, project_root: Path | None = None):
    """Run the MCP server with file-backed Unity transport."""
    resolved_project_root = (
        get_project_root() if project_root is None else Path(project_root)
    )
    logger.info(
        "Starting Unity Code MCP file STDIO Bridge project_root=%s request_timeout=%s",
        _truncate_for_log(resolved_project_root),
        request_timeout,
    )

    unity_client: UnityFileClient | None = None
    try:
        unity_client = UnityFileClient(
            project_root=resolved_project_root,
            request_timeout=request_timeout,
        )
        server = create_server(unity_client)

        client_to_server_send, client_to_server_recv = create_memory_object_stream[
            SessionMessage | Exception
        ](max_buffer_size=100)
        server_to_client_send, server_to_client_recv = create_memory_object_stream[
            SessionMessage
        ](max_buffer_size=100)

        async def stdin_reader():
            raw_stdin = sys.stdin.buffer
            last_line_text = ""

            def read_line():
                return raw_stdin.readline()

            try:
                while True:
                    line = await anyio.to_thread.run_sync(read_line)  # type: ignore[attr-defined]
                    if not line:
                        logger.info("stdin EOF")
                        break

                    line_text = line.decode("utf-8").strip()
                    if not line_text:
                        continue
                    last_line_text = line_text

                    logger.debug(
                        "stdin line received bytes=%s preview=%s",
                        len(line),
                        _truncate_for_log(line_text),
                    )

                    message = JSONRPCMessage.model_validate_json(line_text)
                    await client_to_server_send.send(SessionMessage(message=message))
            except anyio.ClosedResourceError:
                logger.info("stdin reader stopped after client stream closed")
            except Exception:
                logger.error(
                    "stdin_reader error line_preview=%s",
                    _truncate_for_log(last_line_text) if last_line_text else "<none>",
                    exc_info=True,
                )
            finally:
                await client_to_server_send.aclose()

        async def stdout_writer():
            raw_stdout = sys.stdout.buffer
            last_message_summary = "<none>"

            def write_data(data: bytes):
                raw_stdout.write(data)
                raw_stdout.flush()

            try:
                async for session_msg in server_to_client_recv:
                    json_str = session_msg.message.model_dump_json(
                        by_alias=True, exclude_none=True
                    )
                    last_message_summary = _truncate_for_log(json_str)
                    await anyio.to_thread.run_sync(  # type: ignore[attr-defined]
                        lambda: write_data((json_str + "\n").encode("utf-8"))
                    )
            except anyio.ClosedResourceError:
                logger.info("stdout writer stopped after server stream closed")
            except Exception:
                logger.error(
                    "stdout_writer error last_message=%s",
                    last_message_summary,
                    exc_info=True,
                )

        try:
            async with create_task_group() as tg:
                tg.start_soon(stdin_reader)
                tg.start_soon(stdout_writer)

                init_options = server.create_initialization_options()
                await server.run(
                    client_to_server_recv, server_to_client_send, init_options
                )

                tg.cancel_scope.cancel()
        except* anyio.ClosedResourceError:
            logger.info("Bridge stream closed during shutdown")
    finally:
        if unity_client is not None:
            await unity_client.disconnect(reason="server-shutdown")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="MCP STDIO Bridge for Unity Code MCP Server over files",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument(
        "--request-timeout",
        type=float,
        default=DEFAULT_FILE_REQUEST_TIMEOUT,
        help="Seconds to wait for a Unity file response before failing the request",
    )

    args = parser.parse_args()
    asyncio.run(run_server(args.request_timeout))


if __name__ == "__main__":
    main()
