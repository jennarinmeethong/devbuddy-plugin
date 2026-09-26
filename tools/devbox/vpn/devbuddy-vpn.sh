#!/bin/sh
# WireGuard for reaching DevBuddy from outside the LAN (info.md, 2026-09-27).
#
# Runs as root inside its own unprivileged Debian 13 LXC, not in the DevBuddy LXC. The router
# forwards UDP 8840 (jennarin.thddns.net:8840) to this LXC and nothing else. A peer can reach one
# thing: https://192.168.1.160:5010, the DevBuddy gateway. Not the rest of the LAN, not this LXC,
# not the internet. Each device has its own key and can be removed on its own.
#
#   devbuddy-vpn.sh install          once: packages, server key, firewall, start
#   devbuddy-vpn.sh add <device>     a peer; prints its config and a QR code for a phone
#   devbuddy-vpn.sh remove <device>  revokes that peer at once
#   devbuddy-vpn.sh list             peers and their last handshake
set -eu

ENDPOINT="jennarin.thddns.net:8840"
PORT=8840
SUBNET="10.88.40"
TARGET_IP="192.168.1.160"
TARGET_PORT=5010
WG=/etc/wireguard
PEERS=/root/devbuddy-vpn-peers

die() { echo "error: $*" >&2; exit 1; }
[ "$(id -u)" = 0 ] || die "run as root (pct enter <id> on the Proxmox host)"
uplink() { ip -4 route show default | awk '{print $5; exit}'; }

install_vpn() {
  apt-get update -qq
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq wireguard-tools nftables qrencode procps iproute2 >/dev/null

  # The kernel module is the host's. In an unprivileged LXC this fails until the host loads it.
  # WG_QUICK_USERSPACE_IMPLEMENTATION=wireguard-go (wg-quick's own switch) skips the check.
  if [ -n "${WG_QUICK_USERSPACE_IMPLEMENTATION:-}" ]; then
    :
  elif ! ip link add devbuddy-probe type wireguard 2>/dev/null; then
    die "cannot create a WireGuard interface. On the Proxmox host run: modprobe wireguard && echo wireguard >> /etc/modules-load.d/wireguard.conf"
  else
    ip link del devbuddy-probe
  fi

  umask 077
  mkdir -p "$WG" "$PEERS"
  [ -f "$WG/server.key" ] || wg genkey > "$WG/server.key"
  wg pubkey < "$WG/server.key" > "$WG/server.pub"
  if [ ! -f "$WG/wg0.conf" ]; then
    cat > "$WG/wg0.conf" <<EOF
[Interface]
Address = $SUBNET.1/24
ListenPort = $PORT
PrivateKey = $(cat "$WG/server.key")
EOF
  fi

  mkdir -p /etc/sysctl.d
  echo "net.ipv4.ip_forward = 1" > /etc/sysctl.d/90-devbuddy-vpn.conf
  sysctl -q -p /etc/sysctl.d/90-devbuddy-vpn.conf

  up=$(uplink)
  [ -n "$up" ] || die "no default route"
  cat > /etc/nftables.conf <<EOF
#!/usr/sbin/nft -f
# devbuddy-vpn.sh. Default deny; a peer reaches $TARGET_IP:$TARGET_PORT/tcp and nothing else.
flush ruleset
table inet devbuddy_vpn {
  chain input {
    type filter hook input priority 0; policy drop;
    iif "lo" accept
    ct state established,related accept
    ct state invalid drop
    iifname "$up" udp dport $PORT accept
    iifname "$up" ip saddr 192.168.1.0/24 icmp type echo-request accept
  }
  chain forward {
    type filter hook forward priority 0; policy drop;
    ct state established,related accept
    iifname "wg0" oifname "$up" ip daddr $TARGET_IP tcp dport $TARGET_PORT accept
  }
  chain output {
    type filter hook output priority 0; policy accept;
  }
}
table ip devbuddy_vpn_nat {
  chain postrouting {
    type nat hook postrouting priority 100; policy accept;
    oifname "$up" ip saddr $SUBNET.0/24 masquerade
  }
}
EOF
  nft -c -f /etc/nftables.conf
  systemctl enable -q --now nftables
  systemctl restart nftables
  systemctl enable -q --now wg-quick@wg0
  echo "installed. Server public key: $(cat "$WG/server.pub")"
  echo "next: devbuddy-vpn.sh add <device>"
}

add_peer() {
  name=$1
  [ -f "$WG/wg0.conf" ] || die "run: $0 install"
  echo "$name" | grep -Eq '^[a-z0-9-]{1,32}$' || die "device name: lowercase letters, digits and -"
  [ ! -f "$PEERS/$name.conf" ] || die "$name exists; remove it first"
  umask 077
  last=$(grep -ho "AllowedIPs = $SUBNET\.[0-9]*" "$WG/wg0.conf" | awk -F. '{print $4}' | sort -n | tail -1)
  n=$(( ${last:-1} + 1 ))
  [ "$n" -le 254 ] || die "no addresses left"
  key=$(wg genkey); pub=$(echo "$key" | wg pubkey); psk=$(wg genpsk)
  cat >> "$WG/wg0.conf" <<EOF

[Peer]
# $name
PublicKey = $pub
PresharedKey = $psk
AllowedIPs = $SUBNET.$n/32
EOF
  cat > "$PEERS/$name.conf" <<EOF
[Interface]
PrivateKey = $key
Address = $SUBNET.$n/32

[Peer]
PublicKey = $(cat "$WG/server.pub")
PresharedKey = $psk
Endpoint = $ENDPOINT
AllowedIPs = $TARGET_IP/32
PersistentKeepalive = 25
EOF
  wg-quick strip wg0 > /tmp/wg0.strip; wg syncconf wg0 /tmp/wg0.strip; rm -f /tmp/wg0.strip
  echo "== $name: $SUBNET.$n. Config file: $PEERS/$name.conf"
  qrencode -t ansiutf8 < "$PEERS/$name.conf"
  echo "Import it on the device, then delete $PEERS/$name.conf here: its private key belongs on that device only."
}

remove_peer() {
  name=$1
  [ -f "$WG/wg0.conf" ] || die "run: $0 install"
  grep -q "^# $name\$" "$WG/wg0.conf" || die "no peer named $name"
  # A peer block is the "[Peer]" line before "# name" and the three lines after it.
  awk -v n="# $name" '
    { lines[NR]=$0 }
    END {
      for (i=1;i<=NR;i++) if (lines[i]==n) { skip[i-1]=1; for (j=i;j<=i+3;j++) skip[j]=1; if (lines[i-2]=="") skip[i-2]=1 }
      for (i=1;i<=NR;i++) if (!skip[i]) print lines[i]
    }' "$WG/wg0.conf" > "$WG/wg0.conf.new"
  mv "$WG/wg0.conf.new" "$WG/wg0.conf"; chmod 600 "$WG/wg0.conf"
  wg-quick strip wg0 > /tmp/wg0.strip; wg syncconf wg0 /tmp/wg0.strip; rm -f /tmp/wg0.strip
  rm -f "$PEERS/$name.conf"
  echo "removed $name; its key no longer works"
}

list_peers() {
  awk '/^# /{name=substr($0,3)} /^PublicKey/{print $3, name}' "$WG/wg0.conf" | while read -r pub name; do
    hs=$(wg show wg0 latest-handshakes | awk -v p="$pub" '$1==p{print $2}')
    if [ "${hs:-0}" -gt 0 ]; then when=$(date -d "@$hs" '+%F %T'); else when=never; fi
    echo "$name  last handshake: $when"
  done
}

case "${1:-}" in
  install) install_vpn ;;
  add) [ $# -eq 2 ] || die "usage: $0 add <device>"; add_peer "$2" ;;
  remove) [ $# -eq 2 ] || die "usage: $0 remove <device>"; remove_peer "$2" ;;
  list) list_peers ;;
  *) die "usage: $0 install | add <device> | remove <device> | list" ;;
esac
