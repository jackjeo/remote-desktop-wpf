package main

import (
	"fmt"
	"log"
	"net/http"
	"sync"

	"github.com/gin-gonic/gin"
	"relay/agent"
	"relay/api"
	"relay/config"
	"relay/tunnel"
	"relay/ws"
)

func main() {
	cfg := config.Load()

	agentUpdate := make(chan *agent.Agent, 100)
	agent.Init(agentUpdate)
	tunnel.Init()

	var wg sync.WaitGroup

	wg.Add(1)
	go func() {
		defer wg.Done()
		startHTTPServer(cfg)
	}()

	wg.Add(1)
	go func() {
		defer wg.Done()
		startAgentWebSocket(cfg)
	}()

	wg.Add(1)
	go func() {
		defer wg.Done()
		startControllerWebSocket(cfg)
	}()

	log.Printf("Relay server started:")
	log.Printf("  HTTP API: http://0.0.0.0:%d", cfg.HTTPPort)
	log.Printf("  Agent WS: ws://0.0.0.0:%d/ws/agent", cfg.AgentWSPort)
	log.Printf("  Controller WS: ws://0.0.0.0:%d/ws/controller", cfg.ControllerWSPort)

	wg.Wait()
}

func startHTTPServer(cfg *config.Config) {
	r := gin.Default()

	r.Use(corsMiddleware())

	r.GET("/api/v1/health", func(c *gin.Context) {
		c.JSON(http.StatusOK, gin.H{"status": "ok"})
	})

	agents := r.Group("/api/v1/agent")
	{
		agents.GET("/:agent_id", api.GetAgent)
		agents.POST("/register", api.RegisterAgent)
		agents.DELETE("/:agent_id/unregister", api.UnregisterAgent)
		agents.GET("", api.ListAgents)
	}

	addr := fmt.Sprintf(":%d", cfg.HTTPPort)

	log.Printf("HTTP API listening on %s", addr)
	if err := r.Run(addr); err != nil {
		log.Fatalf("HTTP server error: %v", err)
	}
}

func startAgentWebSocket(cfg *config.Config) {
	r := gin.Default()
	r.Use(corsMiddleware())
	r.GET("/ws/agent", ws.GinAgentWS())

	addr := fmt.Sprintf(":%d", cfg.AgentWSPort)

	log.Printf("Agent WebSocket listening on %s", addr)
	if err := r.Run(addr); err != nil {
		log.Fatalf("Agent WebSocket server error: %v", err)
	}
}

func startControllerWebSocket(cfg *config.Config) {
	r := gin.Default()
	r.Use(corsMiddleware())
	r.GET("/ws/controller", ws.GinControllerWS())

	addr := fmt.Sprintf(":%d", cfg.ControllerWSPort)

	log.Printf("Controller WebSocket listening on %s", addr)
	if err := r.Run(addr); err != nil {
		log.Fatalf("Controller WebSocket server error: %v", err)
	}
}

func corsMiddleware() gin.HandlerFunc {
	return func(c *gin.Context) {
		c.Writer.Header().Set("Access-Control-Allow-Origin", "*")
		c.Writer.Header().Set("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS")
		c.Writer.Header().Set("Access-Control-Allow-Headers", "Content-Type, Authorization")

		if c.Request.Method == "OPTIONS" {
			c.AbortWithStatus(http.StatusOK)
			return
		}

		c.Next()
	}
}