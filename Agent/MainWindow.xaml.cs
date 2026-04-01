using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Agent
{
    public partial class MainWindow : Window
    {
        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly ScreenCaptureService _screenCapture;
        private readonly CommandExecutor _commandExecutor;
        private readonly SystemTrayService _trayService;
        private readonly DispatcherTimer _captureTimer;
        private bool _isRunning;
        private bool _isConnected;
        private string _password = "default_password";

        public MainWindow()
        {
            InitializeComponent();

            _screenCapture = new ScreenCaptureService();
            _commandExecutor = new CommandExecutor();
            _trayService = new SystemTrayService(this);

            _captureTimer = new DispatcherTimer();
            _captureTimer.Interval = TimeSpan.FromMilliseconds(100);
            _captureTimer.Tick += CaptureTimer_Tick;

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Ready - Click Start to begin listening");
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRunning)
            {
                StartServer();
            }
            else
            {
                StopServer();
            }
        }

        private void StartServer()
        {
            try
            {
                int port = int.Parse(PortTextBox.Text);
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                _isRunning = true;
                StartButton.Content = "Stop";
                UpdateStatus($"Listening on port {port}...");

                _ = AcceptClientAsync();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
            }
        }

        private void StopServer()
        {
            _isRunning = false;
            _captureTimer.Stop();
            DisconnectClient();
            _listener?.Stop();
            _listener = null;
            StartButton.Content = "Start";
            ConnectionStatus.Status = StatusIndicator.StatusType.Gray;
            UpdateStatus("Server stopped");
        }

        private async Task AcceptClientAsync()
        {
            while (_isRunning)
            {
                try
                {
                    _client = await _listener!.AcceptTcpClientAsync();
                    _stream = _client.GetStream();
                    UpdateStatus("Client connected, awaiting authentication...");

                    if (await AuthenticateClientAsync())
                    {
                        _isConnected = true;
                        ConnectionStatus.Status = StatusIndicator.StatusType.Green;
                        UpdateStatus("Client authenticated successfully");
                        _ = HandleClientAsync();
                    }
                    else
                    {
                        UpdateStatus("Authentication failed");
                        DisconnectClient();
                    }
                }
                catch (Exception ex) when (_isRunning)
                {
                    UpdateStatus($"Connection error: {ex.Message}");
                    DisconnectClient();
                }
            }
        }

        private async Task<bool> AuthenticateClientAsync()
        {
            try
            {
                byte[] challenge = new byte[32];
                RandomNumberGenerator.Fill(challenge);

                await _stream!.WriteAsync(challenge);

                byte[] buffer = new byte[1024];
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                if (response.StartsWith("PASSWORD:"))
                {
                    string receivedPassword = response.Substring(9);
                    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(_password));
                    byte[] expectedHash = SHA256.HashData(challenge.Concat(Encoding.UTF8.GetBytes(_password)).ToArray());

                    using var sha256 = SHA256.Create();
                    byte[] computedHash = sha256.ComputeHash(challenge.Concat(Encoding.UTF8.GetBytes(_password)).ToArray());

                    byte[] authResult = new byte[1];
                    authResult[0] = computedHash.SequenceEqual(expectedHash) ? (byte)0 : (byte)1;
                    await _stream.WriteAsync(authResult);

                    return authResult[0] == 0;
                }

                byte[] failResult = new byte[1] { 1 };
                await _stream.WriteAsync(failResult);
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task HandleClientAsync()
        {
            try
            {
                (int width, int height) = _screenCapture.GetScreenSize();
                await SendScreenSizeAsync(width, height);

                _captureTimer.Start();

                byte[] buffer = new byte[4096];
                while (_isConnected && _client?.Connected == true)
                {
                    try
                    {
                        int bytesRead = await _stream!.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;

                        string command = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        ProcessCommand(command);
                    }
                    catch { break; }
                }
            }
            catch { }
            finally
            {
                _captureTimer.Stop();
                Dispatcher.Invoke(DisconnectClient);
            }
        }

        private void ProcessCommand(string command)
        {
            if (command.StartsWith("MOUSE_MOVE:"))
            {
                string[] parts = command.Substring(11).Split(',');
                if (parts.Length == 2)
                {
                    int x = int.Parse(parts[0]);
                    int y = int.Parse(parts[1]);
                    _commandExecutor.MoveMouse(x, y);
                }
            }
            else if (command.StartsWith("MOUSE_DOWN:"))
            {
                string[] parts = command.Substring(11).Split(',');
                if (parts.Length == 2)
                {
                    int x = int.Parse(parts[0]);
                    int y = int.Parse(parts[1]);
                    _commandExecutor.MouseDown(x, y);
                }
            }
            else if (command.StartsWith("MOUSE_UP:"))
            {
                string[] parts = command.Substring(9).Split(',');
                if (parts.Length == 2)
                {
                    int x = int.Parse(parts[0]);
                    int y = int.Parse(parts[1]);
                    _commandExecutor.MouseUp(x, y);
                }
            }
            else if (command.StartsWith("KEY_PRESS:"))
            {
                string vkCodeStr = command.Substring(10);
                if (int.TryParse(vkCodeStr, out int vkCode))
                {
                    _commandExecutor.KeyPress(vkCode);
                }
            }
            else if (command.StartsWith("KEY_TYPE:"))
            {
                string text = command.Substring(9);
                _commandExecutor.TypeText(text);
            }
            else if (command.StartsWith("FILE_RECV:"))
            {
                string[] parts = command.Substring(10).Split(':');
                if (parts.Length >= 2)
                {
                    string filename = parts[0];
                    long fileSize = long.Parse(parts[1]);
                    _commandExecutor.ReceiveFile(filename, fileSize, _stream!);
                }
            }
        }

        private async void CaptureTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isConnected || _stream == null) return;

            try
            {
                byte[] frameData = _screenCapture.CaptureScreen();

                byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(frameData.Length + 1));
                byte[] frameType = new byte[1] { 1 };

                await _stream.WriteAsync(lengthPrefix);
                await _stream.WriteAsync(frameType);
                await _stream.WriteAsync(frameData);
            }
            catch
            {
                _captureTimer.Stop();
            }
        }

        private async Task SendScreenSizeAsync(int width, int height)
        {
            byte[] sizeData = new byte[8];
            BitConverter.GetBytes(IPAddress.HostToNetworkOrder(width)).CopyTo(sizeData, 0);
            BitConverter.GetBytes(IPAddress.HostToNetworkOrder(height)).CopyTo(sizeData, 4);

            byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(9));
            byte[] frameType = new byte[1] { 2 };

            await _stream!.WriteAsync(lengthPrefix);
            await _stream.WriteAsync(frameType);
            await _stream.WriteAsync(sizeData);
        }

        private void DisconnectClient()
        {
            _isConnected = false;
            _captureTimer.Stop();
            ConnectionStatus.Status = StatusIndicator.StatusType.Gray;

            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }

            _stream = null;
            _client = null;

            if (_isRunning)
            {
                UpdateStatus("Disconnected - Waiting for connection...");
            }
        }

        private void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
            });
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _trayService.Dispose();
            StopServer();
            base.OnClosing(e);
        }
    }
}
