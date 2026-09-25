//go:build windows

package main

import (
	"os/exec"
)

func init() {
	go func() {
		_ = exec.Command("netsh", "advfirewall", "firewall", "delete", "rule", "name=ConfGTS Discovery").Run()
		_ = exec.Command("netsh", "advfirewall", "firewall", "add", "rule",
			"name=ConfGTS Discovery", "dir=in", "action=allow", "protocol=UDP", "localport=8091").Run()
	}()
}
