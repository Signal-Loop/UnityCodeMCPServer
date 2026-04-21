"""Tests for the Unity Code MCP STDIO-over-HTTP bridge."""

from unittest.mock import AsyncMock

import pytest

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
            request, *, trace_id, request_summary, deadline
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
            request, *, trace_id, request_summary, deadline
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
