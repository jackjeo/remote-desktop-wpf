# Remote Desktop WPF

A Windows remote desktop control tool built with C# WPF (.NET 8), featuring a **Go relay server** for firewall/NAT traversal.

## Architecture

```
┌────────────┐      ┌─────────────┐      ┌────────────┐
│ Controller │◄────►│ Relay Server│◄────►│   Agent    │
│  (主控端)  │ WS   │  (Go/Docker)│ WS   │  (被控端)  │
└────────────┘      └─────────────┘      └────────────┘
                         │
                   ┌─────┴─────┐
                   │ REST API  │
                   │  :8080    │
                   └───────────┘
```

## Components

### 1. Relay Server (`relay/`) — Go + Docker
- **Language:** Go 1.22 + Gin + gorilla/websocket
- **Deployment:** Docker + docker-compose
- **Ports:**
  - `:8080` — REST API
  - `:8081` — Agent WebSocket
  - `:8082` — Controller WebSocket
- **Features:**
  - Agent registration with globally unique UUID
  - Bidirectional tunnel (controller ↔ agent via relay)
  - Heartbeat keep-alive (30s interval)
  - Multiple controllers can connect to same agent

### 2. Agent (`Agent/`) — .NET 8 WPF
- Runs on the remote Windows machine
- Registers to relay server automatically
- Screen capture: `Graphics.CopyFromScreen` + JPEG 70%
- Executes mouse/keyboard commands via relay tunnel
- System tray icon with context menu
- Supports both **direct mode** (LAN) and **relay mode** (Internet)

### 3. Controller (`Controller/`) — .NET 8 WPF
- Runs on your Windows machine
- Connect via Agent ID (UUID) instead of IP/port
- Query agent status via relay REST API
- Real-time screen display
- Full mouse + keyboard control

## Quick Start

### 1. Deploy Relay Server

```bash
cd relay
cp .env.example .env
docker-compose up -d
```

### 2. Build & Run Agent

```bash
cd Agent
dotnet restore && dotnet build && dotnet run
```
→ Agent auto-registers to relay, receives a UUID shown in UI

### 3. Build & Run Controller

```bash
cd Controller
dotnet restore && dotnet build && dotnet run
```
→ Enter relay server URL + Agent UUID → Connect

## REST API (Relay Server)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/v1/health` | Health check |
| POST | `/api/v1/agent/register` | Register agent, returns UUID |
| GET | `/api/v1/agent/:agent_id` | Get agent info & online status |
| DELETE | `/api/v1/agent/:agent_id/unregister` | Unregister agent |
| GET | `/api/v1/agents` | List all agents |

### Register Example
```bash
curl -X POST http://localhost:8080/api/v1/agent/register \
  -H "Content-Type: application/json" \
  -d '{"machine_name":"Office-PC","hostname":"OFFICE-PC","os":"Windows 11","password_hash":"..."}'
```

### Query Agent
```bash
curl http://localhost:8080/api/v1/agent/550e8400-e29b-41d4-a716-446655440000
```

## WebSocket Protocol

### Agent connects
```
WS /ws/agent?agent_id=<uuid>
{"type":"register","agent_id":"<uuid>"}
{"type":"heartbeat","time":1234567890}
```

### Controller connects
```
WS /ws/controller
{"type":"connect","agent_id":"<uuid>"}
```

### Tunnel data (binary)
After tunnel established, raw binary frames are forwarded:
- 4-byte length (big-endian) + 1-byte type + data

## Tech Stack

| Component | Technology |
|-----------|------------|
| Relay Server | Go 1.22, Gin, gorilla/websocket |
| Agent & Controller | C# .NET 8 WPF |
| Screen Capture | System.Drawing.Common |
| System Tray | Hardcodet.NotifyIcon.Wpf |
| Container | Docker, docker-compose |

## License
MIT
