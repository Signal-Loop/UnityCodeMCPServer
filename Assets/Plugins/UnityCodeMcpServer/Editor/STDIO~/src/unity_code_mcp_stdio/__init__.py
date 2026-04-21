"""Unity Code MCP STDIO Bridge - Entry point."""

from unity_code_mcp_stdio.unity_code_mcp_bridge_stdio import (
    main,
    UnityTcpClient,
    create_server,
    run_server,
)
from unity_code_mcp_stdio.unity_code_mcp_bridge_stdio_over_http import (
    UnityHttpClient,
    main as http_main,
    run_server as run_http_server,
)

__all__ = [
    "main",
    "UnityTcpClient",
    "UnityHttpClient",
    "create_server",
    "run_server",
    "http_main",
    "run_http_server",
]
