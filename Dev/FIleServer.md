Create new Unity/brigde connection - FileServer

Main components are:
- on unity side: FileServer
- on stdio side: unity-code-mcp-stdio

FileServer exposes the same MCP surface features as the current Unity transport pipeline used by Assets\Plugins\UnityCodeMcpServer\Editor\HttpServer, but instead of using HTTP protocol, it uses file-based communication. It watches a specific directory for incoming request files, processes them, and writes response files back to the directory.
unity-code-mcp-stdio is a bridge that runs on the stdio side and has the same MCP-facing features as unity_code_mcp_bridge_stdio.py, but instead of HTTP it communicates with the FileServer by writing request files to the watched directory and reading response files.

Files are exchanged through the folder .unityCodeMcpServer/messages
Request message name is: [timestamp]_request_[clientId].json
Response message name is: [timestamp]_response_[clientId].json
Where timestamp is current readable time including miliseconds, example: 20240601123045123 which means 2024-06-01 12:30:45 and 123 milliseconds
Client id is unique identifier of the client, created dynamically by the stdio bridge side when it starts, for example a guid.


Protocol:

1. MCP client sends request to the stdio bridge (unity-code-mcp-stdio) using MCP protocol over stdio
2. stdio bridge receives the request, queues it. when it's the request's turn to be processed, stdio bridge creates a request file in the watched directory with the request content serialized as JSON. Only one request can be processed at a time, so if there are multiple requests, stdio bridge queues them and creates request files one by one as previous ones are processed
3. FileServer detects the new request file, reads it, processes the request similarly to how it processes HTTP requests, and creates a response file with the response content serialized as JSON, Fileserver writes both successful responses and error responses to the response file, depending on the outcome of processing the request. The response file has the same timestamp and client id as the request file to associate them together.
3.1 If there are multiple request files, FileServer processes them in the order of their timestamps to ensure that requests are handled sequentially. No new request file is processed until the response file for the previous request is created, ensuring that only one request is processed at a time
4. stdio bridge detects the new response file, reads it, and sends the response back to the MCP client using MCP protocol over stdio. Only sfter the response is sent, stdio bridge deletes both the request and response files to clean up the directory and allow the next request to be processed.

unity-code-mcp-stdio timeout is set by default to 3 minutes (measured in seconds, configurable). If no response file is detected within this time after creating the request file, stdio bridge considers the request failed, sends a timeout error response back to the MCP client, and deletes the request file to clean up the directory.

When unity-code-mcp-stdio starts
- it creates a unique client id (for example a guid) that is used in the request and response file names to associate them with this specific client. This allows multiple instances of unity-code-mcp-stdio to run simultaneously without interfering with each other's requests and responses, as each instance will only process files that match its own client id.


When FileServer starts
- it checks if the watched directory exists, if not it creates it. This ensures that the system is ready to handle requests as soon as it starts, without requiring any manual setup of the directory. The directory is typically located at .unityCodeMcpServer/messages within the Unity project folder.
- it processes any existing request files in the watched directory that match the expected naming pattern and not have a corresponding response file, in case there are any leftover requests from a previous run. This allows the system to recover gracefully from domain reloads, unexpected shutdowns or crashes, ensuring that pending requests are not lost and can still be processed when the server restarts.


Resolved assumptions

1. Default request timeout for unity-code-mcp-stdio is 3 minutes.
2. The current reference implementation on the stdio side is unity_code_mcp_bridge_stdio.py.
3. Orphaned late response files may remain in the message directory for now and are out of scope for the first implementation.
4. Parity with the current transport means MCP surface behavior parity, not HTTP-specific transport behavior parity.

