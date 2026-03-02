"""Tests for the Unity Code MCP STDIO Bridge."""

import asyncio
import json
import struct
from pathlib import Path
from unittest.mock import AsyncMock, patch

import pytest
from mcp import types

# Import the module under test
from unity_code_mcp_stdio import UnityTcpClient
from unity_code_mcp_stdio.unity_code_mcp_bridge_stdio import (
    DEFAULT_PORT,
    _SETTINGS_FILE,
    _convert_content_item,
    _convert_resource_contents,
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
    async def test_connect_success(self, unity_client):
        """Test successful connection."""
        mock_reader = MockStreamReader()
        mock_writer = MockStreamWriter()

        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.return_value = (mock_reader, mock_writer)

            result = await unity_client.connect()

            assert result is True
            assert unity_client.reader is mock_reader
            assert unity_client.writer is mock_writer

    @pytest.mark.asyncio
    async def test_connect_uses_configured_host_and_port(self):
        """Test that connect uses the host and port stored at construction time."""
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
            await client.connect()

        args = mock_open.call_args[0]
        assert args[0] == "127.0.0.1"
        assert args[1] == 12345

    @pytest.mark.asyncio
    async def test_connect_failure_retries(self, unity_client):
        """Test connection retries on failure."""
        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.side_effect = ConnectionRefusedError("Connection refused")

            result = await unity_client.connect()

            assert result is False
            assert mock_open.call_count == unity_client.retry_count

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
        """Test request retry success after initial connection error."""
        mock_reader_success = MockStreamReader()
        expected_response = {
            "jsonrpc": "2.0",
            "id": "test",
            "result": {"success": True},
        }
        mock_reader_success.set_response(expected_response)

        mock_writer_success = MockStreamWriter()

        # Mock connect to first fail (or connect but writer fails), then succeed
        # In this test we simulate a writer failure during send
        unity_client.writer = AsyncMock(spec=MockStreamWriter)
        unity_client.reader = AsyncMock(spec=MockStreamReader)

        # Setup the writer to raise an error on the first write/drain
        unity_client.writer.drain.side_effect = [ConnectionResetError("Reset"), None]

        # For the reconnection
        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.return_value = (mock_reader_success, mock_writer_success)

            request = {
                "jsonrpc": "2.0",
                "id": "test",
                "method": "test_method",
            }

            response = await unity_client.send_request(request)

            assert response == expected_response
            # Should have attempted to reconnect
            assert mock_open.called

    @pytest.mark.asyncio
    async def test_send_request_retry_exhausted(self, unity_client):
        """Test request retry exhaustion."""
        unity_client.retry_count = 2
        unity_client.retry_time = 0.01

        # Mock writer that always fails
        unity_client.writer = AsyncMock(spec=MockStreamWriter)
        unity_client.reader = AsyncMock(spec=MockStreamReader)
        unity_client.writer.drain.side_effect = ConnectionResetError("Always fails")

        # Mock connect to also fail (or succeed but then write fails again)
        # Let's say connect succeeds but write fails immediately
        mock_reader = MockStreamReader()
        mock_writer = AsyncMock(spec=MockStreamWriter)
        mock_writer.drain.side_effect = ConnectionResetError("Always fails")

        with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
            mock_open.return_value = (mock_reader, mock_writer)

            request = {"jsonrpc": "2.0", "id": "test", "method": "test"}
            response = await unity_client.send_request(request)

            assert "error" in response
            assert response["error"]["code"] == -32000
            assert "Failed to communicate" in response["error"]["message"]


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
