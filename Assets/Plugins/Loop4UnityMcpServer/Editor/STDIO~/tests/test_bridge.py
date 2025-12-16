"""Tests for the Loop MCP STDIO Bridge."""

import asyncio
import json
import struct
from unittest.mock import AsyncMock, patch

import pytest

# Import the module under test
from loop_mcp_stdio import UnityTcpClient


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
