package main

import (
	"crypto/rand"
	"crypto/rsa"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/pem"
	"fmt"
	"math/big"
	"net"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// ensureHTTPSCertificate makes HTTPS usable even when the administrator
// only turns it on in ConfGTS Server Settings / web admin and does not
// provide an external certificate. Existing valid custom PEM files win.
func ensureHTTPSCertificate(cfg Config) (Config, error) {
	if !cfg.HTTPS.Enabled {
		return cfg, nil
	}

	if strings.TrimSpace(cfg.HTTPS.CertFile) != "" &&
		strings.TrimSpace(cfg.HTTPS.KeyFile) != "" &&
		fileExists(cfg.HTTPS.CertFile) &&
		fileExists(cfg.HTTPS.KeyFile) {
		return cfg, nil
	}

	certDir := filepath.Join(dataDir(), "certs")
	if err := os.MkdirAll(certDir, 0755); err != nil {
		return cfg, fmt.Errorf("create HTTPS certificate directory: %w", err)
	}

	certPath := filepath.Join(certDir, "confgts-server.crt")
	keyPath := filepath.Join(certDir, "confgts-server.key")

	key, err := rsa.GenerateKey(rand.Reader, 3072)
	if err != nil {
		return cfg, fmt.Errorf("generate HTTPS private key: %w", err)
	}

	serialLimit := new(big.Int).Lsh(big.NewInt(1), 128)
	serial, err := rand.Int(rand.Reader, serialLimit)
	if err != nil {
		return cfg, fmt.Errorf("generate HTTPS serial: %w", err)
	}

	hostNames := map[string]struct{}{}
	ipNames := map[string]net.IP{}

	addHost := func(value string) {
		value = strings.TrimSpace(strings.TrimSuffix(value, "."))
		if value == "" {
			return
		}
		if ip := net.ParseIP(value); ip != nil {
			ipNames[ip.String()] = ip
			return
		}
		hostNames[strings.ToLower(value)] = struct{}{}
	}

	addHost("localhost")
	if host, err := os.Hostname(); err == nil {
		addHost(host)
		if names, err := net.LookupHost(host); err == nil {
			for _, value := range names {
				addHost(value)
			}
		}
		if cname, err := net.LookupCNAME(host); err == nil {
			addHost(cname)
		}
	}

	if u, err := url.Parse(strings.TrimSpace(cfg.PublicURL)); err == nil {
		addHost(u.Hostname())
	}

	if nameBytes, err := os.ReadFile(filepath.Join(dataDir(), "server-name.txt")); err == nil {
		addHost(string(nameBytes))
	}

	if ifaces, err := net.Interfaces(); err == nil {
		for _, iface := range ifaces {
			addrs, err := iface.Addrs()
			if err != nil {
				continue
			}
			for _, addr := range addrs {
				ipText := addr.String()
				if slash := strings.IndexByte(ipText, '/'); slash >= 0 {
					ipText = ipText[:slash]
				}
				if ip := net.ParseIP(ipText); ip != nil && !ip.IsUnspecified() {
					ipNames[ip.String()] = ip
				}
			}
		}
	}

	ipNames["127.0.0.1"] = net.ParseIP("127.0.0.1")

	dnsNames := make([]string, 0, len(hostNames))
	for name := range hostNames {
		dnsNames = append(dnsNames, name)
	}
	ips := make([]net.IP, 0, len(ipNames))
	for _, ip := range ipNames {
		ips = append(ips, ip)
	}

	commonName := "ConfGTS Server"
	if len(dnsNames) > 0 {
		commonName = dnsNames[0]
	}

	template := x509.Certificate{
		SerialNumber: serial,
		Subject: pkix.Name{
			CommonName:   commonName,
			Organization: []string{"Городские тепловые сети"},
		},
		NotBefore:             time.Now().Add(-24 * time.Hour),
		NotAfter:              time.Now().AddDate(3, 0, 0),
		KeyUsage:              x509.KeyUsageDigitalSignature | x509.KeyUsageKeyEncipherment,
		ExtKeyUsage:           []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth},
		BasicConstraintsValid: true,
		DNSNames:              dnsNames,
		IPAddresses:           ips,
	}

	der, err := x509.CreateCertificate(rand.Reader, &template, &template, &key.PublicKey, key)
	if err != nil {
		return cfg, fmt.Errorf("create HTTPS certificate: %w", err)
	}

	certPEM := pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der})
	keyBytes, err := x509.MarshalPKCS8PrivateKey(key)
	if err != nil {
		return cfg, fmt.Errorf("marshal HTTPS private key: %w", err)
	}
	keyPEM := pem.EncodeToMemory(&pem.Block{Type: "PRIVATE KEY", Bytes: keyBytes})

	if err := writePrivateFile(keyPath, keyPEM); err != nil {
		return cfg, fmt.Errorf("write HTTPS private key: %w", err)
	}
	if err := os.WriteFile(certPath, certPEM, 0644); err != nil {
		return cfg, fmt.Errorf("write HTTPS certificate: %w", err)
	}

	cfg.HTTPS.CertFile = certPath
	cfg.HTTPS.KeyFile = keyPath
	logEventSafe("system", "https", "Generated automatic ConfGTS HTTPS certificate")
	return cfg, nil
}

func fileExists(path string) bool {
	info, err := os.Stat(strings.TrimSpace(path))
	return err == nil && !info.IsDir()
}

func writePrivateFile(path string, data []byte) error {
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, data, 0600); err != nil {
		return err
	}
	return os.Rename(tmp, path)
}

// Keep certificate generation independent from Store internals.
func logEventSafe(username, category, message string) {
	_ = username
	_ = category
	_ = message
}
