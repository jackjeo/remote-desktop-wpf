using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Controller
{
    public partial class MainWindow : Window
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly NetworkService _networkService;
        private readonly ScreenDisplayService _screenDisplay;
        private readonly InputService _inputService;
        private readonly FileTransferService _fileTransfer;
        private CancellationTokenSource? _receiveCts;
        private int _remoteWidth;
        private int _remoteHeight;
        private bool _isConnected;
        private bool _isDragging;

        public MainWindow()
        {
            InitializeComponent();

            // 硬编码中继服务器地址
            _networkService = new NetworkService();

            _screenDisplay = new ScreenDisplayService();
            _inputService = new InputService();
            _fileTransfer = new FileTransferService();

            ScreenImage.Source = _screenDisplay.ImageSource;
            NoSignalText.Visibility = Visibility.Visible;

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Ready to connect");
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            Disconnect();
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected) return;

            try
            {
                string host = HostTextBox.Text.Trim();
                int port = int.Parse(PortTextBox.Text.Trim());
                string agentId = AgentIdTextBox.Text.Trim();
                string password = PasswordBox.Password;

                ConnectButton.IsEnabled = false;
                UpdateStatus("Connecting...");

                // 如果填写了 AgentID，优先使用中继模式
                bool useRelay = !string.IsNullOrEmpty(agentId) && _networkService.UseRelay;

                if (useRelay)
                {
                    _networkService.AgentID = agentId;
                    UpdateStatus($"Connecting via relay ({_networkService.RelayServerURL})...");
                }
                else
                {
                    UpdateStatus($"Connecting to {host}:{port}...");
                }

                bool success = await _networkService.ConnectAsync(host, port);

                if (success)
                {
                    // 发送密码认证
                    bool authed = await AuthenticateAsync(password);
                    if (authed)
                    {
                        _isConnected = true;
                        ConnectionStatus.Status = StatusIndicator.StatusType.Green;
                        ConnectButton.IsEnabled = false;
                        DisconnectButton.IsEnabled = true;
                        TransferFileButton.IsEnabled = true;
                        NoSignalText.Visibility = Visibility.Collapsed;
                        UpdateStatus(useRelay ? "Connected via relay" : "Connected directly");

                        _receiveCts = new CancellationTokenSource();
                        _ = ReceiveDataAsync(_receiveCts.Token);
                    }
                    else
                    {
                        UpdateStatus("Authentication failed");
                        Disconnect();
                    }
                }
                else
                {
                    UpdateStatus("Connection failed");
                    ConnectButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Connection error: {ex.Message}");
                Disconnect();
            }
        }

        private async Task<bool> AuthenticateAsync(string password)
        {
            try
            {
                byte[] challenge = new byte[32];
                RandomNumberGenerator.Fill(challenge);

                byte[] challengeLen = BitConverter.GetBytes(challenge.Length);
                byte[] authPacket = new byte[1 + challenge.Length];
                authPacket[0] = (byte)0; // auth type: challenge
                Buffer.BlockCopy(challenge, 0, authPacket, 1, challenge.Length);

                if (_networkService.UseRelay)
                {
                    await _networkService.SendControlAsync(authPacket);
                }
                else
                {
                    await _stream!.WriteAsync(challengeLen);
                    await _stream!.WriteAsync(challenge);
                }

                // 读取响应
                byte[] response = new byte[1];
                if (_networkService.UseRelay)
                {
                    response = await _networkService.ReceiveAsync(1);
                }
                else
                {
                    int n = await _stream!.ReadAsync(response, 0, 1);
                    if (n == 0) return false;
                }

                return response[0] == 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task ReceiveDataAsync(CancellationToken ct)
        {
            byte[] header = new byte[5];
            try
            {
                while (!ct.IsCancellationRequested && _networkService.IsConnected)
                {
                    // 读取帧头：4字节长度 + 1字节类型
                    byte[] lenBuf = new byte[4];
                    if (_networkService.UseRelay)
                    {
                        lenBuf = await _networkService.ReceiveAsync(4);
                        if (lenBuf == null || lenBuf.Length < 4) break;
                    }
                    else
                    {
                        int n = await ReadFully(_stream!, lenBuf, 4);
                        if (n == 0) break;
                    }

                    int frameLen = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lenBuf, 0));

                    // 读取类型
                    byte[] typeBuf = new byte[1];
                    if (_networkService.UseRelay)
                    {
                        typeBuf = await _networkService.ReceiveAsync(1);
                    }
                    else
                    {
                        await _stream!.ReadAsync(typeBuf, 0, 1);
                    }
                    byte frameType = typeBuf[0];

                    // 读取数据
                    int dataLen = frameLen - 1;
                    byte[] data = new byte[dataLen];
                    int totalRead = 0;
                    while (totalRead < dataLen)
                    {
                        byte[] chunk;
                        if (_networkService.UseRelay)
                        {
                            chunk = await _networkService.ReceiveAsync(dataLen - totalRead);
                            if (chunk == null) break;
                        }
                        else
                        {
                            int n = await _stream!.ReadAsync(data, totalRead, dataLen - totalRead);
                            if (n == 0) { totalRead = 0; break; }
                            totalRead += n;
                            continue;
                        }
                        if (chunk == null || chunk.Length == 0) break;
                        Buffer.BlockCopy(chunk, 0, data, totalRead, chunk.Length);
                        totalRead += chunk.Length;
                    }

                    if (totalRead == 0) break;

                    ProcessFrame(frameType, data);
                }
            }
            catch { }
            finally
            {
                Dispatcher.Invoke(() => Disconnect());
            }
        }

        private void ProcessFrame(byte type, byte[] data)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (type == 1) // Screen frame (JPEG)
                    {
                        using var ms = new MemoryStream(data);
                        {
                            var bitmap = new System.Drawing.Bitmap(ms);
                            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                bitmap.GetHbitmap(),
                                IntPtr.Zero,
                                Int32Rect.Empty,
                                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                            bitmap.Dispose();
                            _screenDisplay.UpdateImage(source);
                        }
                        FpsText.Text = $"{DateTime.Now.Second}";
                    }
                    else if (type == 2) // Screen size
                    {
                        if (data.Length >= 8)
                        {
                            _remoteWidth = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 0));
                            _remoteHeight = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 4));
                            ResolutionText.Text = $"{_remoteWidth}x{_remoteHeight}";
                            _inputService.SetScreenSize(_remoteWidth, _remoteHeight);
                        }
                    }
                }
                catch { }
            });
        }

        private int ReadFully(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read == 0) return offset;
                offset += read;
            }
            return offset;
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();
        }

        private void Disconnect()
        {
            _receiveCts?.Cancel();
            _networkService.Disconnect();
            _isConnected = false;
            ConnectionStatus.Status = StatusIndicator.StatusType.Gray;
            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
            TransferFileButton.IsEnabled = false;
            NoSignalText.Visibility = Visibility.Visible;
            ResolutionText.Text = "-";
            UpdateStatus("Disconnected");
        }

        private void TransferFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select file to transfer"
            };
            if (dialog.ShowDialog() == true)
            {
                _ = _fileTransfer.SendFileAsync(dialog.FileName, _networkService);
            }
        }

        private void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }

        // ── 远程屏幕鼠标事件 ────────────────────────────────────
        private void ScreenImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isConnected) return;
            var pos = e.GetPosition(ScreenImage);
            var (x, y) = ScaleToRemote(pos.X, pos.Y);
            _ = _networkService.SendControlAsync($"MOUSE_DOWN:{x},{y}");
        }

        private void ScreenImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isConnected) return;
            var pos = e.GetPosition(ScreenImage);
            var (x, y) = ScaleToRemote(pos.X, pos.Y);
            _ = _networkService.SendControlAsync($"MOUSE_UP:{x},{y}");
        }

        private void ScreenImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isConnected || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(ScreenImage);
            var (x, y) = ScaleToRemote(pos.X, pos.Y);
            _ = _networkService.SendControlAsync($"MOUSE_MOVE:{x},{y}");
        }

        private (int x, int y) ScaleToRemote(double localX, double localY)
        {
            if (_remoteWidth == 0 || _remoteHeight == 0 || ScreenImage.ActualWidth == 0)
                return ((int)localX, (int)localY);

            double scaleX = _remoteWidth / ScreenImage.ActualWidth;
            double scaleY = _remoteHeight / ScreenImage.ActualHeight;
            return ((int)(localX * scaleX), (int)(localY * scaleY));
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();
            Close();
        }
    }
}
