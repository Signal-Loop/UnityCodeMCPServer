"""Unity Code MCP STDIO Bridge - Entry point."""

from unity_code_mcp_stdio.unity_code_mcp_stdio import (
    FileBridgePaths,
    UnityFileClient,
    create_server,
    main,
    run_server,
)

__all__ = [
    "main",
    "UnityFileClient",
    "FileBridgePaths",
    "create_server",
    "run_server",
]
