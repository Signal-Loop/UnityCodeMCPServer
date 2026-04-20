"""
Unity Code MCP STDIO Bridge

Bridges MCP protocol over STDIO to Unity TCP Server.
Uses a custom Windows-compatible stdio transport since asyncio stdin
reading doesn't work properly with Windows pipes.
"""

import argparse
import asyncio
import json
import itertools
import logging
from logging.handlers import RotatingFileHandler
import os
from pathlib import Path
import struct
import sys
import time
from typing import Any

import anyio
from anyio import create_memory_object_stream, create_task_group
from mcp.server import Server
from mcp import types
from mcp.types import JSONRPCMessage
from mcp.shared.message import SessionMessage
from pydantic import AnyUrl

# Configure logging to file only (STDIO protocol uses stdout for messages)
# Redirect stderr to devnull to prevent any output that could corrupt JSON-RPC
sys.stderr = open(os.devnull, "w")

script_dir = os.path.dirname(os.path.abspath(__file__))
log_file_path = os.path.join(script_dir, "unity_code_mcp_bridge.log")

logger = logging.getLogger("unity-code-mcp-stdio")
logger.setLevel(logging.INFO)
logger.propagate = False

LOG_MAX_BYTES = 5 * 1024 * 1024
LOG_BACKUP_COUNT = 3
LOG_VALUE_PREVIEW_LIMIT = 160
DEFAULT_REQUEST_TIMEOUT = 120.0
REQUEST_UNAVAILABLE_ERROR_CODE = -32000
RETRY_GUIDANCE = (
    "Safe next step: wait for Unity to finish domain reload or script compilation, "
    "then retry the same MCP request."
)

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


def _describe_request(request: dict[str, Any]) -> str:
    """Summarize a JSON-RPC request for diagnostic logging."""
    params = request.get("params")
    fragments = [
        f"id={request.get('id', 'unknown')}",
        f"method={request.get('method', 'unknown')}",
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

    error = response.get("error")
    if isinstance(error, dict):
        fragments.append(f"error_code={error.get('code', 'unknown')}")
        fragments.append(
            f"error_message={_truncate_for_log(error.get('message', 'Unknown error'))}"
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


# ---------------------------------------------------------------------------
# Settings discovery
# ---------------------------------------------------------------------------

DEFAULT_PORT: int = 21088
"""Fallback TCP port used when the settings file cannot be found or read."""

# Fixed path to the settings asset: this script lives at
#   <project>/Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~/src/unity_code_mcp_stdio/
# The settings asset is always at
#   <project>/Assets/Plugins/UnityCodeMcpServer/Editor/UnityCodeMcpServerSettings.asset
# which is exactly 4 parent directories up from this file.
_SETTINGS_FILE: Path = (
    Path(__file__).parent.parent.parent.parent / "UnityCodeMcpServerSettings.asset"
)
"""Absolute path to the Unity settings asset derived from this module's location."""


def read_port_from_settings(settings_file: Path) -> int | None:
    """Parse the TCP port from a Unity settings asset file.

    Looks for a YAML-style line of the form ``StdioPort: <number>``.

    Args:
        settings_file: Path to the ``UnityCodeMcpServerSettings.asset`` file.

    Returns:
        Port number, or ``None`` if the file cannot be read or parsed.
    """
    try:
        content = settings_file.read_text(encoding="utf-8")
    except OSError as exc:
        logger.warning(f"Could not read settings file '{settings_file}': {exc}")
        return None

    for line in content.splitlines():
        stripped = line.strip()
        if stripped.startswith("StdioPort:"):
            _, _, raw = stripped.partition(":")
            try:
                return int(raw.strip())
            except ValueError:
                logger.warning(f"Invalid port value in settings: '{stripped}'")
                return None

    logger.warning(f"'StdioPort' key not found in settings file: {settings_file}")
    return None


def get_stdio_port(_settings_file: Path | None = None) -> int:
    """Resolve the TCP port from Unity project settings.

    Reads ``StdioPort`` from the settings asset at the fixed path
    :data:`_SETTINGS_FILE`. Falls back to :data:`DEFAULT_PORT` if the file
    is absent or the port cannot be parsed.

    This function is safe to call on every request: file reads are small and
    cheap, and calling it repeatedly allows the port to reflect runtime
    changes made inside the Unity Editor.

    Args:
        _settings_file: Override the settings file path. Intended for testing
            only; production code should rely on the default fixed path.

    Returns:
        TCP port number.
    """
    settings_file = _SETTINGS_FILE if _settings_file is None else _settings_file
    if not settings_file.is_file():
        logger.info(
            f"Settings file not found at '{settings_file}'. "
            f"Using default port {DEFAULT_PORT}."
        )
        return DEFAULT_PORT

    port = read_port_from_settings(settings_file)
    if port is None:
        logger.info(
            f"Could not read port from '{settings_file}'. "
            f"Using default port {DEFAULT_PORT}."
        )
        return DEFAULT_PORT

    logger.debug(f"Using port {port} from '{settings_file}'.")
    return port


# ---------------------------------------------------------------------------
# TCP client
# ---------------------------------------------------------------------------


UNITY_HOST: str = "localhost"
"""Default Unity TCP Server host. Unity never listens on remote interfaces."""


class UnityRequestTimeoutError(asyncio.TimeoutError):
    """Raised when a Unity request phase exceeds the configured timeout."""

    def __init__(self, phase: str):
        super().__init__(phase)
        self.phase = phase


class UnityTcpClient:
    """TCP client that connects to the Unity MCP Server."""

    HEALTH_PROBE_TIMEOUT = 5.0
    """Seconds to wait for a ping response when verifying a new connection."""

    CONNECT_TIMEOUT = 5.0
    """Seconds to wait for a single TCP connect attempt on the hot path."""

    def __init__(
        self,
        host: str,
        port: int,
        retry_time: float,
        retry_count: int,
        port_resolver: Any = None,
        request_timeout: float = DEFAULT_REQUEST_TIMEOUT,
    ):
        self.host = host
        self.port = port
        self.retry_time = retry_time
        self.retry_count = retry_count
        self._port_resolver = port_resolver
        self.request_timeout = request_timeout
        self.reader: asyncio.StreamReader | None = None
        self.writer: asyncio.StreamWriter | None = None
        self._lock = asyncio.Lock()
        self._connection_verified = False
        self._connected_at: float | None = None
        self._last_request_completed_at: float | None = None

    @staticmethod
    def _remaining_time(deadline: float) -> float:
        """Return the remaining wall-clock time for the active request."""
        return max(0.0, deadline - time.perf_counter())

    @classmethod
    def _should_retry(cls, attempt: int, retry_count: int, deadline: float) -> bool:
        """Return whether another bounded recovery attempt is still allowed."""
        return attempt < retry_count and cls._remaining_time(deadline) > 0

    @staticmethod
    def _build_retryable_error_message(last_failure: str) -> str:
        """Build an actionable exhaustion error for MCP clients and agents."""
        return (
            "Unity was unavailable long enough that the bridge stopped retrying. "
            f"Last observed failure: {last_failure}. {RETRY_GUIDANCE}"
        )

    async def _sleep_before_retry(
        self,
        *,
        trace_id: str,
        request_summary: str,
        attempt: int,
        deadline: float,
        reason: str,
    ) -> None:
        """Pause briefly before the next retry without exceeding the request deadline."""
        if self.retry_time <= 0:
            return

        delay_seconds = min(self.retry_time, self._remaining_time(deadline))
        if delay_seconds <= 0:
            return

        logger.info(
            "trace=%s retrying Unity request attempt=%s reason=%s sleep_s=%.3f %s",
            trace_id,
            attempt,
            reason,
            delay_seconds,
            request_summary,
        )
        await asyncio.sleep(delay_seconds)

    async def _refresh_port(self, trace_id: str, request_summary: str) -> None:
        """Reconnect when Unity settings change the TCP port."""
        if self._port_resolver is None:
            return

        current_port = self._port_resolver()
        if current_port == self.port:
            return

        logger.info(
            "trace=%s %s port changed from %s to %s; reconnecting",
            trace_id,
            request_summary,
            self.port,
            current_port,
        )
        await self.disconnect(reason="port-change")
        self.port = current_port

    async def _open_connection_for_request(
        self,
        *,
        trace_id: str,
        request_summary: str,
        deadline: float,
    ) -> bool:
        """Open one TCP connection attempt without exceeding the request deadline."""
        timeout_seconds = min(self.CONNECT_TIMEOUT, self._remaining_time(deadline))
        if timeout_seconds <= 0:
            return False

        try:
            logger.info(
                "trace=%s Connecting to Unity at %s:%s timeout_s=%.3f %s",
                trace_id,
                self.host,
                self.port,
                timeout_seconds,
                request_summary,
            )
            self.reader, self.writer = await asyncio.wait_for(
                asyncio.open_connection(self.host, self.port),
                timeout=timeout_seconds,
            )
            self._connection_verified = False
            self._connected_at = time.perf_counter()
            self._last_request_completed_at = None
            logger.info(
                "trace=%s Connected to Unity at %s:%s %s",
                trace_id,
                self.host,
                self.port,
                request_summary,
            )
            return True
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError) as e:
            logger.warning(
                "trace=%s Unity connect failed host=%s port=%s error_type=%s error=%s %s",
                trace_id,
                self.host,
                self.port,
                type(e).__name__,
                e,
                request_summary,
            )
            self.reader = None
            self.writer = None
            self._connection_verified = False
            self._connected_at = None
            self._last_request_completed_at = None
            return False

    async def _await_request_phase(
        self,
        awaitable,
        *,
        trace_id: str,
        request_summary: str,
        phase: str,
        started_at: float,
        timeout_seconds: float,
    ):
        """Await one request phase with a terminal timeout."""
        if timeout_seconds <= 0:
            raise UnityRequestTimeoutError(phase)

        try:
            return await asyncio.wait_for(awaitable, timeout=timeout_seconds)
        except asyncio.TimeoutError as exc:
            duration_ms = round((time.perf_counter() - started_at) * 1000)
            logger.warning(
                "trace=%s Unity request timeout %s phase=%s duration_ms=%s timeout_s=%s",
                trace_id,
                request_summary,
                phase,
                duration_ms,
                timeout_seconds,
            )
            raise UnityRequestTimeoutError(phase) from exc

    async def disconnect(self, reason: str = "requested"):
        """Disconnect from Unity TCP Server."""
        writer = self.writer
        self.writer = None
        self.reader = None
        self._connection_verified = False
        self._connected_at = None
        self._last_request_completed_at = None

        if writer:
            writer.close()
            try:
                await writer.wait_closed()
            except Exception:
                pass
        logger.info("Disconnected from Unity reason=%s", reason)

    async def _health_probe(self, trace_id: str, deadline: float) -> bool:
        """Send a ``ping`` to verify Unity is actually responsive.

        The Unity TCP server handles ``ping`` without switching to the main
        thread, so this succeeds even during editor initialization.  A short
        timeout (5 s) means we detect a dead-but-listening server quickly
        instead of waiting the full 30 s request timeout.
        """
        probe = {
            "jsonrpc": "2.0",
            "id": f"health_{trace_id}",
            "method": "ping",
            "params": {},
        }
        try:
            if self.writer is None or self.reader is None:
                return False

            writer = self.writer
            reader = self.reader
            message = json.dumps(probe).encode("utf-8")
            length_prefix = struct.pack(">I", len(message))
            writer.write(length_prefix + message)
            probe_timeout_seconds = min(
                self.HEALTH_PROBE_TIMEOUT, self._remaining_time(deadline)
            )
            if probe_timeout_seconds <= 0:
                return False

            await asyncio.wait_for(writer.drain(), timeout=probe_timeout_seconds)

            length_data = await asyncio.wait_for(
                reader.readexactly(4), timeout=probe_timeout_seconds
            )
            response_length = struct.unpack(">I", length_data)[0]
            await asyncio.wait_for(
                reader.readexactly(response_length),
                timeout=probe_timeout_seconds,
            )

            logger.info("trace=%s Health probe passed", trace_id)
            return True
        except Exception as e:
            logger.warning(
                "trace=%s Health probe failed error_type=%s error=%s",
                trace_id,
                type(e).__name__,
                e,
            )
            return False

    @staticmethod
    def _build_error(
        request: dict[str, Any], code: int, message: str
    ) -> dict[str, Any]:
        """Build a JSON-RPC error response."""
        return {
            "jsonrpc": "2.0",
            "id": request.get("id"),
            "error": {"code": code, "message": message},
        }

    async def send_request(self, request: dict[str, Any]) -> dict[str, Any]:
        """Send a JSON-RPC request to Unity and return the response.

        The port is resolved fresh before every attempt so runtime settings
        changes and transient Unity reload windows can be recovered in-place.

        The request is bounded by a single wall-clock deadline so the bridge
        either returns a successful result or an actionable error without
        leaving the caller stuck waiting forever.
        """
        trace_id = _next_request_trace_id()
        request_summary = _describe_request(request)
        started_at = time.perf_counter()

        logger.info(
            "trace=%s Unity request started %s connection_state=%s",
            trace_id,
            request_summary,
            "connected" if self.writer and self.reader else "disconnected",
        )

        async with self._lock:
            deadline = started_at + self.request_timeout
            last_failure = "Unity did not become ready before the retry window expired"

            for attempt in range(1, self.retry_count + 1):
                await self._refresh_port(trace_id, request_summary)

                logger.info(
                    "trace=%s Unity request attempt=%s/%s remaining_ms=%s %s",
                    trace_id,
                    attempt,
                    self.retry_count,
                    round(self._remaining_time(deadline) * 1000),
                    request_summary,
                )

                if not self.writer or not self.reader:
                    connected = await self._open_connection_for_request(
                        trace_id=trace_id,
                        request_summary=request_summary,
                        deadline=deadline,
                    )
                    if not connected:
                        last_failure = "Unity TCP server was not accepting connections"
                        if not self._should_retry(attempt, self.retry_count, deadline):
                            break
                        await self._sleep_before_retry(
                            trace_id=trace_id,
                            request_summary=request_summary,
                            attempt=attempt,
                            deadline=deadline,
                            reason="connect-failed",
                        )
                        continue

                if not self._connection_verified:
                    if not await self._health_probe(trace_id, deadline):
                        last_failure = "Unity TCP server accepted the connection but did not respond to ping"
                        await self.disconnect(reason="health-probe-failed")
                        if not self._should_retry(attempt, self.retry_count, deadline):
                            break
                        await self._sleep_before_retry(
                            trace_id=trace_id,
                            request_summary=request_summary,
                            attempt=attempt,
                            deadline=deadline,
                            reason="health-probe-failed",
                        )
                        continue

                    self._connection_verified = True

                try:
                    if self.writer is None or self.reader is None:
                        raise ConnectionResetError("Unity socket is not available")

                    writer = self.writer
                    reader = self.reader
                    message = json.dumps(request).encode("utf-8")
                    length_prefix = struct.pack(">I", len(message))
                    writer.write(length_prefix + message)
                    await self._await_request_phase(
                        writer.drain(),
                        trace_id=trace_id,
                        request_summary=request_summary,
                        phase="write",
                        started_at=started_at,
                        timeout_seconds=self._remaining_time(deadline),
                    )

                    logger.debug(
                        "trace=%s Sent Unity request bytes=%s %s",
                        trace_id,
                        len(message),
                        request_summary,
                    )

                    length_data = await self._await_request_phase(
                        reader.readexactly(4),
                        trace_id=trace_id,
                        request_summary=request_summary,
                        phase="response-length",
                        started_at=started_at,
                        timeout_seconds=self._remaining_time(deadline),
                    )
                    response_length = struct.unpack(">I", length_data)[0]
                    response_data = await self._await_request_phase(
                        reader.readexactly(response_length),
                        trace_id=trace_id,
                        request_summary=request_summary,
                        phase="response-body",
                        started_at=started_at,
                        timeout_seconds=self._remaining_time(deadline),
                    )
                    response = json.loads(response_data.decode("utf-8"))

                    self._last_request_completed_at = time.perf_counter()
                    duration_ms = round((time.perf_counter() - started_at) * 1000)
                    response_summary = _describe_response(response)
                    logger.info(
                        "trace=%s Unity request completed %s duration_ms=%s response=%s",
                        trace_id,
                        request_summary,
                        duration_ms,
                        response_summary,
                    )
                    return response

                except UnityRequestTimeoutError as e:
                    last_failure = f"Unity request timed out during {e.phase} after {self.request_timeout}s"
                    await self.disconnect(reason=f"request-timeout:{e.phase}")
                except (
                    asyncio.IncompleteReadError,
                    ConnectionResetError,
                    BrokenPipeError,
                    ConnectionRefusedError,
                    OSError,
                ) as e:
                    duration_ms = round((time.perf_counter() - started_at) * 1000)
                    now = time.perf_counter()
                    connection_age_ms = (
                        round((now - self._connected_at) * 1000)
                        if self._connected_at is not None
                        else None
                    )
                    idle_ms = (
                        round((now - self._last_request_completed_at) * 1000)
                        if self._last_request_completed_at is not None
                        else None
                    )
                    logger.warning(
                        "trace=%s Unity request transport error %s duration_ms=%s "
                        "error_type=%s error=%s connection_age_ms=%s idle_ms=%s",
                        trace_id,
                        request_summary,
                        duration_ms,
                        type(e).__name__,
                        e,
                        connection_age_ms,
                        idle_ms,
                        exc_info=True,
                    )
                    last_failure = (
                        f"Unity connection dropped during request. Last error: {e}"
                    )
                    await self.disconnect(reason=f"request-error:{type(e).__name__}")
                except Exception as e:
                    duration_ms = round((time.perf_counter() - started_at) * 1000)
                    logger.error(
                        "trace=%s Unity request failed unexpectedly %s duration_ms=%s",
                        trace_id,
                        request_summary,
                        duration_ms,
                        exc_info=True,
                    )
                    await self.disconnect(reason="unexpected-error")
                    return self._build_error(request, -32603, f"Internal error: {e}")

                if not self._should_retry(attempt, self.retry_count, deadline):
                    break

                await self._sleep_before_retry(
                    trace_id=trace_id,
                    request_summary=request_summary,
                    attempt=attempt,
                    deadline=deadline,
                    reason="request-retry",
                )

            duration_ms = round((time.perf_counter() - started_at) * 1000)
            logger.warning(
                "trace=%s Unity request exhausted retries %s duration_ms=%s last_failure=%s",
                trace_id,
                request_summary,
                duration_ms,
                last_failure,
            )
            return self._build_error(
                request,
                REQUEST_UNAVAILABLE_ERROR_CODE,
                self._build_retryable_error_message(last_failure),
            )


class SafeServer(Server):
    """Server variant that treats closed client streams as expected teardown."""

    async def _handle_request(
        self,
        message,
        req,
        session,
        lifespan_context,
        raise_exceptions,
    ):
        try:
            await super()._handle_request(
                message,
                req,
                session,
                lifespan_context,
                raise_exceptions,
            )
        except anyio.ClosedResourceError:
            logger.info(
                "Client stream closed before response could be sent for request %s",
                getattr(message, "request_id", "unknown"),
            )


def _convert_resource_contents(
    resource: dict[str, Any],
) -> types.TextResourceContents:
    """Convert Unity resource payload to an MCP TextResourceContents object.

    Blob payloads are not supported by the protocol and are ignored.
    """
    # Ignore any `blob` payloads — always map to TextResourceContents.
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


def create_server(unity_client: UnityTcpClient) -> Server:
    """Create MCP server that proxies requests to Unity."""
    server = SafeServer("unity-code-mcp-stdio")

    @server.list_tools()
    async def list_tools() -> list[types.Tool]:
        """List available tools from Unity."""
        response = await unity_client.send_request(
            {
                "jsonrpc": "2.0",
                "id": "list_tools",
                "method": "tools/list",
                "params": {},
            }
        )

        if "error" in response:
            logger.error(f"Error listing tools: {response['error']}")
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
    async def call_tool(
        name: str, arguments: dict[str, Any]
    ) -> list[types.TextContent | types.ImageContent | types.EmbeddedResource]:
        """Call a tool in Unity."""
        response = await unity_client.send_request(
            {
                "jsonrpc": "2.0",
                "id": f"call_tool_{name}",
                "method": "tools/call",
                "params": {"name": name, "arguments": arguments},
            }
        )

        if "error" in response:
            error = response["error"]
            return [
                types.TextContent(
                    type="text", text=f"Error: {error.get('message', 'Unknown error')}"
                )
            ]

        result = response.get("result", {})
        content = result.get("content", [])

        mcp_content: list[
            types.TextContent | types.ImageContent | types.EmbeddedResource
        ] = []
        for item in content:
            converted = _convert_content_item(item)
            if converted is not None:
                mcp_content.append(converted)

        return (
            mcp_content
            if mcp_content
            else [types.TextContent(type="text", text="No content returned")]
        )

    @server.list_prompts()
    async def list_prompts() -> list[types.Prompt]:
        """List available prompts from Unity."""
        response = await unity_client.send_request(
            {
                "jsonrpc": "2.0",
                "id": "list_prompts",
                "method": "prompts/list",
                "params": {},
            }
        )

        if "error" in response:
            logger.error(f"Error listing prompts: {response['error']}")
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
        """Get a prompt from Unity."""
        response = await unity_client.send_request(
            {
                "jsonrpc": "2.0",
                "id": f"get_prompt_{name}",
                "method": "prompts/get",
                "params": {"name": name, "arguments": arguments or {}},
            }
        )

        if "error" in response:
            error = response["error"]
            return types.GetPromptResult(
                description=f"Error: {error.get('message', 'Unknown error')}",
                messages=[],
            )

        result = response.get("result", {})
        messages = result.get("messages", [])

        return types.GetPromptResult(
            description=result.get("description"),
            messages=[
                types.PromptMessage(
                    role=msg["role"],
                    content=types.TextContent(
                        type="text", text=msg.get("content", {}).get("text", "")
                    ),
                )
                for msg in messages
            ],
        )

    @server.list_resources()
    async def list_resources() -> list[types.Resource]:
        """List available resources from Unity."""
        response = await unity_client.send_request(
            {
                "jsonrpc": "2.0",
                "id": "list_resources",
                "method": "resources/list",
                "params": {},
            }
        )

        if "error" in response:
            logger.error(f"Error listing resources: {response['error']}")
            return []

        result = response.get("result", {})
        resources = result.get("resources", [])

        return [
            types.Resource(
                uri=res["uri"],
                name=res.get("name", ""),
                description=res.get("description"),
                mimeType=res.get("mimeType"),
            )
            for res in resources
        ]

    @server.read_resource()
    async def read_resource(uri: AnyUrl) -> str:
        """Read a resource from Unity."""
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
            error = response["error"]
            return f"Error: {error.get('message', 'Unknown error')}"

        result = response.get("result", {})
        contents = result.get("contents", [])

        if contents and "text" in contents[0]:
            return contents[0]["text"]

        # `blob` is not supported by the protocol — ignore it and return empty string
        return ""

    return server


async def run_server(
    host: str,
    port: int,
    retry_time: float,
    retry_count: int,
    request_timeout: float,
):
    """Run the MCP server with Windows-compatible stdio transport."""
    logger.info(
        "Starting Unity Code MCP STDIO Bridge host=%s port=%s retry_time=%s retry_count=%s request_timeout=%s log_path=%s max_bytes=%s backups=%s",
        host,
        port,
        retry_time,
        retry_count,
        request_timeout,
        log_file_path,
        LOG_MAX_BYTES,
        LOG_BACKUP_COUNT,
    )

    unity_client: UnityTcpClient | None = None
    try:
        unity_client = UnityTcpClient(
            host,
            port,
            retry_time,
            retry_count,
            port_resolver=get_stdio_port,
            request_timeout=request_timeout,
        )
        server = create_server(unity_client)

        # Create memory streams for the server
        client_to_server_send, client_to_server_recv = create_memory_object_stream[
            SessionMessage | Exception
        ](max_buffer_size=100)
        server_to_client_send, server_to_client_recv = create_memory_object_stream[
            SessionMessage
        ](max_buffer_size=100)

        async def stdin_reader():
            """Read JSON-RPC messages from stdin using thread pool."""
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
            """Write JSON-RPC messages to stdout using thread pool."""
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

    except Exception as e:
        logger.error(f"Server error: {e}", exc_info=True)
        raise
    finally:
        if unity_client:
            await unity_client.disconnect(reason="server-shutdown")


def main():
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="MCP STDIO Bridge for Unity Code MCP Server",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument(
        "--retry-time",
        type=float,
        default=2.0,
        help="Seconds between connection retries",
    )
    parser.add_argument(
        "--retry-count",
        type=int,
        default=15,
        help="Maximum number of connection retries",
    )
    parser.add_argument(
        "--request-timeout",
        type=float,
        default=DEFAULT_REQUEST_TIMEOUT,
        help="Seconds to wait for each Unity request phase before failing the request",
    )
    parser.add_argument("--verbose", action="store_true", help="Enable verbose logging")
    parser.add_argument("--quiet", action="store_true", help="Suppress logging")

    args = parser.parse_args()

    if args.quiet:
        logger.setLevel(logging.WARNING)
    elif args.verbose:
        logger.setLevel(logging.DEBUG)

    host = UNITY_HOST
    port = get_stdio_port()
    logger.info(
        "Unity Code MCP STDIO Bridge starting (Unity at %s:%s, request_timeout=%s)",
        host,
        port,
        args.request_timeout,
    )
    asyncio.run(
        run_server(
            host,
            port,
            args.retry_time,
            args.retry_count,
            args.request_timeout,
        )
    )


if __name__ == "__main__":
    main()
