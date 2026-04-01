using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Agent
{
    public class NetworkService
    {
        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private RelayClient? _relayClient;

        public string? RelayServerURL { get; set; }
        public bool UseRelay => !string.IsNullOrEmpty(RelayServerURL);

        public event EventHandler<string>? DataReceived;
        public event EventHandler? ClientConnected;
        public event EventHandler? ClientDisconnected;

        public bool IsConnected => UseRelay 
            ? (_relayClient?.IsConnected ?? false)
            : (_client?.Connected ?? false);

        public async Task StartAsync(int port)
        {
            if (UseRelay)
            {
                await StartWithRelayAsync(port);
            }
            else
            {
                await StartDirectAsync(port);
            }
        }

        private async Task StartDirectAsync(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _client = await _listener.AcceptTcpClientAsync();
            _stream = _client.GetStream();
            ClientConnected?.Invoke(this, EventArgs.Empty);
        }

        private async Task StartWithRelayAsync(int port)
        {
            var machineName = Environment.MachineName;
            var hostname = Environment.HostName;
            var os = Environment.OSVersion.ToString();

            _relayClient = new RelayClient(RelayServerURL!, port, machineName, hostname, os);
            _relayClient.DataReceived += (s, data) => DataReceived?.Invoke(this, Encoding.UTF8.GetString(data));
            _relayClient.Connected += (s, e) => ClientConnected?.Invoke(this, EventArgs.Empty);
            _relayClient.Disconnected += (s, e) => ClientDisconnected?.Invoke(this, EventArgs.Empty);

            _relayClient.Error += (s, err) => Console.WriteLine($"Relay error: {err}");

            await _relayClient.ConnectAsync();
        }

        public async Task<string?> ReadLineAsync()
        {
            if (UseRelay && _relayClient != null)
            {
                return null;
            }

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
            if (UseRelay && _relayClient != null)
            {
                await _relayClient.SendMessageAsync(data);
                return;
            }

            byte[] buffer = Encoding.UTF8.GetBytes(data);
            await _stream!.WriteAsync(buffer);
        }

        public async Task WriteFrameAsync(byte[] data, byte frameType)
        {
            if (UseRelay && _relayClient != null)
            {
                await _relayClient.SendFrameAsync(data, frameType);
                return;
            }

            byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length + 1));
            byte[] typePrefix = new byte[] { frameType };

            await _stream!.WriteAsync(lengthPrefix);
            await _stream.WriteAsync(typePrefix);
            await _stream.WriteAsync(data);
        }

        public void Disconnect()
        {
            if (UseRelay && _relayClient != null)
            {
                _relayClient.Disconnect();
                _relayClient = null;
            }
            else
            {
                _stream?.Close();
                _client?.Close();
                _stream = null;
                _client = null;
            }
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