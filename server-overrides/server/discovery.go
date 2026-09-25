package main

import (
	"encoding/json"
	"fmt"
	"log"
	"net"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

const discoveryPort = 8091
const discoveryRequest = "CONFGTS_DISCOVER_V1"

type discoveryConfig struct {
	ListenAddr string `json:"listen_addr"`
	PublicURL  string `json:"public_url"`
	ServerName string `json:"server_name"`
}

func startDiscoveryResponder() {
	addr := &net.UDPAddr{IP: net.IPv4zero, Port: discoveryPort}
	conn, err := net.ListenUDP("udp4", addr)
	if err != nil {
		log.Printf("ConfGTS discovery disabled: %v", err)
		return
	}
	defer conn.Close()
	_ = conn.SetReadBuffer(64 * 1024)
	buf := make([]byte, 2048)
	for {
		_ = conn.SetReadDeadline(time.Now().Add(30 * time.Second))
		n, remote, err := conn.ReadFromUDP(buf)
		if err != nil {
			if ne, ok := err.(net.Error); ok && ne.Timeout() {
				continue
			}
			log.Printf("ConfGTS discovery read: %v", err)
			continue
		}
		req := strings.TrimSpace(string(buf[:n]))
		if !strings.HasPrefix(req, discoveryRequest) {
			continue
		}
		requested := ""
		if p := strings.SplitN(req, "|", 2); len(p) == 2 {
			requested = strings.TrimSpace(p[1])
		}

		cfg := loadDiscoveryConfig()
		names := discoveryNames(cfg)
		if requested != "" && requested != "*" && !matchesDiscoveryName(requested, names) {
			continue
		}

		port := serverPort(cfg.ListenAddr)
		scheme := "http"
		if strings.HasPrefix(strings.ToLower(cfg.PublicURL), "https://") {
			scheme = "https"
		}
		payload, _ := json.Marshal(map[string]any{
			"protocol": "CONFGTS_V1",
			"name":     firstNonEmpty(cfg.ServerName, names...),
			"port":     port,
			"scheme":   scheme,
			"version":  Version,
		})
		_, _ = conn.WriteToUDP(payload, remote)
	}
}

func loadDiscoveryConfig() discoveryConfig {
	cfg := discoveryConfig{ListenAddr: "0.0.0.0:8090"}
	path := filepath.Join(dataDir(), "config.json")
	if b, err := os.ReadFile(path); err == nil {
		_ = json.Unmarshal(b, &cfg)
	}
	if b, err := os.ReadFile(filepath.Join(dataDir(), "server-name.txt")); err == nil {
		if name := strings.TrimSpace(string(b)); name != "" {
			cfg.ServerName = name
		}
	}
	return cfg
}

func discoveryNames(cfg discoveryConfig) []string {
	var names []string
	add := func(v string) {
		v = strings.TrimSpace(strings.ToLower(v))
		if v == "" {
			return
		}
		for _, x := range names {
			if x == v {
				return
			}
		}
		names = append(names, v)
	}
	add(cfg.ServerName)
	if h, err := os.Hostname(); err == nil {
		add(h)
		if entry, err := net.LookupCNAME(h); err == nil {
			add(strings.TrimSuffix(entry, "."))
		}
	}
	if u, err := neturlHost(cfg.PublicURL); err == nil {
		add(u)
	}
	return names
}

func matchesDiscoveryName(requested string, names []string) bool {
	r := strings.TrimSuffix(strings.ToLower(strings.TrimSpace(requested)), ".")
	if r == "confgts" || r == "confgts-server" {
		return true
	}
	for _, n := range names {
		n = strings.TrimSuffix(strings.ToLower(strings.TrimSpace(n)), ".")
		if r == n {
			return true
		}
		if strings.Contains(r, ".") && strings.SplitN(r, ".", 2)[0] == strings.SplitN(n, ".", 2)[0] {
			return true
		}
		if !strings.Contains(r, ".") && strings.SplitN(n, ".", 2)[0] == r {
			return true
		}
	}
	return false
}

func serverPort(listen string) int {
	listen = strings.TrimSpace(listen)
	if strings.HasPrefix(listen, ":") {
		if p, err := strconv.Atoi(strings.TrimPrefix(listen, ":")); err == nil {
			return p
		}
	}
	if _, p, err := net.SplitHostPort(listen); err == nil {
		if n, err := strconv.Atoi(p); err == nil {
			return n
		}
	}
	return 8090
}

func neturlHost(raw string) (string, error) {
	raw = strings.TrimSpace(raw)
	if raw == "" {
		return "", fmt.Errorf("empty")
	}
	if !strings.Contains(raw, "://") {
		raw = "http://" + raw
	}
	u, err := neturlParse(raw)
	if err != nil {
		return "", err
	}
	return u, nil
}

func neturlParse(raw string) (string, error) {
	i := strings.Index(raw, "://")
	if i >= 0 {
		raw = raw[i+3:]
	}
	raw = strings.SplitN(raw, "/", 2)[0]
	host, _, err := net.SplitHostPort(raw)
	if err == nil {
		return strings.Trim(host, "[]"), nil
	}
	if strings.Contains(raw, ":") {
		return strings.SplitN(raw, ":", 2)[0], nil
	}
	if raw == "" {
		return "", fmt.Errorf("empty")
	}
	return raw, nil
}

func firstNonEmpty(primary string, values ...string) string {
	if s := strings.TrimSpace(primary); s != "" {
		return s
	}
	for _, v := range values {
		if s := strings.TrimSpace(v); s != "" {
			return s
		}
	}
	return "ConfGTS"
}

func init() {
	go startDiscoveryResponder()
}
