using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Controller
{
    public partial class MainWindow : Window
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
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
                string host = HostTextBox.Text;
                int port = int.Parse(PortTextBox.Text);
                string password = PasswordBox.Password;

                ConnectButton.IsEnabled = false;
                UpdateStatus($"Connecting to {host}:{port}...");

                _client = new TcpClient();
                await _client.ConnectAsync(host, port);
                _stream = _client.GetStream();

                UpdateStatus("Authenticating...");

                if (await AuthenticateAsync(password))
                {
                    _isConnected = true;
                    ConnectionStatus.Status = StatusIndicator.StatusType.Green;
                    ConnectButton.IsEnabled = false;
                    DisconnectButton.IsEnabled = true;
                    TransferFileButton.IsEnabled = true;
                    NoSignalText.Visibility = Visibility.Collapsed;
                    UpdateStatus("Connected");

                    _receiveCts = new CancellationTokenSource();
                    _ = ReceiveDataAsync(_receiveCts.Token);
                }
                else
                {
                    UpdateStatus("Authentication failed");
                    Disconnect();
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
                int bytesRead = await _stream!.ReadAsync(challenge);
                if (bytesRead != 32)
                {
                    return false;
                }

                using var sha256 = SHA256.Create();
                byte[] hash = sha256.ComputeHash(challenge.Concat(Encoding.UTF8.GetBytes(password)).ToArray());

                string authMessage = "PASSWORD:" + password + "\n";
                byte[] authBytes = Encoding.UTF8.GetBytes(authMessage);
                await _stream.WriteAsync(authBytes);

                byte[] result = new byte[1];
                bytesRead = await _stream.ReadAsync(result);
                if (bytesRead != 1)
                {
                    return false;
                }

                return result[0] == 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task ReceiveDataAsync(CancellationToken token)
        {
            byte[] headerBuffer = new byte[5];

            try
            {
                while (!token.IsCancellationRequested && _client?.Connected == true)
                {
                    int bytesRead = await ReadExactAsync(headerBuffer, 5, token);
                    if (bytesRead == 0) break;

                    int frameLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(headerBuffer, 0));
                    byte frameType = headerBuffer[4];

                    if (frameLength < 1 || frameLength > 10 * 1024 * 1024) continue;

                    byte[] frameData = new byte[frameLength - 1];
                    bytesRead = await ReadExactAsync(frameData, frameLength - 1, token);
                    if (bytesRead == 0) break;

                    ProcessFrame(frameType, frameData);
                }
            }
            catch (OperationCanceledException) { }
            catch { }

            Dispatcher.Invoke(Disconnect);
        }

        private async Task<int> ReadExactAsync(byte[] buffer, int count, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int bytesRead = await _stream!.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), token);
                if (bytesRead == 0) return totalRead;
                totalRead += bytesRead;
            }
            return totalRead;
        }

        private void ProcessFrame(byte frameType, byte[] data)
        {
            switch (frameType)
            {
                case 1:
                    _screenDisplay.UpdateFrame(data);
                    Dispatcher.Invoke(() =>
                    {
                        InfoText.Text = $"Frame received: {data.Length} bytes";
                    });
                    break;

                case 2:
                    if (data.Length >= 8)
                    {
                        _remoteWidth = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 0));
                        _remoteHeight = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 4));
                        Dispatcher.Invoke(() =>
                        {
                            ResolutionText.Text = $"Resolution: {_remoteWidth} x {_remoteHeight}";
                            _inputService.SetScreenSize(_remoteWidth, _remoteHeight);
                        });
                    }
                    break;

                case 3:
                    if (data.Length >= 1)
                    {
                        bool success = data[0] == 0;
                        Dispatcher.Invoke(() =>
                        {
                            if (!success)
                            {
                                UpdateStatus("Auth failed");
                                Disconnect();
                            }
                        });
                    }
                    break;
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();
        }

        private void Disconnect()
        {
            _receiveCts?.Cancel();

            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }

            _stream = null;
            _client = null;
            _isConnected = false;

            ConnectionStatus.Status = StatusIndicator.StatusType.Gray;
            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
            TransferFileButton.IsEnabled = false;
            NoSignalText.Visibility = Visibility.Visible;
            ResolutionText.Text = "";

            UpdateStatus("Disconnected");
        }

        private void ScreenImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isConnected || _remoteWidth == 0 || _remoteHeight == 0) return;

            Point pos = e.GetPosition(ScreenImage);

            double scaleX = _remoteWidth / ScreenImage.ActualWidth;
            double scaleY = _remoteHeight / ScreenImage.ActualHeight;

            int remoteX = (int)(pos.X * scaleX);
            int remoteY = (int)(pos.Y * scaleY);

            remoteX = Math.Max(0, Math.Min(_remoteWidth - 1, remoteX));
            remoteY = Math.Max(0, Math.Min(_remoteHeight - 1, remoteY));

            if (_isDragging)
            {
                SendCommand($"MOUSE_MOVE:{remoteX},{remoteY}");
            }
        }

        private void ScreenImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isConnected) return;

            _isDragging = true;
            ScreenImage.CaptureMouse();

            Point pos = e.GetPosition(ScreenImage);
            var (remoteX, remoteY) = GetRemoteCoordinates(pos);

            SendCommand($"MOUSE_DOWN:{remoteX},{remoteY}");
        }

        private void ScreenImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isConnected) return;

            _isDragging = false;
            ScreenImage.ReleaseMouseCapture();

            Point pos = e.GetPosition(ScreenImage);
            var (remoteX, remoteY) = GetRemoteCoordinates(pos);

            SendCommand($"MOUSE_UP:{remoteX},{remoteY}");
        }

        private (int x, int y) GetRemoteCoordinates(Point pos)
        {
            double scaleX = _remoteWidth / ScreenImage.ActualWidth;
            double scaleY = _remoteHeight / ScreenImage.ActualHeight;

            int remoteX = (int)(pos.X * scaleX);
            int remoteY = (int)(pos.Y * scaleY);

            remoteX = Math.Max(0, Math.Min(_remoteWidth - 1, remoteX));
            remoteY = Math.Max(0, Math.Min(_remoteHeight - 1, remoteY));

            return (remoteX, remoteY);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (!_isConnected) return;

            int? vkCode = GetVirtualKeyCode(e);
            if (vkCode.HasValue)
            {
                SendCommand($"KEY_PRESS:{vkCode.Value}");
                e.Handled = true;
            }

            base.OnPreviewKeyDown(e);
        }

        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            if (!_isConnected) return;

            string text = e.Text;
            if (!string.IsNullOrEmpty(text))
            {
                SendCommand($"KEY_TYPE:{text}");
                e.Handled = true;
            }

            base.OnPreviewTextInput(e);
        }

        private int? GetVirtualKeyCode(KeyEventArgs e)
        {
            if (e.Key >= Key.A && e.Key <= Key.Z)
                return (int)(e.Key - Key.A) + 0x41;

            if (e.Key >= Key.D0 && e.Key <= Key.D9)
                return (int)(e.Key - Key.D0) + 0x30;

            if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
                return (int)(e.Key - Key.NumPad0) + 0x60;

            return e.Key switch
            {
                Key.Enter => 0x0D,
                Key.Escape => 0x1B,
                Key.Space => 0x20,
                Key.Tab => 0x09,
                Key.Back => 0x08,
                Key.Delete => 0x2E,
                Key.Left => 0x25,
                Key.Up => 0x26,
                Key.Right => 0x27,
                Key.Down => 0x28,
                Key.Home => 0x24,
                Key.End => 0x23,
                Key.PageUp => 0x21,
                Key.PageDown => 0x22,
                Key.F1 => 0x70,
                Key.F2 => 0x71,
                Key.F3 => 0x72,
                Key.F4 => 0x73,
                Key.F5 => 0x74,
                Key.F6 => 0x75,
                Key.F7 => 0x76,
                Key.F8 => 0x77,
                Key.F9 => 0x78,
                Key.F10 => 0x79,
                Key.F11 => 0x7A,
                Key.F12 => 0x7B,
                _ => null
            };
        }

        private void SendCommand(string command)
        {
            if (_stream == null || !_isConnected) return;

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(command);
                _stream.Write(data, 0, data.Length);
            }
            catch { }
        }

        private async void TransferFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected) return;

            var dialog = new OpenFileDialog
            {
                Title = "Select file to transfer",
                Filter = "All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    TransferProgress.Visibility = Visibility.Visible;
                    TransferProgress.Value = 0;
                    TransferStatusText.Text = $"Sending: {dialog.FileName}";

                    await _fileTransfer.SendFileAsync(dialog.FileName, _stream!,
                        progress =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            TransferProgress.Value = progress;
                            TransferStatusText.Text = $"Progress: {progress}%";
                        });
                    });

                    TransferStatusText.Text = "Transfer complete";
                }
                catch (Exception ex)
                {
                    TransferStatusText.Text = $"Error: {ex.Message}";
                }
                finally
                {
                    TransferProgress.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }
    }
}
