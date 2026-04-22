using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UnityCodeMcpServer.Servers.StreamableHttp
{
    public sealed class LoopbackHttpServerTransport : IDisposable
    {
        private readonly IPAddress _address;
        private readonly int _backlog;
        private readonly Func<TcpClient, CancellationToken, UniTask> _clientHandler;

        private TcpListener _listener;
        private CancellationTokenSource _acceptLoopCts;

        public LoopbackHttpServerTransport(
            IPAddress address,
            int port,
            Func<TcpClient, CancellationToken, UniTask> clientHandler,
            int backlog = 16)
        {
            _address = address ?? throw new ArgumentNullException(nameof(address));
            Port = port;
            _clientHandler = clientHandler ?? throw new ArgumentNullException(nameof(clientHandler));
            _backlog = backlog;
        }

        public int Port { get; }

        public bool IsListening { get; private set; }

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
            _listener.Server.LingerState = new LingerOption(true, 0);
            _listener.Start(_backlog);
            IsListening = true;

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
            }
        }

        private async UniTaskVoid HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                await _clientHandler(client, ct);
            }
        }
    }
}
