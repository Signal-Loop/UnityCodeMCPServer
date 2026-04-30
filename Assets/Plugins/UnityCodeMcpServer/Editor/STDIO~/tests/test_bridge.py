"""Tests for the HTTP-backed Unity Code MCP STDIO bridge."""

import json
import logging
from pathlib import Path
from unittest.mock import AsyncMock, patch

import anyio
import pytest
from mcp import types

import unity_code_mcp_stdio
import unity_code_mcp_stdio.unity_code_mcp_bridge_stdio as stdio_bridge
from unity_code_mcp_stdio.unity_code_mcp_bridge_stdio import (
    DEFAULT_HTTP_PORT,
    DEFAULT_REQUEST_TIMEOUT,
    HTTP_PROTOCOL_VERSION,
    LOG_BACKUP_COUNT,
    LOG_MAX_BYTES,
    REQUEST_UNAVAILABLE_ERROR_CODE,
    UNITY_HTTP_HOST,
    UnityHttpClient,
    _SETTINGS_FILE,
    _build_rotating_handler,
    _convert_content_item,
    _convert_resource_contents,
    _describe_request,
    _describe_response,
    create_server,
    get_http_port,
    read_http_port_from_settings,
)


class ClosedStreamRequestResponder:
    """Minimal request responder that simulates a closed client write stream."""

    def __init__(self, request_id="test-request"):
        self.request_id = request_id
        self.request_meta = None
        self.message_metadata = None

    async def respond(self, response):
        raise anyio.ClosedResourceError()


class TestPackageExports:
    def test_package_main_uses_http_stdio_bridge(self):
        assert unity_code_mcp_stdio.main is stdio_bridge.main
        assert unity_code_mcp_stdio.run_server is stdio_bridge.run_server
        assert unity_code_mcp_stdio.UnityHttpClient is UnityHttpClient


class TestSettingsDiscovery:
    def test_default_request_timeout_is_120_seconds(self):
        assert DEFAULT_REQUEST_TIMEOUT == 120.0

    def test_settings_file_path_is_absolute(self):
        assert _SETTINGS_FILE.is_absolute()

    def test_settings_file_points_to_expected_name(self):
        assert _SETTINGS_FILE.name == "UnityCodeMcpServerSettings.asset"

    def test_settings_file_matches_known_relative_location(self):
        expected = (
            Path(stdio_bridge.__file__).parent.parent.parent.parent
            / "UnityCodeMcpServerSettings.asset"
        ).resolve()
        assert _SETTINGS_FILE.resolve() == expected

    def test_read_http_port_from_settings_valid(self, tmp_path):
        settings_file = tmp_path / "settings.asset"
        settings_file.write_text("HttpPort: 3002\n", encoding="utf-8")

        assert read_http_port_from_settings(settings_file) == 3002

    def test_read_http_port_from_settings_missing_key(self, tmp_path):
        settings_file = tmp_path / "settings.asset"
        settings_file.write_text("Backlog: 10\n", encoding="utf-8")

        assert read_http_port_from_settings(settings_file) is None

    def test_read_http_port_from_settings_invalid_value(self, tmp_path):
        settings_file = tmp_path / "settings.asset"
        settings_file.write_text("HttpPort: not_a_number\n", encoding="utf-8")

        assert read_http_port_from_settings(settings_file) is None

    def test_get_http_port_reads_from_settings(self, tmp_path):
        settings_file = tmp_path / "UnityCodeMcpServerSettings.asset"
        settings_file.write_text("HttpPort: 32000\n", encoding="utf-8")

        assert get_http_port(_settings_file=settings_file) == 32000

    def test_get_http_port_defaults_when_settings_missing(self, tmp_path):
        missing = tmp_path / "UnityCodeMcpServerSettings.asset"

        assert get_http_port(_settings_file=missing) == DEFAULT_HTTP_PORT


class TestUnityHttpClient:
    def test_default_http_host_uses_ipv4_loopback(self):
        assert UNITY_HTTP_HOST == "127.0.0.1"

    def test_build_headers_use_plain_mcp_http_headers(self):
        client = UnityHttpClient(
            host="127.0.0.1",
            port=DEFAULT_HTTP_PORT,
            retry_time=0.0,
            retry_count=1,
        )

        headers = client._build_headers()

        assert headers["Accept"] == "application/json, text/event-stream"
        assert headers["Content-Type"] == "application/json"
        assert headers["MCP-Protocol-Version"] == HTTP_PROTOCOL_VERSION
        assert set(headers) == {"Accept", "Content-Type", "MCP-Protocol-Version"}
        assert "Mcp-Session-Id" not in headers

    def test_parse_sse_response_reads_message_event_payload(self):
        body = b'event: message\ndata: {"jsonrpc":"2.0","id":"1","result":{}}\n\n'

        parsed = UnityHttpClient._parse_sse_response(body)

        assert parsed == {"jsonrpc": "2.0", "id": "1", "result": {}}

    @pytest.mark.asyncio
    async def test_send_request_forwards_request_without_bridge_bootstrap(self):
        client = UnityHttpClient(
            host="127.0.0.1",
            port=DEFAULT_HTTP_PORT,
            retry_time=0.0,
            retry_count=1,
        )

        sent_methods = []

        async def fake_send_transport_request(
            request, *, trace_id, request_summary, timeout_seconds
        ):
            sent_methods.append(request["method"])
            return {
                "jsonrpc": "2.0",
                "id": request["id"],
                "result": {"tools": []},
            }

        client._send_transport_request = AsyncMock(
            side_effect=fake_send_transport_request
        )

        response = await client.send_request(
            {"jsonrpc": "2.0", "id": "tools", "method": "tools/list", "params": {}}
        )

        assert response["result"] == {"tools": []}
        assert sent_methods == ["tools/list"]

    @pytest.mark.asyncio
    async def test_send_request_retries_same_request_after_reload_failure(self):
        client = UnityHttpClient(
            host="127.0.0.1",
            port=DEFAULT_HTTP_PORT,
            retry_time=0.0,
            retry_count=2,
        )

        sent_methods = []
        failure_seen = False

        async def fake_send_transport_request(
            request, *, trace_id, request_summary, timeout_seconds
        ):
            nonlocal failure_seen

            sent_methods.append(request["method"])
            if not failure_seen:
                failure_seen = True
                raise ConnectionRefusedError("Unity is reloading")

            return {
                "jsonrpc": "2.0",
                "id": request["id"],
                "result": {"tools": []},
            }

        client._send_transport_request = AsyncMock(
            side_effect=fake_send_transport_request
        )

        response = await client.send_request(
            {"jsonrpc": "2.0", "id": "tools", "method": "tools/list", "params": {}}
        )

        assert response["result"] == {"tools": []}
        assert sent_methods == ["tools/list", "tools/list"]

    @pytest.mark.asyncio
    async def test_send_request_returns_actionable_error_when_retry_budget_expires(
        self,
    ):
        client = UnityHttpClient(
            host="127.0.0.1",
            port=DEFAULT_HTTP_PORT,
            retry_time=0.0,
            retry_count=1,
            request_timeout=0.01,
        )

        client._send_transport_request = AsyncMock(
            side_effect=ConnectionRefusedError("Unity is reloading")
        )

        response = await client.send_request(
            {"jsonrpc": "2.0", "id": "tools", "method": "tools/list", "params": {}}
        )

        assert response["error"]["code"] == REQUEST_UNAVAILABLE_ERROR_CODE
        assert "Unity was unavailable" in response["error"]["message"]

    @pytest.mark.asyncio
    async def test_send_request_retries_after_timeout_without_global_deadline(self):
        client = UnityHttpClient(
            host="127.0.0.1",
            port=DEFAULT_HTTP_PORT,
            retry_time=0.0,
            retry_count=2,
            request_timeout=0.01,
        )

        remaining_time_values = iter([1.0, 0.0])
        client._remaining_time = lambda _deadline: next(remaining_time_values)
        client._send_transport_request = AsyncMock(
            side_effect=[
                TimeoutError("timed out"),
                {
                    "jsonrpc": "2.0",
                    "id": "tools",
                    "result": {"tools": []},
                },
            ]
        )

        response = await client.send_request(
            {"jsonrpc": "2.0", "id": "tools", "method": "tools/list", "params": {}}
        )

        assert response["result"] == {"tools": []}
        assert client._send_transport_request.await_count == 2


class TestBridgeServerLifecycle:
    @pytest.mark.asyncio
    async def test_handle_request_suppresses_closed_stream_response_error(self):
        server = create_server(AsyncMock(spec=UnityHttpClient))
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
    def test_build_rotating_handler_uses_bounded_retention_defaults(self, tmp_path):
        log_path = tmp_path / "bridge.log"

        handler = _build_rotating_handler(log_path)

        assert handler.baseFilename == str(log_path)
        assert handler.maxBytes == LOG_MAX_BYTES
        assert handler.backupCount == LOG_BACKUP_COUNT
        assert handler.encoding == "utf-8"
        assert isinstance(handler.formatter, logging.Formatter)
        assert "pid=%(process)d" in handler.formatter._fmt

    def test_describe_request_includes_request_context(self):
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


class TestJsonRpcMessages:
    def test_request_serialization(self):
        request = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/list",
            "params": {},
        }

        serialized = json.dumps(request)
        deserialized = json.loads(serialized)

        assert deserialized == request


class TestResourceContentMapping:
    def test_convert_resource_contents_text(self):
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

    def test_convert_content_item_resource_blob_ignored(self):
        item = {
            "type": "resource",
            "resource": {
                "uri": "memory://play_unity_game_video/example.mp4",
                "mimeType": "video/mp4",
                "blob": "AAAAGGZ0eXBtcDQyAAAAAG1wNDFpc29tAAAAKHV1",
            },
        }

        converted = _convert_content_item(item)

        assert isinstance(converted, types.EmbeddedResource)
        assert isinstance(converted.resource, types.TextResourceContents)
        assert (
            str(converted.resource.uri) == "memory://play_unity_game_video/example.mp4"
        )
        assert converted.resource.mimeType == "video/mp4"
        assert converted.resource.text == ""


class TestCliDefaults:
    def test_main_uses_retry_count_default_of_five(self, monkeypatch):
        monkeypatch.setattr("sys.argv", ["bridge-http"])
        monkeypatch.setattr(stdio_bridge, "get_http_port", lambda: 3001)

        captured = {}

        def fake_run_server(host, port, retry_time, retry_count, request_timeout):
            captured.update(
                {
                    "host": host,
                    "port": port,
                    "retry_time": retry_time,
                    "retry_count": retry_count,
                    "request_timeout": request_timeout,
                }
            )

            async def done():
                return None

            return done()

        def fake_asyncio_run(coro):
            try:
                coro.close()
            except AttributeError:
                pass

        with (
            patch.object(stdio_bridge, "run_server", new=fake_run_server),
            patch.object(stdio_bridge.asyncio, "run", side_effect=fake_asyncio_run),
        ):
            stdio_bridge.main()

        assert captured["host"] == UNITY_HTTP_HOST
        assert captured["retry_count"] == 5
