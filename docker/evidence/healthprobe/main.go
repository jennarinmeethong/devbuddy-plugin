// The evidence store's health probe (ADR-0014, point 4). A scratch image has no shell and no client,
// so this is the one thing Compose's health check can run.
//
// Healthy means both: the S3 gateway answers /healthz with 200, and an unsigned request is refused.
// SeaweedFS allows all access when no credentials are configured, so a store that answers the
// unsigned request is reported unhealthy, and nothing that waits on its health starts.
package main

import (
	"fmt"
	"net/http"
	"os"
	"time"
)

func main() {
	base := "http://127.0.0.1:8333"
	if len(os.Args) > 1 {
		base = os.Args[1]
	}
	client := &http.Client{Timeout: 3 * time.Second}

	if code, err := status(client, base+"/healthz"); err != nil || code != http.StatusOK {
		fail("gateway not ready: %d %v", code, err)
	}
	// An unsigned ListBuckets. Anything but 403 means the store would serve evidence to anyone.
	if code, err := status(client, base+"/"); err != nil || code != http.StatusForbidden {
		fail("unsigned request not refused: %d %v", code, err)
	}
	fmt.Println("ok")
}

func status(client *http.Client, url string) (int, error) {
	resp, err := client.Get(url)
	if err != nil {
		return 0, err
	}
	resp.Body.Close()
	return resp.StatusCode, nil
}

func fail(format string, args ...any) {
	fmt.Fprintf(os.Stderr, format+"\n", args...)
	os.Exit(1)
}
