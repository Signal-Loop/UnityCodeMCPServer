"""Tests for the Unity Code MCP STDIO-over-HTTP bridge."""

from unittest.mock import AsyncMock, patch

import pytest

import unity_code_mcp_stdio.unity_code_mcp_bridge_stdio_over_http as http_bridge
from unity_code_mcp_stdio.unity_code_mcp_bridge_stdio_over_http import (
    DEFAULT_HTTP_PORT,
    HTTP_PROTOCOL_VERSION,
    REQUEST_UNAVAILABLE_ERROR_CODE,
    UNITY_HTTP_HOST,
    UnityHttpClient,
    read_http_port_from_settings,
)


class TestSettingsDiscovery:
    """Tests for HTTP settings discovery."""

    def test_read_http_port_from_settings_valid(self, tmp_path):
        settings_file = tmp_path / "settings.asset"
        settings_file.write_text("HttpPort: 3002\n", encoding="utf-8")

        assert read_http_port_from_settings(settings_file) == 3002

    def test_read_http_port_from_settings_missing_key(self, tmp_path):
        settings_file = tmp_path / "settings.asset"
        settings_file.write_text("StdioPort: 21099\n", encoding="utf-8")

        assert read_http_port_from_settings(settings_file) is None


class TestUnityHttpClient:
    """Tests for the stateless HTTP bridge client."""

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


class TestCliDefaults:
    def test_main_uses_retry_count_default_of_five(self, monkeypatch):
        monkeypatch.setattr("sys.argv", ["bridge-http"])
        monkeypatch.setattr(http_bridge, "get_http_port", lambda: 3001)

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
            patch.object(http_bridge, "run_server", new=fake_run_server),
            patch.object(http_bridge.asyncio, "run", side_effect=fake_asyncio_run),
        ):
            http_bridge.main()

        assert captured["retry_count"] == 5
