using System.IO;
using System.Net.Sockets;

namespace Controller
{
    public class NetworkService
    {
        private TcpClient? _client;
        private NetworkStream? _stream;

        public bool IsConnected => _client?.Connected == true;

        public async Task ConnectAsync(string host, int port)
        {
            _client = new TcpClient();
            await _client.ConnectAsync(host, port);
            _stream = _client.GetStream();
        }

        public async Task<string?> ReadLineAsync()
        {
            byte[] buffer = new byte[4096];
            int bytesRead = await _stream!.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                return System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
            return null;
        }

        public async Task WriteAsync(string data)
        {
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(data);
            await _stream!.WriteAsync(buffer, 0, buffer.Length);
        }

        public async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            return await _stream!.ReadAsync(buffer, offset, count);
        }

        public async Task WriteFrameAsync(byte[] data, byte frameType)
        {
            byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length + 1));
            byte[] typePrefix = new byte[] { frameType };

            await _stream!.WriteAsync(lengthPrefix, 0, lengthPrefix.Length);
            await _stream.WriteAsync(typePrefix, 0, typePrefix.Length);
            await _stream.WriteAsync(data, 0, data.Length);
        }

        public void Disconnect()
        {
            _stream?.Close();
            _client?.Close();
            _stream = null;
            _client = null;
        }
    }
}
