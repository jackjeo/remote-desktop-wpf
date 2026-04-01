using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Agent
{
    public class NetworkService
    {
        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;

        public event EventHandler<string>? DataReceived;
        public event EventHandler? ClientConnected;
        public event EventHandler? ClientDisconnected;

        public bool IsConnected => _client?.Connected == true;

        public async Task StartAsync(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _client = await _listener.AcceptTcpClientAsync();
            _stream = _client.GetStream();
            ClientConnected?.Invoke(this, EventArgs.Empty);
        }

        public async Task<string?> ReadLineAsync()
        {
            byte[] buffer = new byte[4096];
            try
            {
                int bytesRead = await _stream!.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    return Encoding.UTF8.GetString(buffer, 0, bytesRead);
                }
            }
            catch { }
            return null;
        }

        public async Task WriteAsync(string data)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(data);
            await _stream!.WriteAsync(buffer);
        }

        public async Task WriteFrameAsync(byte[] data, byte frameType)
        {
            byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length + 1));
            byte[] typePrefix = new byte[] { frameType };

            await _stream!.WriteAsync(lengthPrefix);
            await _stream.WriteAsync(typePrefix);
            await _stream.WriteAsync(data);
        }

        public void Disconnect()
        {
            _stream?.Close();
            _client?.Close();
            _stream = null;
            _client = null;
            ClientDisconnected?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            Disconnect();
            _listener?.Stop();
            _listener = null;
        }
    }
}
