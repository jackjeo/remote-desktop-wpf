package config

import (
	"os"
	"strconv"
)

type Config struct {
	HTTPPort         int
	AgentWSPort     int
	ControllerWSPort int
	JWTSecret        string
}

func Load() *Config {
	return &Config{
		HTTPPort:         getEnvInt("RELAY_PORT", 8080),
		AgentWSPort:      getEnvInt("AGENT_WS_PORT", 8081),
		ControllerWSPort: getEnvInt("CONTROLLER_WS_PORT", 8082),
		JWTSecret:        getEnv("JWT_SECRET", "default-secret-key"),
	}
}

func getEnv(key, defaultValue string) string {
	if value := os.Getenv(key); value != "" {
		return value
	}
	return defaultValue
}

func getEnvInt(key string, defaultValue int) int {
	if value := os.Getenv(key); value != "" {
		if intValue, err := strconv.Atoi(value); err == nil {
			return intValue
		}
	}
	return defaultValue
}