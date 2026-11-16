#!/bin/sh
# multidrone.sh - connect the AirCard to multiple Holy Stone drones at once,
# each remapped to a private, non-colliding subnet. DRONES ARE NEVER MODIFIED.
#
#   add <slot> <ssid> [iface]   provision a drone in slot N (1..3) -> 10.9.N.1
#   del <slot>                  tear down slot N
#   status                      show slots / links / rules
#
# slot 1 defaults to wlan0 (QCMAP's existing STA); slots 2..3 make new VAPs.
# On the PC add one route per slot:  route add 10.9.N.0 mask 255.255.255.0 192.168.1.1
#
# Design: PC hits 10.9.N.1 -> mangle marks it N -> NETMAP rewrites dst to the
# drone's real 192.168.100.x -> fwmark policy-routes it out that drone's radio ->
# MASQUERADE so the drone replies to us. conntrack reverses the whole path.
# Safety: touches ONLY the AirCard. Recover over USB. Nothing persists a reboot.

DRONE_NET=192.168.100.0/24
DRONE_GW=192.168.100.1

iface_for() { case "$1" in 1) echo wlan0;; 2) echo wlan2;; 3) echo wlan3;; *) echo "";; esac; }

add() {
  SLOT=$1; SSID=$2; IF=${3:-$(iface_for $SLOT)}
  [ -z "$SLOT" ] || [ -z "$SSID" ] || [ -z "$IF" ] && { echo "usage: add <slot 1-3> <ssid> [iface]"; return 1; }
  TABLE=$((100+SLOT)); MARK=$TABLE; RM=10.9.$SLOT

  # 1. bring up the radio + associate (open network)
  if [ "$IF" = wlan0 ]; then
    # reuse QCMAP's supplicant on wlan0
    ID=$(wpa_cli -i $IF list_networks 2>/dev/null | awk -v s="$SSID" '$0 ~ s {print $1; exit}')
    if [ -z "$ID" ]; then
      ID=$(wpa_cli -i $IF add_network | tail -1)
      wpa_cli -i $IF set_network $ID ssid "\"$SSID\""
      wpa_cli -i $IF set_network $ID key_mgmt NONE
    fi
    wpa_cli -i $IF enable_network $ID; wpa_cli -i $IF select_network $ID
    sleep 6
    udhcpc -i $IF -n -q -t 6 >/dev/null 2>&1
    LOCALIP=$(ifconfig $IF | sed -n 's/.*inet addr:\([0-9.]*\).*/\1/p')
  else
    # dedicated VAP + our own supplicant
    iw dev $IF del 2>/dev/null
    iw dev wlan0 interface add $IF type managed 2>/dev/null
    CFG=/tmp/wpa_$IF.conf
    printf 'ctrl_interface=/var/run/wpa_%s\nnetwork={\n ssid="%s"\n key_mgmt=NONE\n}\n' "$IF" "$SSID" > $CFG
    killall -q wpa_supplicant_$IF 2>/dev/null
    wpa_supplicant -i $IF -Dnl80211 -c $CFG -B >/dev/null 2>&1
    sleep 6
    LOCALIP=192.168.100.$((100+SLOT))
    ifconfig $IF $LOCALIP netmask 255.255.255.0 up
    ip route del $DRONE_NET dev $IF 2>/dev/null   # keep this subnet OUT of the main table
  fi
  [ -z "$LOCALIP" ] && { echo "slot $SLOT: $IF got no IP - is the drone on?"; return 1; }

  # 2. per-slot routing table -> out this drone's radio
  ip route flush table $TABLE 2>/dev/null
  ip route add $DRONE_NET dev $IF src $LOCALIP table $TABLE
  ip route add default via $DRONE_GW dev $IF table $TABLE
  ip rule del fwmark $MARK table $TABLE 2>/dev/null
  ip rule add fwmark $MARK table $TABLE

  # 3. remap RM.0/24 <-> drone 192.168.100.0/24, mark, and SNAT out the radio
  iptables -t mangle -C PREROUTING -d $RM.0/24 -j MARK --set-mark $MARK 2>/dev/null || \
    iptables -t mangle -A PREROUTING -d $RM.0/24 -j MARK --set-mark $MARK
  iptables -t nat -C PREROUTING -d $RM.0/24 -j NETMAP --to $DRONE_NET 2>/dev/null || \
    iptables -t nat -A PREROUTING -d $RM.0/24 -j NETMAP --to $DRONE_NET
  iptables -t nat -C POSTROUTING -o $IF -j MASQUERADE 2>/dev/null || \
    iptables -t nat -A POSTROUTING -o $IF -j MASQUERADE

  echo "slot $SLOT ready: $SSID on $IF ($LOCALIP) -> PC reaches it at $RM.1"
}

del() {
  SLOT=$1; IF=$(iface_for $SLOT); TABLE=$((100+SLOT)); MARK=$TABLE; RM=10.9.$SLOT
  iptables -t mangle -D PREROUTING -d $RM.0/24 -j MARK --set-mark $MARK 2>/dev/null
  iptables -t nat -D PREROUTING -d $RM.0/24 -j NETMAP --to $DRONE_NET 2>/dev/null
  iptables -t nat -D POSTROUTING -o $IF -j MASQUERADE 2>/dev/null
  ip rule del fwmark $MARK table $TABLE 2>/dev/null
  ip route flush table $TABLE 2>/dev/null
  [ "$IF" != wlan0 ] && { killall -q wpa_supplicant 2>/dev/null; iw dev $IF del 2>/dev/null; }
  echo "slot $SLOT torn down"
}

status() {
  echo "== station links =="; iw dev 2>/dev/null | grep -E 'Interface|type|ssid'
  echo "== remap rules =="; iptables -t nat -L PREROUTING -n 2>/dev/null | grep -E 'NETMAP|10.9'
  echo "== policy rules =="; ip rule list 2>/dev/null | grep fwmark
  echo "== connected drones =="
  for s in 1 2 3; do IF=$(iface_for $s); S=$(wpa_cli -i $IF status 2>/dev/null | sed -n 's/^ssid=//p'); [ -n "$S" ] && echo "  slot $s ($IF): $S  -> 10.9.$s.1"; done
}

CMD=$1; shift 2>/dev/null
case "$CMD" in
  add) add "$@";;
  del) del "$@";;
  status|"") status;;
  *) echo "usage: multidrone.sh add|del|status";;
esac
