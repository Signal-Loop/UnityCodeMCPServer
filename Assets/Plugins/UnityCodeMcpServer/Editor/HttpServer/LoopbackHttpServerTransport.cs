using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Helpers;

namespace UnityCodeMcpServer.HttpServer
{
    public sealed class LoopbackHttpServerTransport : IDisposable
    {
        private readonly IPAddress _address;
        private readonly int _backlog;
        private readonly Func<TcpClient, CancellationToken, UniTask> _clientHandler;
        private readonly Action<Exception> _acceptLoopFaulted;

        private TcpListener _listener;
        private CancellationTokenSource _acceptLoopCts;
        private int _activeConnections;
        private int _staleConnections;

        public LoopbackHttpServerTransport(
            IPAddress address,
            int port,
            Func<TcpClient, CancellationToken, UniTask> clientHandler,
            int backlog = 16,
            Action<Exception> acceptLoopFaulted = null)
        {
            _address = address ?? throw new ArgumentNullException(nameof(address));
            Port = port;
            _clientHandler = clientHandler ?? throw new ArgumentNullException(nameof(clientHandler));
            _backlog = backlog;
            _acceptLoopFaulted = acceptLoopFaulted;
        }

        public int Port { get; }

        public bool IsListening { get; private set; }

        public bool IsAcceptLoopRunning { get; private set; }

        public int ActiveConnections => Volatile.Read(ref _activeConnections);

        public int StaleConnections => Volatile.Read(ref _staleConnections);

        public DateTime? LastAcceptUtc { get; private set; }

        public string LastUnhandledAcceptLoopException { get; private set; }

        public void Start()
        {
            if (IsListening)
            {
                return;
            }

            _acceptLoopCts = new CancellationTokenSource();
            _listener = new TcpListener(_address, Port);
            _listener.Server.ExclusiveAddressUse = false;
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start(_backlog);
            IsListening = true;
            IsAcceptLoopRunning = true;
            LastUnhandledAcceptLoopException = null;

            AcceptClientsAsync(_acceptLoopCts.Token).Forget();
        }

        public void Stop()
        {
            if (!IsListening && _listener == null && _acceptLoopCts == null)
            {
                return;
            }

            _acceptLoopCts?.Cancel();

            try
            {
                if (_listener?.Server != null)
                {
                    _listener.Server.Close(0);
                    _listener.Server.Dispose();
                }

                _listener?.Stop();
            }
            finally
            {
                _listener = null;
                _acceptLoopCts?.Dispose();
                _acceptLoopCts = null;
                IsAcceptLoopRunning = false;
                IsListening = false;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private async UniTaskVoid AcceptClientsAsync(CancellationToken ct)
        {
            await UniTask.SwitchToThreadPool();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpListener listener = _listener;
                    if (listener == null)
                    {
                        break;
                    }

                    try
                    {
                        TcpClient client = await listener.AcceptTcpClientAsync();
                        LastAcceptUtc = DateTime.UtcNow;
                        string remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                        UnityCodeMcpServerLogger.Debug($"[LoopbackHttpServerTransport] Accepted connection remote={remoteEndPoint}");
                        HandleClientAsync(client, ct).Forget();
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LastUnhandledAcceptLoopException = ex.ToString();
                        UnityCodeMcpServerLogger.Error($"[LoopbackHttpServerTransport] Accept loop faulted: {ex}");
                        _acceptLoopFaulted?.Invoke(ex);
                        break;
                    }
                }
            }
            finally
            {
                IsAcceptLoopRunning = false;
            }
        }

        private async UniTaskVoid HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            string remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            Interlocked.Increment(ref _activeConnections);

            try
            {
                client.NoDelay = true;
                await _clientHandler(client, ct);
                UnityCodeMcpServerLogger.Debug($"[LoopbackHttpServerTransport] Request handling completed remote={remoteEndPoint}");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                UnityCodeMcpServerLogger.Error($"[LoopbackHttpServerTransport] Client loop faulted remote={remoteEndPoint}: {ex}");
            }
            finally
            {
                client.Dispose();
                int activeConnections = Interlocked.Decrement(ref _activeConnections);
                UnityCodeMcpServerLogger.Debug($"[LoopbackHttpServerTransport] Connection disposed remote={remoteEndPoint} activeConnections={activeConnections}");
            }
        }

        public void RecordStaleConnection()
        {
            Interlocked.Increment(ref _staleConnections);
        }
    }
}
