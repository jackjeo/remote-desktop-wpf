# Remote Desktop WPF

A Windows remote desktop control tool built with C# WPF (.NET 8).

## Features

### Agent (Server)
- TcpListener-based server with SHA256 password authentication
- Screen capture using `Graphics.CopyFromScreen` + JPEG compression (70%)
- Executes mouse/keyboard commands: `SetCursorPos`, `mouse_event`, `SendKeys`
- System tray icon with context menu
- Supports file transfer

### Controller (Client)
- TcpClient connection to Agent
- Real-time screen display via WPF Image control
- Mouse control: move, click, double-click, drag (coordinate scaling)
- Keyboard event forwarding
- File upload to Agent
- Beautiful dark theme (VSCode style)

## Tech Stack
- C# / .NET 8 WPF
- System.Drawing.Common (screen capture)
- Hardcodet.NotifyIcon.Wpf (system tray)
- TCP binary protocol (4-byte length + 1-byte type + data)

## Getting Started

### Build
```bash
# Agent
cd Agent && dotnet restore && dotnet build

# Controller
cd Controller && dotnet restore && dotnet build
```

### Run
```bash
# Start Agent on the remote machine
dotnet run

# Start Controller on your machine
dotnet run
```

### Connect
1. Start Agent → enter port (default 55555) and password → click Start
2. Start Controller → enter IP, port, password → click Connect
3. Remote screen will display in real-time

## Network Protocol
- Frame: 4-byte length (big-endian) + 1-byte type + data
- Type 1: Screen frame (JPEG)
- Type 2: Screen size (width + height, 4 bytes each)
- Type 3: Auth result (1 byte: 0=success, 1=fail)
- Type 10: File data

## License
MIT
