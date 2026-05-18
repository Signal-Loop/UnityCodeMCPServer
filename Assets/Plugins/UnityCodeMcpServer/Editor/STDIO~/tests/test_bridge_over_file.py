"""Tests for the file-backed Unity Code MCP STDIO bridge."""

import importlib.util
import json
import logging
from pathlib import Path
import sys
from unittest.mock import AsyncMock

import pytest
from mcp import types

from unity_code_mcp_stdio.unity_code_mcp_stdio import (
    DEFAULT_FILE_REQUEST_TIMEOUT,
    FileBridgePaths,
    UnityFileClient,
    _write_text_atomically,
    _build_call_tool_result,
    get_project_root,
)


MODULE_PATH = (
    Path(__file__).resolve().parent.parent
    / "src"
    / "unity_code_mcp_stdio"
    / "unity_code_mcp_stdio.py"
)


def load_bridge_over_file_module():
    spec = importlib.util.spec_from_file_location(
        "isolated_bridge_over_file",
        MODULE_PATH,
    )
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    try:
        spec.loader.exec_module(module)
        return module
    finally:
        sys.modules.pop(spec.name, None)


class TestFileBridgePaths:
    def test_default_file_request_timeout_is_180_seconds(self):
        assert DEFAULT_FILE_REQUEST_TIMEOUT == 180.0

    def test_package_main_uses_file_bridge_entrypoint(self):
        import unity_code_mcp_stdio
        import unity_code_mcp_stdio.unity_code_mcp_stdio as file_bridge

        assert unity_code_mcp_stdio.main is file_bridge.main
        assert unity_code_mcp_stdio.UnityFileClient is file_bridge.UnityFileClient

    def test_get_project_root_resolves_workspace_root(self):
        project_root = get_project_root()

        assert project_root.name == "UnityCodeMcpServer"
        assert (project_root / "Assets").is_dir()

    def test_build_request_path_uses_timestamp_and_client_id(self, tmp_path):
        paths = FileBridgePaths(project_root=tmp_path, client_id="client-123")

        request_path = paths.build_request_path("20260504123456789")

        assert request_path.name == "20260504123456789_request_client-123.json"
        assert request_path.parent == tmp_path / ".unityCodeMcpServer" / "messages"

    def test_module_loads_in_isolation(self):
        module = load_bridge_over_file_module()

        assert module.DEFAULT_FILE_REQUEST_TIMEOUT == 180.0

    def test_module_configures_file_logging_on_import(self):
        module = load_bridge_over_file_module()

        assert any(
            isinstance(handler, logging.FileHandler)
            for handler in module.logger.handlers
        )


class TestUnityFileClient:
    def test_write_text_atomically_replaces_file_without_leaving_temp_files(
        self, tmp_path
    ):
        target_path = tmp_path / "request.json"

        _write_text_atomically(target_path, '{"jsonrpc":"2.0"}')

        assert target_path.read_text(encoding="utf-8") == '{"jsonrpc":"2.0"}'
        assert list(tmp_path.glob("*.tmp")) == []

    @pytest.mark.asyncio
    async def test_send_request_writes_request_and_returns_matching_response(
        self, tmp_path
    ):
        client = UnityFileClient(
            project_root=tmp_path,
            client_id="client-123",
            request_timeout=1.0,
        )
        request_payload = {
            "jsonrpc": "2.0",
            "id": "tools",
            "method": "tools/list",
            "params": {},
        }

        async def fake_wait_for_response(response_path):
            assert response_path.name.endswith("_response_client-123.json")
            response_path.write_text(
                json.dumps(
                    {
                        "jsonrpc": "2.0",
                        "id": "tools",
                        "result": {"tools": []},
                    }
                ),
                encoding="utf-8",
            )

        client._wait_for_response = fake_wait_for_response

        response = await client.send_request(request_payload)

        assert response["result"] == {"tools": []}
        assert list(client.paths.messages_dir.glob("*_request_client-123.json")) == []
        assert list(client.paths.messages_dir.glob("*_response_client-123.json")) == []

    @pytest.mark.asyncio
    async def test_send_request_succeeds_after_unity_deletes_request_file(
        self, tmp_path
    ):
        client = UnityFileClient(
            project_root=tmp_path,
            client_id="client-123",
            request_timeout=1.0,
        )
        request_payload = {
            "jsonrpc": "2.0",
            "id": "tools",
            "method": "tools/list",
            "params": {},
        }
        request_paths: list[Path] = []

        async def fake_wait_for_response(response_path):
            request_paths.extend(
                client.paths.messages_dir.glob("*_request_client-123.json")
            )
            assert len(request_paths) == 1
            request_paths[0].unlink(missing_ok=True)
            response_path.write_text(
                json.dumps(
                    {
                        "jsonrpc": "2.0",
                        "id": "tools",
                        "result": {"tools": []},
                    }
                ),
                encoding="utf-8",
            )

        client._wait_for_response = fake_wait_for_response

        response = await client.send_request(request_payload)

        assert response["result"] == {"tools": []}
        assert request_paths[0].exists() is False

    @pytest.mark.asyncio
    async def test_send_request_returns_timeout_error_and_removes_request_file(
        self, tmp_path
    ):
        client = UnityFileClient(
            project_root=tmp_path,
            client_id="client-123",
            request_timeout=0.01,
        )
        request_payload = {
            "jsonrpc": "2.0",
            "id": "tools",
            "method": "tools/list",
            "params": {},
        }

        async def fake_wait_for_response(response_path):
            raise TimeoutError("timed out")

        client._wait_for_response = fake_wait_for_response

        response = await client.send_request(request_payload)

        assert response["error"]["message"].startswith(
            "Timed out waiting for Unity file response"
        )
        assert list(client.paths.messages_dir.glob("*_request_client-123.json")) == []


class TestToolErrorHandling:
    @pytest.mark.asyncio
    async def test_build_call_tool_result_marks_tool_result_as_error(self):
        client = AsyncMock()
        client.send_request.return_value = {
            "jsonrpc": "2.0",
            "id": "call_tool_read_unity_console_logs",
            "result": {
                "content": [
                    {
                        "type": "text",
                        "text": "Tool execution error: main-thread only API",
                    }
                ],
                "isError": True,
            },
        }

        result = await _build_call_tool_result(
            client,
            "read_unity_console_logs",
            {"max_entries": 2},
        )

        assert isinstance(result, types.CallToolResult)
        assert result.isError is True
        assert result.content[0].text == "Tool execution error: main-thread only API"
