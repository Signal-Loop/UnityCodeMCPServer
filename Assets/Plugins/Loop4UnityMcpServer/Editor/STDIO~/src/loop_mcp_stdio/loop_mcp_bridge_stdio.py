"""
Loop MCP STDIO Bridge

Bridges MCP protocol over STDIO to Unity TCP Server.
Uses a custom Windows-compatible stdio transport since asyncio stdin
reading doesn't work properly with Windows pipes.
"""

import argparse
import asyncio
import json
import logging
from logging.handlers import RotatingFileHandler
import os
import struct
import sys
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
sys.stderr = open(os.devnull, 'w')

script_dir = os.path.dirname(os.path.abspath(__file__))
log_file_path = os.path.join(script_dir, "loop_mcp_bridge.log")

logger = logging.getLogger("loop-mcp-stdio")
logger.setLevel(logging.INFO)

formatter = logging.Formatter("%(asctime)s - %(levelname)s - %(message)s")


class FlushingHandler(RotatingFileHandler):
    """File handler that flushes immediately after each log message."""
    def emit(self, record):
        super().emit(record)
        self.flush()


file_handler = FlushingHandler(
    log_file_path,
    maxBytes=5 * 1024 * 1024,
    backupCount=3,
    encoding="utf-8"
)
file_handler.setLevel(logging.DEBUG)
file_handler.setFormatter(formatter)
logger.addHandler(file_handler)


class UnityTcpClient:
    """TCP client that connects to Unity MCP Server with retry support."""

    def __init__(self, host: str, port: int, retry_time: float, retry_count: int):
        self.host = host
        self.port = port
        self.retry_time = retry_time
        self.retry_count = retry_count
        self.reader: asyncio.StreamReader | None = None
        self.writer: asyncio.StreamWriter | None = None
        self._lock = asyncio.Lock()

    async def connect(self) -> bool:
        """Connect to Unity TCP Server with retry logic."""
        for attempt in range(self.retry_count):
            try:
                logger.info(f"Connecting to Unity at {self.host}:{self.port} (attempt {attempt + 1}/{self.retry_count})")
                self.reader, self.writer = await asyncio.open_connection(self.host, self.port)
                logger.info(f"Connected to Unity at {self.host}:{self.port}")
                return True
            except (ConnectionRefusedError, OSError) as e:
                logger.warning(f"Connection failed: {e}")
                if attempt < self.retry_count - 1:
                    logger.info(f"Retrying in {self.retry_time} seconds...")
                    await asyncio.sleep(self.retry_time)

        logger.error(f"Failed to connect after {self.retry_count} attempts")
        return False

    async def disconnect(self):
        """Disconnect from Unity TCP Server."""
        if self.writer:
            self.writer.close()
            try:
                await self.writer.wait_closed()
            except Exception:
                pass
            self.writer = None
            self.reader = None
            logger.info("Disconnected from Unity")

    async def send_request(self, request: dict[str, Any]) -> dict[str, Any]:
        """Send a JSON-RPC request to Unity and return the response."""
        last_error = None
        
        for attempt in range(self.retry_count):
            async with self._lock:
                if not self.writer or not self.reader:
                    if not await self.connect():
                        last_error = "Failed to connect"
                        # connect() already retries internally, so if it fails, 
                        # we might as well count this as a failed attempt.
                
                if self.writer and self.reader:
                    try:
                        message = json.dumps(request).encode("utf-8")
                        length_prefix = struct.pack(">I", len(message))
                        self.writer.write(length_prefix + message)
                        await self.writer.drain()

                        logger.debug(f"Sent: {request}")

                        length_data = await self.reader.readexactly(4)
                        response_length = struct.unpack(">I", length_data)[0]
                        response_data = await self.reader.readexactly(response_length)
                        response = json.loads(response_data.decode("utf-8"))

                        logger.debug(f"Received: {response}")
                        return response

                    except (asyncio.IncompleteReadError, ConnectionResetError, BrokenPipeError, ConnectionRefusedError, OSError) as e:
                        logger.warning(f"Connection error during request (attempt {attempt + 1}/{self.retry_count}): {e}")
                        await self.disconnect()
                        last_error = str(e)
                    except Exception as e:
                        logger.error(f"Error sending request: {e}")
                        return {
                            "jsonrpc": "2.0",
                            "id": request.get("id"),
                            "error": {"code": -32603, "message": f"Internal error: {e}"},
                        }

            # Wait before retrying if we haven't exhausted attempts
            if attempt < self.retry_count - 1:
                logger.info(f"Retrying request in {self.retry_time} seconds...")
                await asyncio.sleep(self.retry_time)

        return {
            "jsonrpc": "2.0",
            "id": request.get("id"),
            "error": {
                "code": -32000,
                "message": f"Failed to communicate with Unity after {self.retry_count} attempts. Last error: {last_error}",
            },
        }


def create_server(unity_client: UnityTcpClient) -> Server:
    """Create MCP server that proxies requests to Unity."""
    server = Server("loop-mcp-stdio")

    @server.list_tools()
    async def list_tools() -> list[types.Tool]:
        """List available tools from Unity."""
        response = await unity_client.send_request({
            "jsonrpc": "2.0",
            "id": "list_tools",
            "method": "tools/list",
            "params": {},
        })

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
        response = await unity_client.send_request({
            "jsonrpc": "2.0",
            "id": f"call_tool_{name}",
            "method": "tools/call",
            "params": {"name": name, "arguments": arguments},
        })

        if "error" in response:
            error = response["error"]
            return [types.TextContent(type="text", text=f"Error: {error.get('message', 'Unknown error')}")]

        result = response.get("result", {})
        content = result.get("content", [])

        mcp_content: list[types.TextContent | types.ImageContent | types.EmbeddedResource] = []
        for item in content:
            item_type = item.get("type")
            if item_type == "text":
                mcp_content.append(types.TextContent(type="text", text=item.get("text", "")))
            elif item_type == "image":
                mcp_content.append(types.ImageContent(
                    type="image",
                    data=item.get("data", ""),
                    mimeType=item.get("mimeType", "image/png"),
                ))
            elif item_type == "resource":
                resource = item.get("resource", {})
                mcp_content.append(types.EmbeddedResource(
                    type="resource",
                    resource=types.TextResourceContents(
                        uri=resource.get("uri", ""),
                        mimeType=resource.get("mimeType"),
                        text=resource.get("text", ""),
                    ),
                ))

        return mcp_content if mcp_content else [types.TextContent(type="text", text="No content returned")]

    @server.list_prompts()
    async def list_prompts() -> list[types.Prompt]:
        """List available prompts from Unity."""
        response = await unity_client.send_request({
            "jsonrpc": "2.0",
            "id": "list_prompts",
            "method": "prompts/list",
            "params": {},
        })

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
    async def get_prompt(name: str, arguments: dict[str, str] | None = None) -> types.GetPromptResult:
        """Get a prompt from Unity."""
        response = await unity_client.send_request({
            "jsonrpc": "2.0",
            "id": f"get_prompt_{name}",
            "method": "prompts/get",
            "params": {"name": name, "arguments": arguments or {}},
        })

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
                    content=types.TextContent(type="text", text=msg.get("content", {}).get("text", "")),
                )
                for msg in messages
            ],
        )

    @server.list_resources()
    async def list_resources() -> list[types.Resource]:
        """List available resources from Unity."""
        response = await unity_client.send_request({
            "jsonrpc": "2.0",
            "id": "list_resources",
            "method": "resources/list",
            "params": {},
        })

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
        response = await unity_client.send_request({
            "jsonrpc": "2.0",
            "id": f"read_resource_{uri_str}",
            "method": "resources/read",
            "params": {"uri": uri_str},
        })

        if "error" in response:
            error = response["error"]
            return f"Error: {error.get('message', 'Unknown error')}"

        result = response.get("result", {})
        contents = result.get("contents", [])

        if contents and "text" in contents[0]:
            return contents[0]["text"]

        return ""

    return server


async def run_server(host: str, port: int, retry_time: float, retry_count: int):
    """Run the MCP server with Windows-compatible stdio transport."""
    logger.info(f"Starting Loop MCP STDIO Bridge (Unity at {host}:{port})")
    
    unity_client: UnityTcpClient | None = None
    try:
        unity_client = UnityTcpClient(host, port, retry_time, retry_count)
        server = create_server(unity_client)

        # Create memory streams for the server
        client_to_server_send, client_to_server_recv = create_memory_object_stream[SessionMessage | Exception](max_buffer_size=100)
        server_to_client_send, server_to_client_recv = create_memory_object_stream[SessionMessage](max_buffer_size=100)

        async def stdin_reader():
            """Read JSON-RPC messages from stdin using thread pool."""
            raw_stdin = sys.stdin.buffer

            def read_line():
                return raw_stdin.readline()

            try:
                while True:
                    line = await anyio.to_thread.run_sync(read_line)  # type: ignore[attr-defined]
                    if not line:
                        logger.info("stdin EOF")
                        break

                    line_text = line.decode('utf-8').strip()
                    if not line_text:
                        continue

                    message = JSONRPCMessage.model_validate_json(line_text)
                    await client_to_server_send.send(SessionMessage(message=message))

            except Exception as e:
                logger.error(f"stdin_reader error: {e}")
            finally:
                await client_to_server_send.aclose()

        async def stdout_writer():
            """Write JSON-RPC messages to stdout using thread pool."""
            raw_stdout = sys.stdout.buffer

            def write_data(data: bytes):
                raw_stdout.write(data)
                raw_stdout.flush()

            try:
                async for session_msg in server_to_client_recv:
                    json_str = session_msg.message.model_dump_json(by_alias=True, exclude_none=True)
                    await anyio.to_thread.run_sync(lambda: write_data((json_str + "\n").encode('utf-8')))  # type: ignore[attr-defined]
            except Exception as e:
                logger.error(f"stdout_writer error: {e}")

        async with create_task_group() as tg:
            tg.start_soon(stdin_reader)
            tg.start_soon(stdout_writer)

            init_options = server.create_initialization_options()
            await server.run(client_to_server_recv, server_to_client_send, init_options)

            tg.cancel_scope.cancel()

    except Exception as e:
        logger.error(f"Server error: {e}", exc_info=True)
        raise
    finally:
        if unity_client:
            await unity_client.disconnect()


def main():
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="MCP STDIO Bridge for Unity Loop Server",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument("--host", default="localhost", help="Unity TCP Server host")
    parser.add_argument("--port", type=int, default=21088, help="Unity TCP Server port")
    parser.add_argument("--retry-time", type=float, default=2.0, help="Seconds between connection retries")
    parser.add_argument("--retry-count", type=int, default=15, help="Maximum number of connection retries")
    parser.add_argument("--verbose", action="store_true", help="Enable verbose logging")
    parser.add_argument("--quiet", action="store_true", help="Suppress logging")

    args = parser.parse_args()

    if args.quiet:
        logger.setLevel(logging.WARNING)
    elif args.verbose:
        logger.setLevel(logging.DEBUG)

    logger.info(f"Loop MCP STDIO Bridge starting (Unity at {args.host}:{args.port})")
    asyncio.run(run_server(args.host, args.port, args.retry_time, args.retry_count))


if __name__ == "__main__":
    main()
