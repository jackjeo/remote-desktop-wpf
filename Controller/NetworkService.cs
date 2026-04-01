using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Controller
{
    public class NetworkService
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;

        public string? RelayServerURL { get; set; } = "http://aids.caretop.com";
        public string? AgentID { get; set; }
        public bool UseRelay => !string.IsNullOrEmpty(RelayServerURL);

        public bool IsConnected => UseRelay 
            ? (_ws?.State == WebSocketState.Open)
            : (_client?.Connected ?? false);

        public async Task<bool> ConnectAsync(string host, int port)
        {
            if (UseRelay)
            {
                return await ConnectViaRelayAsync();
            }
            else
            {
                return await ConnectDirectAsync(host, port);
            }
        }

        private async Task<bool> ConnectDirectAsync(string host, int port)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(host, port);
                _stream = _client.GetStream();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> ConnectViaRelayAsync()
        {
            if (string.IsNullOrEmpty(AgentID) || string.IsNullOrEmpty(RelayServerURL))
                return false;

            try
            {
                var agentInfo = await GetAgentInfoAsync(AgentID);
                if (agentInfo == null || !agentInfo.Value.online)
                {
                    Console.WriteLine("Agent is not online");
                    return false;
                }

                _ws = new ClientWebSocket();
                _cts = new CancellationTokenSource();

                var wsUrl = RelayServerURL!.TrimEnd('/').Replace("http://", "ws://").Replace("https://", "wss://");
                await _ws.ConnectAsync(new Uri($"{wsUrl}/ws/controller"), _cts.Token);

                var connectMsg = new { type = "connect", agent_id = AgentID };
                await _ws.SendAsync(
                    new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(connectMsg))),
                    WebSocketMessageType.Text,
                    true,
                    _cts.Token);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Relay connection error: {ex.Message}");
                return false;
            }
        }

        public async Task<(bool online, string? machineName, string? hostname, string? os)?> GetAgentInfoAsync(string agentID)
        {
            if (string.IsNullOrEmpty(RelayServerURL))
                return null;

            try
            {
                var client = new HttpClient();
                var response = await client.GetAsync($"{RelayServerURL}/api/v1/agent/{agentID}");
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                return (
                    online: doc.RootElement.TryGetProperty("online", out var o) && o.GetBoolean(),
                    machineName: doc.RootElement.TryGetProperty("machine_name", out var m) ? m.GetString() : null,
                    hostname: doc.RootElement.TryGetProperty("hostname", out var h) ? h.GetString() : null,
                    os: doc.RootElement.TryGetProperty("os", out var os) ? os.GetString() : null
                );
            }
            catch
            {
                return null;
            }
        }

        public async Task<string?> ReadLineAsync()
        {
            if (UseRelay && _ws != null)
            {
                var buffer = new byte[8192];
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                if (result.Count > 0)
                    return Encoding.UTF8.GetString(buffer, 0, result.Count);
                return null;
            }

            byte[] buffer2 = new byte[4096];
            int bytesRead = await _stream!.ReadAsync(buffer2, 0, buffer2.Length);
            if (bytesRead > 0)
            {
                return Encoding.UTF8.GetString(buffer2, 0, bytesRead);
            }
            return null;
        }

        public async Task WriteAsync(string data)
        {
            if (UseRelay && _ws != null)
            {
                await _ws.SendAsync(
                    new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
                return;
            }

            byte[] buffer = Encoding.UTF8.GetBytes(data);
            await _stream!.WriteAsync(buffer, 0, buffer.Length);
        }

        public async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            if (UseRelay && _ws != null)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer, offset, count), CancellationToken.None);
                return result.Count;
            }

            return await _stream!.ReadAsync(buffer, offset, count);
        }

        public async Task WriteFrameAsync(byte[] data, byte frameType)
        {
            if (UseRelay && _ws != null)
            {
                var length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length + 1));
                var frame = new byte[4 + 1 + data.Length];
                Buffer.BlockCopy(length, 0, frame, 0, 4);
                frame[4] = frameType;
                Buffer.BlockCopy(data, 0, frame, 5, data.Length);

                await _ws.SendAsync(
                    new ArraySegment<byte>(frame),
                    WebSocketMessageType.Binary,
                    true,
                    CancellationToken.None);
                return;
            }

            byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length + 1));
            byte[] typePrefix = new byte[] { frameType };

            await _stream!.WriteAsync(lengthPrefix, 0, lengthPrefix.Length);
            await _stream.WriteAsync(typePrefix, 0, typePrefix.Length);
            await _stream.WriteAsync(data, 0, data.Length);
        }

        // 发送控制指令（文本命令，如 MOUSE_MOVE:x,y）
        public async Task SendControlAsync(string command)
        {
            var bytes = Encoding.UTF8.GetBytes(command);
            await SendControlAsync(bytes);
        }

        // 发送原始字节数据（用于认证等）
        public async Task SendControlAsync(byte[] data)
        {
            if (UseRelay && _ws != null)
            {
                await _ws.SendAsync(
                    new ArraySegment<byte>(data),
                    WebSocketMessageType.Binary,
                    true,
                    _cts!.Token);
            }
            else if (_stream != null)
            {
                await _stream.WriteAsync(data, 0, data.Length);
            }
        }

        // 接收指定字节数
        public async Task<byte[]> ReceiveAsync(int count)
        {
            if (UseRelay && _ws != null)
            {
                var buffer = new byte[count];
                int totalRead = 0;
                while (totalRead < count)
                {
                    var chunk = new byte[count - totalRead];
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(chunk), _cts!.Token);
                    if (result.Count == 0) break;
                    Buffer.BlockCopy(chunk, 0, buffer, totalRead, result.Count);
                    totalRead += result.Count;
                }
                return totalRead == count ? buffer : buffer.Take(totalRead).ToArray();
            }

            var result2 = new byte[count];
            int read = await _stream!.ReadAsync(result2, 0, count);
            return read == count ? result2 : result2.Take(read).ToArray();
        }

        public void Disconnect()
        {
            if (UseRelay && _ws != null)
            {
                _cts?.Cancel();
                try
                {
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait();
                }
                catch { }
                _ws.Dispose();
                _ws = null;
                _cts?.Dispose();
                _cts = null;
            }
            else
            {
                _stream?.Close();
                _client?.Close();
                _stream = null;
                _client = null;
            }
        }
    }
}