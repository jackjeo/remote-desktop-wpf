using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Agent
{
    public class RelayClient
    {
        private string _relayServerUrl;
        private string _agentId;
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private int _localPort;
        private string _machineName;
        private string _hostname;
        private string _os;
        private Timer _heartbeatTimer;

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler? Connected;
        public event EventHandler? Disconnected;
        public event EventHandler<string>? Error;

        public bool IsConnected => _ws?.State == WebSocketState.Open;
        public string? AgentId => _agentId;

        public RelayClient(string relayServerUrl, int localPort, string machineName, string hostname, string os)
        {
            _relayServerUrl = relayServerUrl.TrimEnd('/');
            _localPort = localPort;
            _machineName = machineName;
            _hostname = hostname;
            _os = os;
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();
        }

        public async Task<bool> RegisterAsync()
        {
            try
            {
                var client = new HttpClient();
                var request = new
                {
                    machine_name = _machineName,
                    hostname = _hostname,
                    os = _os,
                    port = _localPort
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"{_relayServerUrl}/api/v1/agent/register", content);
                if (!response.IsSuccessStatusCode)
                {
                    Error?.Invoke(this, $"Registration failed: {response.StatusCode}");
                    return false;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(responseJson);
                _agentId = doc.RootElement.GetProperty("agent_id").GetString();

                if (string.IsNullOrEmpty(_agentId))
                {
                    Error?.Invoke(this, "Invalid agent_id received");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, $"Registration error: {ex.Message}");
                return false;
            }
        }

        public async Task ConnectAsync()
        {
            if (string.IsNullOrEmpty(_agentId))
            {
                if (!await RegisterAsync())
                    return;
            }

            try
            {
                _ws = new ClientWebSocket();
                var wsUrl = _relayServerUrl.Replace("http://", "ws://").Replace("https://", "wss://");
                await _ws.ConnectAsync(new Uri($"{wsUrl}/ws/agent?agent_id={_agentId}"), _cts.Token);

                var registerMsg = new { type = "register", agent_id = _agentId };
                await SendMessageAsync(JsonSerializer.Serialize(registerMsg));

                StartHeartbeat();

                Connected?.Invoke(this, EventArgs.Empty);

                _ = ReceiveLoopAsync();
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, $"WebSocket connection error: {ex.Message}");
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        private void StartHeartbeat()
        {
            _heartbeatTimer = new Timer(async _ =>
            {
                if (IsConnected)
                {
                    try
                    {
                        var heartbeat = new { type = "heartbeat", time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                        await SendMessageAsync(JsonSerializer.Serialize(heartbeat));
                    }
                    catch { }
                }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8192];
            while (_ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        var data = new byte[result.Count];
                        Array.Copy(buffer, data, result.Count);
                        DataReceived?.Invoke(this, data);
                    }
                }
                catch (WebSocketException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        public async Task SendMessageAsync(string message)
        {
            if (_ws.State != WebSocketState.Open)
                return;

            var bytes = Encoding.UTF8.GetBytes(message);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        public async Task SendFrameAsync(byte[] data, byte frameType)
        {
            if (_ws.State != WebSocketState.Open)
                return;

            var length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length + 1));
            var frame = new byte[4 + 1 + data.Length];
            Buffer.BlockCopy(length, 0, frame, 0, 4);
            frame[4] = frameType;
            Buffer.BlockCopy(data, 0, frame, 5, data.Length);

            await _ws.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, _cts.Token);
        }

        public void Disconnect()
        {
            _cts.Cancel();
            _heartbeatTimer?.Dispose();
            if (_ws.State == WebSocketState.Open)
            {
                try
                {
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait();
                }
                catch { }
            }
            _ws.Dispose();
        }
    }
}