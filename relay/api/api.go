package api

import (
	"net/http"

	"github.com/gin-gonic/gin"
	"relay/agent"
	"relay/model"
)

func SetupRouter() *gin.Engine {
	r := gin.Default()

	r.GET("/api/v1/health", HealthCheck)

	agents := r.Group("/api/v1/agent")
	{
		agents.GET("/:agent_id", GetAgent)
		agents.POST("/register", RegisterAgent)
		agents.DELETE("/:agent_id/unregister", UnregisterAgent)
		agents.GET("", ListAgents)
	}

	return r
}

func HealthCheck(c *gin.Context) {
	c.JSON(http.StatusOK, gin.H{
		"status": "ok",
	})
}

func GetAgent(c *gin.Context) {
	agentID := c.Param("agent_id")
	agent := agent.Get(agentID)
	if agent == nil {
		c.JSON(http.StatusNotFound, gin.H{"error": "agent not found"})
		return
	}

	c.JSON(http.StatusOK, gin.H{
		"agent_id":     agent.AgentID,
		"machine_name": agent.MachineName,
		"hostname":     agent.Hostname,
		"os":           agent.OS,
		"public_ip":    agent.PublicIP,
		"local_port":   agent.LocalPort,
		"online":       agent.Online,
	})
}

func RegisterAgent(c *gin.Context) {
	var req model.AgentRegisterRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
		return
	}

	agent := agent.Register(req)
	c.JSON(http.StatusCreated, model.AgentRegisterResponse{
		AgentID:    agent.AgentID,
		ServerPort: 8082,
	})
}

func UnregisterAgent(c *gin.Context) {
	agentID := c.Param("agent_id")
	if agent.Unregister(agentID) {
		c.JSON(http.StatusOK, gin.H{"message": "agent unregistered"})
	} else {
		c.JSON(http.StatusNotFound, gin.H{"error": "agent not found"})
	}
}

func ListAgents(c *gin.Context) {
	agents := agent.GetAll()
	response := make([]gin.H, 0, len(agents))
	for _, a := range agents {
		response = append(response, gin.H{
			"agent_id":     a.AgentID,
			"machine_name": a.MachineName,
			"hostname":     a.Hostname,
			"os":           a.OS,
			"online":       a.Online,
		})
	}
	c.JSON(http.StatusOK, response)
}