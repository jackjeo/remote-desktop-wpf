package ws

import (
	"encoding/json"
	"log"
	"net/http"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/gorilla/websocket"
	"relay/agent"
	"relay/model"
	"relay/tunnel"
)

var upgrader = websocket.Upgrader{
	ReadBufferSize:  1024,
	WriteBufferSize: 1024,
	CheckOrigin: func(r *http.Request) bool {
		return true
	},
}

func AgentWebSocket(c *gin.Context) {
	agentID := c.Query("agent_id")
	if agentID == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "agent_id required"})
		return
	}

	ws, err := upgrader.Upgrade(c.Writer, c.Request, nil)
	if err != nil {
		log.Printf("Agent WebSocket upgrade error: %v", err)
		return
	}
	defer func() {
		agent.HandleAgentDisconnect(agentID)
		ws.Close()
	}()

	agent.SetOnline(agentID, ws)

	for {
		msg, err := readMessage(ws)
		if err != nil {
			log.Printf("Agent %s read error: %v", agentID, err)
			return
		}

		switch msg.Type {
		case "register":
			agent.HandleRegisterMessage(ws, model.WSMessage{
				Type:    msg.Type,
				AgentID: msg.AgentID,
			})
		case "heartbeat":
			agent.HandleHeartbeatMessage(model.WSMessage{
				Type:    msg.Type,
				AgentID: msg.AgentID,
				Time:    msg.Time,
			})
		}
	}
}

func ControllerWebSocket(c *gin.Context) {
	ws, err := upgrader.Upgrade(c.Writer, c.Request, nil)
	if err != nil {
		log.Printf("Controller WebSocket upgrade error: %v", err)
		return
	}
	defer ws.Close()

	for {
		msg, err := readMessage(ws)
		if err != nil {
			log.Printf("Controller read error: %v", err)
			return
		}

		switch msg.Type {
		case "connect":
			if msg.AgentID == "" {
				log.Printf("Controller connect: agent_id required")
				continue
			}
			log.Printf("Controller connecting to agent: %s", msg.AgentID)
			t := tunnel.Manager.CreateTunnel(msg.AgentID, ws)
			if t == nil {
				writeError(ws, "agent not available")
				return
			}
			return
		}
	}
}

func readMessage(ws *websocket.Conn) (*model.WSMessage, error) {
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

func writeError(ws *websocket.Conn, message string) {
	msg := model.WSMessage{Type: "error", AgentID: message}
	data, _ := json.Marshal(msg)
	ws.WriteMessage(websocket.TextMessage, data)
}

func StartAgentWS(addr string) error {
	r := gin.Default()
	r.GET("/ws/agent", AgentWebSocket)
	return r.Run(addr)
}

func StartControllerWS(addr string) error {
	r := gin.Default()
	r.GET("/ws/controller", ControllerWebSocket)
	return r.Run(addr)
}

func GinAgentWS() gin.HandlerFunc {
	return func(c *gin.Context) {
		AgentWebSocket(c)
	}
}

func GinControllerWS() gin.HandlerFunc {
	return func(c *gin.Context) {
		ControllerWebSocket(c)
	}
}

type WSHandler struct{}

func (h *WSHandler) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	if r.URL.Path == "/ws/agent" {
		AgentWebSocket(&gin.Context{})
	} else if r.URL.Path == "/ws/controller" {
		ControllerWebSocket(&gin.Context{})
	}
}

func SetReadDeadline(ws *websocket.Conn, timeout time.Duration) error {
	return ws.SetReadDeadline(time.Now().Add(timeout))
}

func SetWriteDeadline(ws *websocket.Conn, timeout time.Duration) error {
	return ws.SetWriteDeadline(time.Now().Add(timeout))
}