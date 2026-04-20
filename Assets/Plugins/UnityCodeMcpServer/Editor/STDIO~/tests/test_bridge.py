"""
Tests for the Unity Code MCP STDIO Bridge.

Running Tests
-------------
From the STDIO~ directory:

    uv run --extra dev pytest tests/ -v

Windows (if uv raises "Failed to canonicalize script path"):

    .venv\\Scripts\\python.exe -m pytest tests/ -v
"""

import asyncio
import json
import logging
import struct
import time
from pathlib import Path
from unittest.mock import AsyncMock, patch

import anyio
import pytest
from mcp import types

# Import the module under test
from unity_code_mcp_stdio import UnityTcpClient
from unity_code_mcp_stdio.unity_code_mcp_bridge_stdio import (
    DEFAULT_REQUEST_TIMEOUT,
    DEFAULT_PORT,
    LOG_BACKUP_COUNT,
    LOG_MAX_BYTES,
    _SETTINGS_FILE,
    _build_rotating_handler,
    _convert_content_item,
    _convert_resource_contents,
    _describe_request,
    _describe_response,
    create_server,
    get_stdio_port,
    read_port_from_settings,
)


class MockStreamReader:
    """Mock asyncio StreamReader for testing."""

    def __init__(self):
        self.data = b""
        self.pos = 0

    def set_response(self, response: dict):
        """Set the response that will be returned."""
        response_bytes = json.dumps(response).encode("utf-8")
        length_prefix = struct.pack(">I", len(response_bytes))
        self.data = length_prefix + response_bytes
        self.pos = 0

    async def readexactly(self, n: int) -> bytes:
        """Read exactly n bytes."""
        result = self.data[self.pos : self.pos + n]
        self.pos += n
        if len(result) < n:
            raise asyncio.IncompleteReadError(result, n)
        return result


class MockStreamWriter:
    """Mock asyncio StreamWriter for testing."""

    def __init__(self):
        self.data = b""
        self.closed = False

    def write(self, data: bytes):
        """Write data."""
        self.data += data

    async def drain(self):
        """Drain the buffer."""
        pass

    def close(self):
        """Close the writer."""
        self.closed = True

    async def wait_closed(self):
        """Wait for close to complete."""
        pass


class ClosedStreamRequestResponder:
    """Minimal request responder that simulates a closed client write stream."""

    def __init__(self, request_id="test-request"):
        self.request_id = request_id
        self.request_meta = None
        self.message_metadata = None

    async def respond(self, response):
        raise anyio.ClosedResourceError()


@pytest.fixture
def unity_client():
    """Create a UnityTcpClient for testing."""
    return UnityTcpClient(
        host="localhost",
        port=21088,
        retry_time=0.1,
        retry_count=2,
    )


class TestUnityTcpClient:
    """Tests for UnityTcpClient."""

    @pytest.mark.asyncio
    async def test_open_connection_for_request_success(self, unity_client):
        """A request-scoped connection attempt should populate the active socket."""
        mock_reader = MockStreamReader()
        mock_writer = MockStreamWriter()

        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.return_value = (mock_reader, mock_writer)

            result = await unity_client._open_connection_for_request(
                trace_id="test-trace",
                request_summary="id=test method=tools/list",
                deadline=time.perf_counter() + 5,
            )

            assert result is True
            assert unity_client.reader is mock_reader
            assert unity_client.writer is mock_writer

    @pytest.mark.asyncio
    async def test_open_connection_for_request_uses_configured_host_and_port(self):
        """The request-scoped connect path should use the configured endpoint."""
        client = UnityTcpClient(
            host="127.0.0.1",
            port=12345,
            retry_time=0.0,
            retry_count=1,
        )

        mock_reader = MockStreamReader()
        mock_writer = MockStreamWriter()

        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.return_value = (mock_reader, mock_writer)
            await client._open_connection_for_request(
                trace_id="test-trace",
                request_summary="id=test method=tools/list",
                deadline=time.perf_counter() + 5,
            )

        args = mock_open.call_args[0]
        assert args[0] == "127.0.0.1"
        assert args[1] == 12345

    @pytest.mark.asyncio
    async def test_open_connection_for_request_returns_false_on_failure(
        self, unity_client
    ):
        """A failed request-scoped connect should return False and clear the socket."""
        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.side_effect = ConnectionRefusedError("Connection refused")

            result = await unity_client._open_connection_for_request(
                trace_id="test-trace",
                request_summary="id=test method=tools/list",
                deadline=time.perf_counter() + 5,
            )

            assert result is False
            assert mock_open.call_count == 1
            assert unity_client.reader is None
            assert unity_client.writer is None

    @pytest.mark.asyncio
    async def test_disconnect(self, unity_client):
        """Test disconnection."""
        mock_writer = MockStreamWriter()
        unity_client.writer = mock_writer
        unity_client.reader = MockStreamReader()

        await unity_client.disconnect()

        assert mock_writer.closed is True
        assert unity_client.writer is None
        assert unity_client.reader is None

    @pytest.mark.asyncio
    async def test_send_request_success(self, unity_client):
        """Test successful request/response."""
        mock_reader = MockStreamReader()
        mock_writer = MockStreamWriter()

        expected_response = {
            "jsonrpc": "2.0",
            "id": "test",
            "result": {"tools": []},
        }
        mock_reader.set_response(expected_response)

        unity_client.reader = mock_reader
        unity_client.writer = mock_writer
        unity_client._connection_verified = True

        request = {
            "jsonrpc": "2.0",
            "id": "test",
            "method": "tools/list",
            "params": {},
        }

        response = await unity_client.send_request(request)

        assert response == expected_response

    @pytest.mark.asyncio
    async def test_send_request_connection_error_retries_success(self, unity_client):
        """Test request retry success when the initial connect path fails."""
        mock_reader_success = MockStreamReader()
        expected_response = {
            "jsonrpc": "2.0",
            "id": "test",
            "result": {"success": True},
        }
        mock_reader_success.set_response(expected_response)

        mock_writer_success = MockStreamWriter()
        unity_client._health_probe = AsyncMock(return_value=True)

        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.side_effect = [
                ConnectionRefusedError("Connection refused"),
                (mock_reader_success, mock_writer_success),
            ]

            request = {
                "jsonrpc": "2.0",
                "id": "test",
                "method": "test_method",
            }

            response = await unity_client.send_request(request)

            assert response == expected_response
            assert mock_open.call_count == 2

    @pytest.mark.asyncio
    async def test_send_request_retry_exhausted(self, unity_client):
        """Test request retry exhaustion when Unity never accepts a connection."""
        unity_client.retry_count = 2
        unity_client.retry_time = 0.01

        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.side_effect = ConnectionRefusedError("Always fails")

            request = {"jsonrpc": "2.0", "id": "test", "method": "test"}
            response = await unity_client.send_request(request)

            assert "error" in response
            assert response["error"]["code"] == -32000
            assert "Unity was unavailable" in response["error"]["message"]
            assert "Safe next step:" in response["error"]["message"]

    @pytest.mark.asyncio
    async def test_send_request_retries_same_request_after_established_connection_breaks(
        self,
    ):
        """An established Unity connection drop should retry the same request on a fresh socket."""
        client = UnityTcpClient(
            host="localhost",
            port=21088,
            retry_time=0.0,
            retry_count=4,
        )

        first_reader = AsyncMock(spec=MockStreamReader)
        first_writer = AsyncMock(spec=MockStreamWriter)
        client.reader = first_reader
        client.writer = first_writer
        client.writer.drain.side_effect = ConnectionResetError("Reset")
        client._connection_verified = True
        client._health_probe = AsyncMock(return_value=True)

        request = {"jsonrpc": "2.0", "id": "test", "method": "test"}

        retry_reader = MockStreamReader()
        retry_writer = MockStreamWriter()
        expected_response = {"jsonrpc": "2.0", "id": "test", "result": {}}
        retry_reader.set_response(expected_response)

        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.return_value = (retry_reader, retry_writer)
            response = await client.send_request(request)

        assert response == expected_response
        assert first_writer.close.called is True
        assert client.writer is retry_writer
        assert client.reader is retry_reader
        assert mock_open.call_count == 1

    @pytest.mark.asyncio
    async def test_send_request_returns_actionable_error_after_retry_window_expires(
        self,
    ):
        """A bounded retry window must end with an actionable error instead of a hung request."""
        client = UnityTcpClient(
            host="localhost",
            port=21088,
            retry_time=0.01,
            retry_count=2,
            request_timeout=0.02,
        )

        client.reader = AsyncMock(spec=MockStreamReader)
        client.writer = AsyncMock(spec=MockStreamWriter)
        client.writer.drain.side_effect = ConnectionResetError("Reset")
        client._connection_verified = True

        first_request = {"jsonrpc": "2.0", "id": "first", "method": "test"}
        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.side_effect = ConnectionRefusedError("still reloading")
            first_response = await client.send_request(first_request)

        assert first_response["error"]["code"] == -32000
        assert "Unity was unavailable" in first_response["error"]["message"]
        assert "Safe next step:" in first_response["error"]["message"]

    @pytest.mark.asyncio
    async def test_send_request_times_out_when_response_never_arrives(self):
        """A request must fail explicitly instead of hanging forever on a silent socket."""
        client = UnityTcpClient(
            host="localhost",
            port=21088,
            retry_time=0.0,
            retry_count=1,
            request_timeout=0.01,
        )

        async def never_returns(_n: int) -> bytes:
            await asyncio.sleep(1)
            return b""

        client.reader = AsyncMock(spec=MockStreamReader)
        client.reader.readexactly.side_effect = never_returns
        client.writer = AsyncMock(spec=MockStreamWriter)
        client._connection_verified = True

        response = await client.send_request(
            {"jsonrpc": "2.0", "id": "timeout", "method": "tools/list"}
        )

        assert response["error"]["code"] == -32000
        assert "timed out during response-length" in response["error"]["message"]
        assert client.writer is None
        assert client.reader is None

    @pytest.mark.asyncio
    async def test_send_request_retries_initial_health_probe_failure(self):
        """Startup requests should survive a transient failed ping while Unity finishes initializing."""
        client = UnityTcpClient(
            host="localhost",
            port=21088,
            retry_time=0.0,
            retry_count=2,
        )

        expected_response = {
            "jsonrpc": "2.0",
            "id": "test",
            "result": {"tools": []},
        }

        async def fake_open_connection_for_request(**_kwargs) -> bool:
            reader = MockStreamReader()
            reader.set_response(expected_response)
            client.reader = reader
            client.writer = MockStreamWriter()
            return True

        client._open_connection_for_request = AsyncMock(
            side_effect=fake_open_connection_for_request
        )
        client._health_probe = AsyncMock(side_effect=[False, True])

        response = await client.send_request(
            {"jsonrpc": "2.0", "id": "test", "method": "tools/list", "params": {}}
        )

        assert response == expected_response
        assert client._open_connection_for_request.await_count == 2
        assert client._health_probe.await_count == 2
        assert client._connection_verified is True

    @pytest.mark.asyncio
    async def test_send_request_reconnects_on_next_request_after_timeout(self):
        """The request after a timeout must use a fresh reconnect path."""
        client = UnityTcpClient(
            host="localhost",
            port=21088,
            retry_time=0.0,
            retry_count=1,
            request_timeout=0.01,
        )

        async def never_returns(_n: int) -> bytes:
            await asyncio.sleep(1)
            return b""

        client.reader = AsyncMock(spec=MockStreamReader)
        client.reader.readexactly.side_effect = never_returns
        client.writer = AsyncMock(spec=MockStreamWriter)
        client._connection_verified = True
        client._health_probe = AsyncMock(return_value=True)

        first_response = await client.send_request(
            {"jsonrpc": "2.0", "id": "timeout", "method": "tools/list"}
        )

        mock_reader = MockStreamReader()
        mock_writer = MockStreamWriter()
        expected_response = {"jsonrpc": "2.0", "id": "second", "result": {}}
        mock_reader.set_response(expected_response)

        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.return_value = (mock_reader, mock_writer)
            second_response = await client.send_request(
                {"jsonrpc": "2.0", "id": "second", "method": "test"}
            )

        assert first_response["error"]["code"] == -32000
        assert "timed out during response-length" in first_response["error"]["message"]
        assert second_response == expected_response
        assert mock_open.call_count == 1

    @pytest.mark.asyncio
    async def test_port_resolver_no_change_does_not_disconnect(self):
        """If port_resolver returns the same port, no disconnect/reconnect happens."""
        resolver = lambda: 21088  # noqa: E731
        client = UnityTcpClient(
            host="localhost",
            port=21088,
            retry_time=0.0,
            retry_count=1,
            port_resolver=resolver,
        )

        mock_reader = MockStreamReader()
        mock_writer = MockStreamWriter()
        expected_response = {"jsonrpc": "2.0", "id": "test", "result": {}}
        mock_reader.set_response(expected_response)

        client.reader = mock_reader
        client.writer = mock_writer
        client._connection_verified = True

        await client.send_request({"jsonrpc": "2.0", "id": "test", "method": "ping"})

        # Writer should NOT have been closed
        assert mock_writer.closed is False
        assert client.port == 21088

    @pytest.mark.asyncio
    async def test_port_resolver_port_change_disconnects_and_updates_port(self):
        """If port_resolver returns a new port, client disconnects and updates self.port."""
        new_port = 22000
        resolver = lambda: new_port  # noqa: E731
        client = UnityTcpClient(
            host="localhost",
            port=21088,
            retry_time=0.0,
            retry_count=1,
            port_resolver=resolver,
        )

        old_writer = MockStreamWriter()
        old_reader = MockStreamReader()
        client.reader = old_reader
        client.writer = old_writer
        client._health_probe = AsyncMock(return_value=True)

        mock_reader = MockStreamReader()
        mock_writer = MockStreamWriter()
        expected_response = {"jsonrpc": "2.0", "id": "test", "result": {}}
        mock_reader.set_response(expected_response)

        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.return_value = (mock_reader, mock_writer)
            await client.send_request(
                {"jsonrpc": "2.0", "id": "test", "method": "ping"}
            )

        # Old writer should have been closed (disconnect called)
        assert old_writer.closed is True
        # Port should be updated to the new value
        assert client.port == new_port
        # Reconnection should have been attempted on the new port
        assert mock_open.called
        args = mock_open.call_args[0]
        assert args[1] == new_port

    @pytest.mark.asyncio
    async def test_no_port_resolver_does_not_change_port(self):
        """Without a port_resolver, port stays fixed regardless of settings."""
        client = UnityTcpClient(
            host="localhost",
            port=21088,
            retry_time=0.0,
            retry_count=1,
        )

        mock_reader = MockStreamReader()
        mock_writer = MockStreamWriter()
        expected_response = {"jsonrpc": "2.0", "id": "test", "result": {}}
        mock_reader.set_response(expected_response)
        client.reader = mock_reader
        client.writer = mock_writer
        client._connection_verified = True

        await client.send_request({"jsonrpc": "2.0", "id": "test", "method": "ping"})

        assert client.port == 21088


class TestBridgeServerLifecycle:
    """Tests for MCP server lifecycle error handling."""

    @pytest.mark.asyncio
    async def test_handle_request_suppresses_closed_stream_response_error(self):
        """A closed client write stream must not crash the server task group."""
        server = create_server(AsyncMock(spec=UnityTcpClient))
        message = ClosedStreamRequestResponder()
        session = AsyncMock()

        await server._handle_request(
            message=message,
            req=types.PingRequest(),
            session=session,
            lifespan_context=None,
            raise_exceptions=False,
        )


class TestBridgeLogging:
    """Tests for bridge logging helpers and retention policy."""

    def test_build_rotating_handler_uses_bounded_retention_defaults(self, tmp_path):
        """Bridge log rotation keeps a bounded number of log files."""
        log_path = tmp_path / "bridge.log"

        handler = _build_rotating_handler(log_path)

        assert handler.baseFilename == str(log_path)
        assert handler.maxBytes == LOG_MAX_BYTES
        assert handler.backupCount == LOG_BACKUP_COUNT
        assert handler.encoding == "utf-8"
        assert isinstance(handler.formatter, logging.Formatter)
        assert "pid=%(process)d" in handler.formatter._fmt

    def test_describe_request_includes_request_context(self):
        """Request log summaries should identify the failing MCP call."""
        request = {
            "jsonrpc": "2.0",
            "id": "call_tool_read_unity_console_logs",
            "method": "tools/call",
            "params": {
                "name": "read_unity_console_logs",
                "arguments": {"max_entries": 3},
            },
        }

        summary = _describe_request(request)

        assert "id=call_tool_read_unity_console_logs" in summary
        assert "method=tools/call" in summary
        assert "tool=read_unity_console_logs" in summary
        assert "argument_keys=max_entries" in summary

    def test_describe_response_includes_error_context(self):
        """Response summaries should expose the error code and message."""
        response = {
            "jsonrpc": "2.0",
            "id": "call_tool_read_unity_console_logs",
            "error": {
                "code": -32000,
                "message": "Unity connection dropped during request",
            },
        }

        summary = _describe_response(response)

        assert "id=call_tool_read_unity_console_logs" in summary
        assert "error_code=-32000" in summary
        assert "error_message=Unity connection dropped during request" in summary


class TestMessageFraming:
    """Tests for message framing."""

    def test_length_prefix_encoding(self):
        """Test that length prefix is correctly encoded."""
        message = b"test message"
        length_prefix = struct.pack(">I", len(message))

        assert length_prefix == b"\x00\x00\x00\x0c"  # 12 in big-endian

    def test_length_prefix_decoding(self):
        """Test that length prefix is correctly decoded."""
        length_prefix = b"\x00\x00\x00\x0c"
        length = struct.unpack(">I", length_prefix)[0]

        assert length == 12


class TestJsonRpcMessages:
    """Tests for JSON-RPC message handling."""

    def test_request_serialization(self):
        """Test request serialization."""
        request = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/list",
            "params": {},
        }

        serialized = json.dumps(request)
        deserialized = json.loads(serialized)

        assert deserialized == request

    def test_response_with_result(self):
        """Test response with result."""
        response = {
            "jsonrpc": "2.0",
            "id": 1,
            "result": {"tools": [{"name": "echo"}]},
        }

        assert "error" not in response
        assert response["result"]["tools"][0]["name"] == "echo"

    def test_response_with_error(self):
        """Test response with error."""
        response = {
            "jsonrpc": "2.0",
            "id": 1,
            "error": {
                "code": -32601,
                "message": "Method not found",
            },
        }

        assert "result" not in response
        assert response["error"]["code"] == -32601


class TestSettingsDiscovery:
    """Tests for Unity settings file discovery and port reading."""

    def test_default_request_timeout_is_120_seconds(self):
        """Bridge requests default to a 120 second per-phase timeout."""
        assert DEFAULT_REQUEST_TIMEOUT == 120.0

    # -- fixed settings path ------------------------------------------------

    def test_settings_file_path_is_absolute(self):
        """_SETTINGS_FILE is an absolute path."""
        assert _SETTINGS_FILE.is_absolute()

    def test_settings_file_points_to_expected_name(self):
        """_SETTINGS_FILE ends with the expected filename."""
        assert _SETTINGS_FILE.name == "UnityCodeMcpServerSettings.asset"

    def test_settings_file_matches_known_relative_location(self):
        """_SETTINGS_FILE is 4 parent levels above __file__ and in Editor/."""
        import unity_code_mcp_stdio.unity_code_mcp_bridge_stdio as bridge

        # __file__ → parent(unity_code_mcp_stdio/) → parent(src/) → parent(STDIO~/) → parent(Editor/)
        expected = (
            Path(bridge.__file__).parent.parent.parent.parent
            / "UnityCodeMcpServerSettings.asset"
        ).resolve()
        assert _SETTINGS_FILE.resolve() == expected

    # -- read_port_from_settings --------------------------------------------

    def test_read_port_from_settings_valid(self, tmp_path):
        """Parses a valid StdioPort line from the settings file."""
        settings_file = tmp_path / "settings.asset"
        settings_file.write_text(
            "StartupServer: 0\nStdioPort: 21088\nBacklog: 10\n",
            encoding="utf-8",
        )

        assert read_port_from_settings(settings_file) == 21088

    def test_read_port_from_settings_custom_port(self, tmp_path):
        """Parses a non-default port value."""
        settings_file = tmp_path / "settings.asset"
        settings_file.write_text("StdioPort: 12345\n", encoding="utf-8")

        assert read_port_from_settings(settings_file) == 12345

    def test_read_port_from_settings_missing_key(self, tmp_path):
        """Returns None when StdioPort key is absent."""
        settings_file = tmp_path / "settings.asset"
        settings_file.write_text("StartupServer: 0\nBacklog: 10\n", encoding="utf-8")

        assert read_port_from_settings(settings_file) is None

    def test_read_port_from_settings_invalid_value(self, tmp_path):
        """Returns None when the StdioPort value is not a valid integer."""
        settings_file = tmp_path / "settings.asset"
        settings_file.write_text("StdioPort: not_a_number\n", encoding="utf-8")

        assert read_port_from_settings(settings_file) is None

    def test_read_port_from_settings_file_not_found(self, tmp_path):
        """Returns None when the file does not exist."""
        missing_file = tmp_path / "nonexistent.asset"

        assert read_port_from_settings(missing_file) is None

    # -- get_stdio_port -----------------------------------------------------

    def test_get_stdio_port_reads_from_settings(self, tmp_path):
        """Returns the port from the settings file when found."""
        settings_file = tmp_path / "UnityCodeMcpServerSettings.asset"
        settings_file.write_text("StdioPort: 22000\n", encoding="utf-8")

        result = get_stdio_port(_settings_file=settings_file)

        assert result == 22000

    def test_get_stdio_port_defaults_when_settings_missing(self, tmp_path):
        """Falls back to DEFAULT_PORT when the settings file is not found."""
        missing = tmp_path / "UnityCodeMcpServerSettings.asset"
        # File intentionally not created
        result = get_stdio_port(_settings_file=missing)

        assert result == DEFAULT_PORT

    def test_get_stdio_port_defaults_when_port_unparsable(self, tmp_path):
        """Falls back to DEFAULT_PORT when StdioPort cannot be parsed."""
        settings_file = tmp_path / "UnityCodeMcpServerSettings.asset"
        settings_file.write_text("StdioPort: bad_value\n", encoding="utf-8")

        result = get_stdio_port(_settings_file=settings_file)

        assert result == DEFAULT_PORT

    def test_get_stdio_port_returns_int(self, tmp_path):
        """get_stdio_port always returns an int."""
        settings_file = tmp_path / "UnityCodeMcpServerSettings.asset"
        settings_file.write_text("StdioPort: 19999\n", encoding="utf-8")

        result = get_stdio_port(_settings_file=settings_file)

        assert isinstance(result, int)
        assert result == 19999


class TestResourceContentMapping:
    """Tests for resource content mapping between Unity and MCP SDK types."""

    def test_convert_resource_contents_text(self):
        """Maps text resources to TextResourceContents."""
        resource = {
            "uri": "memory://example.txt",
            "mimeType": "text/plain",
            "text": "hello",
        }

        converted = _convert_resource_contents(resource)

        assert isinstance(converted, types.TextResourceContents)
        assert str(converted.uri) == "memory://example.txt"
        assert converted.mimeType == "text/plain"
        assert converted.text == "hello"

    def test_convert_resource_contents_blob_ignored(self):
        """`blob` payloads are ignored and mapped to TextResourceContents."""
        resource = {
            "uri": "memory://video.mp4",
            "mimeType": "video/mp4",
            "blob": "AAAAGGZ0eXBtcDQyAAAAAG1wNDFpc29tAAAAKHV1",
        }

        converted = _convert_resource_contents(resource)

        # Blob is not supported by the protocol — expect TextResourceContents with empty text
        assert isinstance(converted, types.TextResourceContents)
        assert str(converted.uri) == "memory://video.mp4"
        assert converted.mimeType == "video/mp4"
        assert converted.text == ""

    def test_convert_content_item_resource_blob_ignored(self):
        """Embedded resources with `blob` payloads are mapped to TextResourceContents (blob ignored)."""
        item = {
            "type": "resource",
            "resource": {
                "uri": "memory://play_unity_game_video/28f2eae676cb427583741f19fea98b0b.mp4",
                "mimeType": "video/mp4",
                "blob": "AAAAGGZ0eXBtcDQyAAAAAG1wNDFpc29tAAAAKHV1",
            },
        }

        converted = _convert_content_item(item)

        assert isinstance(converted, types.EmbeddedResource)
        # Blob is unsupported — resource should be TextResourceContents with empty text
        assert isinstance(converted.resource, types.TextResourceContents)
        assert (
            str(converted.resource.uri)
            == "memory://play_unity_game_video/28f2eae676cb427583741f19fea98b0b.mp4"
        )
        assert converted.resource.mimeType == "video/mp4"
        assert converted.resource.text == ""


class TestStaleConnectionDiagnostics:
    """Tests for stale connection detection and diagnostic logging."""

    @pytest.mark.asyncio
    async def test_connected_at_is_set_on_successful_connection(self):
        """_connected_at should be set when a connection is established."""
        client = UnityTcpClient(
            host="localhost",
            port=21088,
            retry_time=0.0,
            retry_count=1,
        )

        mock_reader = MockStreamReader()
        mock_writer = MockStreamWriter()

        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.return_value = (mock_reader, mock_writer)
            result = await client._open_connection_for_request(
                trace_id="test",
                request_summary="id=test method=ping",
                deadline=time.perf_counter() + 5,
            )

        assert result is True
        assert client._connected_at is not None
        assert isinstance(client._connected_at, float)

    @pytest.mark.asyncio
    async def test_connected_at_is_cleared_on_disconnect(self):
        """_connected_at should be None after disconnect."""
        client = UnityTcpClient(
            host="localhost",
            port=21088,
            retry_time=0.0,
            retry_count=1,
        )
        client._connected_at = 12345.0
        client._last_request_completed_at = 12346.0
        client.writer = MockStreamWriter()
        client.reader = MockStreamReader()

        await client.disconnect(reason="test")

        assert client._connected_at is None
        assert client._last_request_completed_at is None

    @pytest.mark.asyncio
    async def test_last_request_completed_at_is_set_after_successful_request(self):
        """_last_request_completed_at should be updated on each successful response."""
        client = UnityTcpClient(
            host="localhost",
            port=21088,
            retry_time=0.0,
            retry_count=1,
        )

        mock_reader = MockStreamReader()
        mock_writer = MockStreamWriter()
        expected_response = {"jsonrpc": "2.0", "id": "test", "result": {}}
        mock_reader.set_response(expected_response)

        client.reader = mock_reader
        client.writer = mock_writer
        client._connection_verified = True

        await client.send_request({"jsonrpc": "2.0", "id": "test", "method": "ping"})

        assert client._last_request_completed_at is not None
        assert isinstance(client._last_request_completed_at, float)

    @pytest.mark.asyncio
    async def test_transport_error_log_includes_connection_age_and_idle_time(self):
        """Stale connection errors should log connection_age_ms and idle_ms for diagnostics."""
        client = UnityTcpClient(
            host="localhost",
            port=21088,
            retry_time=0.0,
            retry_count=1,
            request_timeout=0.5,
        )

        client.reader = AsyncMock(spec=MockStreamReader)
        client.writer = AsyncMock(spec=MockStreamWriter)
        client.writer.drain.side_effect = ConnectionResetError("Reset")
        client._connection_verified = True
        client._connected_at = time.perf_counter() - 30.0  # Connected 30s ago
        client._last_request_completed_at = (
            time.perf_counter() - 10.0
        )  # Last request 10s ago

        with patch(
            "unity_code_mcp_stdio.unity_code_mcp_bridge_stdio.logger"
        ) as mock_logger:
            await client.send_request(
                {"jsonrpc": "2.0", "id": "test", "method": "ping"}
            )

        warning_calls = [
            call
            for call in mock_logger.warning.call_args_list
            if "transport error" in str(call)
        ]
        assert len(warning_calls) >= 1
        log_format_str = warning_calls[0].args[0]
        assert "connection_age_ms=%s" in log_format_str
        assert "idle_ms=%s" in log_format_str
        # connection_age_ms and idle_ms should be numeric (not None)
        log_args = warning_calls[0].args
        connection_age_ms = log_args[6]  # 7th positional arg
        idle_ms = log_args[7]  # 8th positional arg
        assert isinstance(connection_age_ms, int)
        assert isinstance(idle_ms, int)
        assert connection_age_ms >= 29000  # ~30s
        assert idle_ms >= 9000  # ~10s

    @pytest.mark.asyncio
    async def test_transport_error_log_handles_missing_timestamps(self):
        """Transport error logging should handle None timestamps gracefully."""
        client = UnityTcpClient(
            host="localhost",
            port=21088,
            retry_time=0.0,
            retry_count=1,
            request_timeout=0.5,
        )

        client.reader = AsyncMock(spec=MockStreamReader)
        client.writer = AsyncMock(spec=MockStreamWriter)
        client.writer.drain.side_effect = ConnectionResetError("Reset")
        client._connection_verified = True
        # Timestamps not set (as if connection tracking wasn't initialized)

        with patch(
            "unity_code_mcp_stdio.unity_code_mcp_bridge_stdio.logger"
        ) as mock_logger:
            await client.send_request(
                {"jsonrpc": "2.0", "id": "test", "method": "ping"}
            )

        warning_calls = [
            call
            for call in mock_logger.warning.call_args_list
            if "transport error" in str(call)
        ]
        assert len(warning_calls) >= 1
        log_args = warning_calls[0].args
        connection_age_ms = log_args[6]  # 7th positional arg
        idle_ms = log_args[7]  # 8th positional arg
        assert connection_age_ms is None
        assert idle_ms is None
