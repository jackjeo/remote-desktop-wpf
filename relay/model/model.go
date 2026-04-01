package model

import (
	"sync"
	"time"

	"github.com/gorilla/websocket"
)

type Agent struct {
	AgentID      string    `json:"agent_id"`
	MachineName  string    `json:"machine_name"`
	Hostname     string    `json:"hostname"`
	OS           string    `json:"os"`
	PasswordHash string    `json:"password_hash,omitempty"`
	PublicIP     string    `json:"public_ip,omitempty"`
	LocalPort    int       `json:"local_port"`
	Online       bool      `json:"online"`
	LastHeartbeat time.Time `json:"last_heartbeat"`
	ws           *websocket.Conn
	mu           sync.RWMutex
}

type AgentRegisterRequest struct {
	MachineName  string `json:"machine_name" binding:"required"`
	Hostname     string `json:"hostname" binding:"required"`
	OS           string `json:"os" binding:"required"`
	Port         int    `json:"port" binding:"required"`
	PasswordHash string `json:"password_hash"`
}

type AgentRegisterResponse struct {
	AgentID     string `json:"agent_id"`
	ServerPort  int    `json:"server_port"`
}

type Tunnel struct {
	controllerConn *websocket.Conn
	agentConn      *websocket.Conn
	agentID        string
	done           chan struct{}
}

type TunnelManager struct {
	mu      sync.RWMutex
	tunnels map[string][]*Tunnel
}

func NewTunnelManager() *TunnelManager {
	return &TunnelManager{
		tunnels: make(map[string][]*Tunnel),
	}
}

func (tm *TunnelManager) AddTunnel(agentID string, t *Tunnel) {
	tm.mu.Lock()
	defer tm.mu.Unlock()
	tm.tunnels[agentID] = append(tm.tunnels[agentID], t)
}

func (tm *TunnelManager) RemoveTunnel(agentID string, t *Tunnel) {
	tm.mu.Lock()
	defer tm.mu.Unlock()
	tunnels := tm.tunnels[agentID]
	for i, tunnel := range tunnels {
		if tunnel == t {
			tm.tunnels[agentID] = append(tunnels[:i], tunnels[i+1:]...)
			break
		}
	}
}

func (tm *TunnelManager) GetTunnelCount(agentID string) int {
	tm.mu.RLock()
	defer tm.mu.RUnlock()
	return len(tm.tunnels[agentID])
}

type WSMessage struct {
	Type    string `json:"type"`
	AgentID string `json:"agent_id,omitempty"`
	Time    int64  `json:"time,omitempty"`
}