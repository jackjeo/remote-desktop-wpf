package agent

import (
	"encoding/json"
	"log"
	"sync"
	"time"

	"github.com/gorilla/websocket"
	"github.com/google/uuid"
	"relay/model"
)

type AgentManager struct {
	mu     sync.RWMutex
	agents map[string]*model.Agent
}

var (
	Manager     *AgentManager
	agentUpdate chan<- *model.Agent
)

const heartbeatTimeout = 90 * time.Second

func Init(updateChan chan<- *model.Agent) {
	Manager = &AgentManager{
		agents: make(map[string]*model.Agent),
	}
	agentUpdate = updateChan
	go cleanupLoop()
}

func cleanupLoop() {
	ticker := time.NewTicker(30 * time.Second)
	for range ticker.C {
		Manager.mu.Lock()
		now := time.Now()
		for agentID, agent := range Manager.agents {
			if agent.Online && now.Sub(agent.LastHeartbeat) > heartbeatTimeout {
				agent.Online = false
				agent.ws.Close()
				agent.ws = nil
				if agentUpdate != nil {
					agentUpdate <- agent
				}
				log.Printf("Agent %s heartbeat timeout, marked offline", agentID)
			}
		}
		Manager.mu.Unlock()
	}
}

func Register(req model.AgentRegisterRequest) *model.Agent {
	Manager.mu.Lock()
	defer Manager.mu.Unlock()

	agentID := uuid.New().String()
	agent := &model.Agent{
		AgentID:      agentID,
		MachineName:  req.MachineName,
		Hostname:     req.Hostname,
		OS:           req.OS,
		PasswordHash: req.PasswordHash,
		LocalPort:    req.Port,
		Online:       false,
		LastHeartbeat: time.Now(),
	}

	Manager.agents[agentID] = agent
	log.Printf("Agent registered: %s (%s)", agentID, req.MachineName)
	return agent
}

func Get(agentID string) *model.Agent {
	Manager.mu.RLock()
	defer Manager.mu.RUnlock()
	return Manager.agents[agentID]
}

func GetAll() []*model.Agent {
	Manager.mu.RLock()
	defer Manager.mu.RUnlock()
	agents := make([]*model.Agent, 0, len(Manager.agents))
	for _, agent := range Manager.agents {
		agents = append(agents, agent)
	}
	return agents
}

func Unregister(agentID string) bool {
	Manager.mu.Lock()
	defer Manager.mu.Unlock()
	if agent, ok := Manager.agents[agentID]; ok {
		if agent.ws != nil {
			agent.ws.Close()
		}
		delete(Manager.agents, agentID)
		log.Printf("Agent unregistered: %s", agentID)
		return true
	}
	return false
}

func SetOnline(agentID string, ws *websocket.Conn) bool {
	Manager.mu.Lock()
	defer Manager.mu.Unlock()
	if agent, ok := Manager.agents[agentID]; ok {
		agent.Online = true
		agent.ws = ws
		agent.LastHeartbeat = time.Now()
		if agentUpdate != nil {
			agentUpdate <- agent
		}
		log.Printf("Agent %s is now online", agentID)
		return true
	}
	return false
}

func SetOffline(agentID string) {
	Manager.mu.Lock()
	defer Manager.mu.Unlock()
	if agent, ok := Manager.agents[agentID]; ok {
		agent.Online = false
		if agent.ws != nil {
			agent.ws.Close()
		}
		agent.ws = nil
		if agentUpdate != nil {
			agentUpdate <- agent
		}
		log.Printf("Agent %s is now offline", agentID)
	}
}

func UpdateHeartbeat(agentID string) bool {
	Manager.mu.Lock()
	defer Manager.mu.Unlock()
	if agent, ok := Manager.agents[agentID]; ok {
		agent.LastHeartbeat = time.Now()
		return true
	}
	return false
}

func HandleRegisterMessage(ws *websocket.Conn, msg model.WSMessage) bool {
	return SetOnline(msg.AgentID, ws)
}

func HandleHeartbeatMessage(msg model.WSMessage) bool {
	return UpdateHeartbeat(msg.AgentID)
}

func HandleAgentDisconnect(agentID string) {
	SetOffline(agentID)
}

func GetWebSocket(agentID string) *websocket.Conn {
	Manager.mu.RLock()
	defer Manager.mu.RUnlock()
	if agent, ok := Manager.agents[agentID]; ok && agent.Online {
		return agent.ws
	}
	return nil
}

func ReadMessage(ws *websocket.Conn) (*model.WSMessage, error) {
	_, data, err := ws.ReadMessage()
	if err != nil {
		return nil, err
	}
	var msg model.WSMessage
	if err := json.Unmarshal(data, &msg); err != nil {
		return nil, err
	}
	return &msg, nil
}