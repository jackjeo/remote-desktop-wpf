package tunnel

import (
	"encoding/binary"
	"io"
	"log"
	"sync"
	"time"

	"github.com/gorilla/websocket"
	"relay/agent"
)

const (
	writeWait      = 10 * time.Second
	pongWait       = 60 * time.Second
	pingPeriod     = (pongWait * 9) / 10
	maxMessageSize = 512
)

type TunnelManager struct {
	mu      sync.RWMutex
	tunnels map[string]map[*Tunnel]bool
}

var Manager *TunnelManager

func Init() {
	Manager = &TunnelManager{
		tunnels: make(map[string]map[*Tunnel]bool),
	}
}

func (tm *TunnelManager) CreateTunnel(agentID string, controllerConn *websocket.Conn) *Tunnel {
	agentConn := agent.GetWebSocket(agentID)
	if agentConn == nil {
		log.Printf("Agent %s not found or offline", agentID)
		return nil
	}

	tunnel := &Tunnel{
		controllerConn: controllerConn,
		agentConn:      agentConn,
		agentID:        agentID,
		done:           make(chan struct{}),
	}

	tm.mu.Lock()
	if tm.tunnels[agentID] == nil {
		tm.tunnels[agentID] = make(map[*Tunnel]bool)
	}
	tm.tunnels[agentID][tunnel] = true
	tm.mu.Unlock()

	log.Printf("Tunnel created for agent %s", agentID)

	go tunnel.start()

	return tunnel
}

func (tm *TunnelManager) RemoveTunnel(tunnel *Tunnel) {
	tm.mu.Lock()
	defer tm.mu.Unlock()
	if tunnels, ok := tm.tunnels[tunnel.agentID]; ok {
		delete(tunnels, tunnel)
		if len(tunnels) == 0 {
			delete(tm.tunnels, tunnel.agentID)
		}
	}
	log.Printf("Tunnel removed for agent %s", tunnel.agentID)
}

func (t *Tunnel) start() {
	defer func() {
		close(t.done)
		Manager.RemoveTunnel(t)
		t.Close()
	}()

	go t.pingController()
	go t.pingAgent()

	var wg sync.WaitGroup
	wg.Add(2)

	go func() {
		defer wg.Done()
		t.proxyControllerToAgent()
	}()

	go func() {
		defer wg.Done()
		t.proxyAgentToController()
	}()

	wg.Wait()
}

func (t *Tunnel) pingController() {
	ticker := time.NewTicker(pingPeriod)
	defer ticker.Stop()
	for {
		select {
		case <-t.done:
			return
		case <-ticker.C:
			if err := t.controllerConn.WriteControl(websocket.PingMessage, nil, time.Now().Add(writeWait)); err != nil {
				return
			}
		}
	}
}

func (t *Tunnel) pingAgent() {
	ticker := time.NewTicker(pingPeriod)
	defer ticker.Stop()
	for {
		select {
		case <-t.done:
			return
		case <-ticker.C:
			if err := t.agentConn.WriteControl(websocket.PingMessage, nil, time.Now().Add(writeWait)); err != nil {
				return
			}
		}
	}
}

func (t *Tunnel) proxyControllerToAgent() {
	defer t.Close()
	for {
		select {
		case <-t.done:
			return
		default:
			_, data, err := t.controllerConn.ReadMessage()
			if err != nil {
				return
			}
			if err := t.writeFrame(t.agentConn, data); err != nil {
				return
			}
		}
	}
}

func (t *Tunnel) proxyAgentToController() {
	defer t.Close()
	for {
		select {
		case <-t.done:
			return
		default:
			_, data, err := t.agentConn.ReadMessage()
			if err != nil {
				return
			}
			if err := t.writeFrame(t.controllerConn, data); err != nil {
				return
			}
		}
	}
}

func (t *Tunnel) writeFrame(ws *websocket.Conn, data []byte) error {
	if len(data) < 5 {
		return ws.WriteMessage(websocket.BinaryMessage, data)
	}

	length := binary.BigEndian.Uint32(data[0:4])
	frameType := data[4]
	payload := data[5:]

	frameData := make([]byte, 5+len(payload))
	binary.BigEndian.PutUint32(frameData[0:4], length)
	frameData[4] = frameType
	copy(frameData[5:], payload)

	return ws.WriteMessage(websocket.BinaryMessage, frameData)
}

func (t *Tunnel) Close() {
	select {
	case <-t.done:
	default:
		close(t.done)
	}
	if t.controllerConn != nil {
		t.controllerConn.Close()
	}
	if t.agentConn != nil {
		t.agentConn.Close()
	}
}