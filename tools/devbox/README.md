# The devbox's own files

What runs on the owner's DevBuddy installation outside the product's stack, kept here so a later
owner can see it and rebuild it. Nothing here is built or shipped by CI.

- `gateway/`: the HTTPS gateway in LXC 100, at `/data/devbuddy-tools/gateway` there (Phase 14 B1,
  `info.md` 2026-09-26 and 2026-09-27). Caddy with its own internal CA on `192.168.1.160:5010`,
  proxying to the stack's `api`. It serves LAN and VPN addresses only and drops a public one.
- `vpn/devbuddy-vpn.sh`: WireGuard in LXC 102, `192.168.1.162`, on UDP 8840
  (`jennarin.thddns.net:8840`). A peer reaches `192.168.1.160:5010/tcp` and nothing else. Tested
  on 2026-09-27 on devrelease with `wireguard-go`, a server and a client container: the handshake,
  DevBuddy answering 200 through the tunnel, SSH on both LXCs, HomeHub's 5030 and the internet
  timing out, and a removed peer cut off at once. It has not yet run in LXC 102 itself.

The CA's keys, the server's WireGuard key and the peers' configurations are secrets and are not
here. They live in LXC 100's `gateway/data` and LXC 102's `/etc/wireguard`.
