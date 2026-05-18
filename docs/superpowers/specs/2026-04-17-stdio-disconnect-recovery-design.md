# STDIO Disconnect Recovery Design

## Goal

Prevent the Python STDIO bridge from leaving VS Code attached to a degraded MCP session after the Unity TCP backend drops mid-request.

## Problem Summary

The primary failure mode is not startup or port discovery. The bridge starts successfully, processes requests, then later hits an upstream socket failure such as `WinError 64`. Today the bridge keeps retrying that request inside the same long-lived MCP session. That creates two bad outcomes:

1. A single MCP request can stall for up to the full retry window while the bridge repeatedly reconnects.
2. If the MCP client gives up or closes the session during that delay, the server can still try to respond into a closed write stream and crash with `anyio.ClosedResourceError`.

The custom line-based stdin transport should still be audited separately, but it does not fit the observed rare-and-recoverable production symptom.

## Recommended Approach

Use fail-fast upstream disconnect handling for already-established Unity sessions.

### Behavior

1. If a request hits a Unity socket failure after the bridge had an established TCP session, treat that request as failed immediately instead of retrying through the full retry budget.
2. Disconnect the Unity socket, mark the connection state cleanly, and return a deterministic JSON-RPC error for the current MCP request.
3. Allow the next MCP request to establish a fresh Unity TCP connection instead of trying to heal the broken request in place.
4. Treat expected stream-closure conditions during bridge shutdown as normal termination rather than surfacing `ClosedResourceError` as an unhandled bridge crash.

## Why This Approach

This is the smallest change that addresses the observed user impact:

1. It removes the long degraded request window that makes the MCP client appear stuck.
2. It preserves the existing process model and avoids a larger stdio transport rewrite in the same change.
3. It keeps recovery cheap: the next request can reconnect normally.
4. It directly targets the logged `ClosedResourceError` teardown path.

## Scope

### In Scope

1. Unity TCP request failure handling in the Python bridge.
2. Regression tests for fail-fast disconnect behavior.
3. Graceful handling of expected stream closure during shutdown.
4. Documentation updates for the bridge behavior.

### Out of Scope

1. Replacing the custom stdin/stdout transport with the SDK transport.
2. Changing Unity-side TCP server behavior.
3. End-to-end VS Code launcher changes.

## Verification

1. Add a failing Python test showing that a mid-request connection reset does not burn through the full reconnect budget once an established session has broken.
2. Add a failing Python test showing that the next request can reconnect successfully after that failure.
3. Run the Python bridge test suite.
4. Read Unity console logs to confirm there are no editor compilation errors introduced by this work.
