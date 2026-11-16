#!/bin/sh
# unbridge.sh - undo everything DroneBridge.Run() / btnBridge ("One-Click:
# Bridge PC -> Drone") sets up ON THE AIRCARD (aircard_dump/README.md Phase
# 7-8, DroneBridge.cs, multidrone.sh). No revert script for the device side
# existed before this one - only the PC-side route fix had one
# (unfix-ac797-route.ps1). Run this ON THE AIRCARD (root telnet). Idempotent -
# safe to re-run, or run after only some of the bridge stages completed.
#
# Reverts, in order:
#   1. the slot-1 NAT/NETMAP/policy-routing added by `multidrone.sh add 1 <ssid>`
#      (inlined here, not dependent on /tmp/multidrone.sh still being present -
#      it's RAM-only in /tmp and won't survive a reboot anyway)
#   2. the wlan0 station join itself (wpa_cli disable_network all) - "del" in
#      multidrone.sh only tears down the NAT rules, NOT the station join, so
#      wlan0 stays associated to the drone's AP even after a plain "del 1"
#   3. the 3 standby idle timers that DroneBridge.Run() / OffloadStation.Connect
#      force to 0, back to their documented factory default of 300s (see
#      aircard_dump/README.md lines ~396-398). Wlan.WifiEnabled is left at 1 -
#      it's needed for the device's normal Wi-Fi function and was almost
#      certainly already 1 before the bridge ran, so there's nothing to revert.
#
# Does NOT touch: root/telnet access (AT!CUSTOM TELNETENABLE/ADBENABLE) - that
# stays enabled on purpose. This script is about NETWORK settings only, per
# the "network settings" scope this was asked for.
#
# Usage - either paste this file's body directly into an interactive root
# telnet session (telnet 192.168.1.1), or run it one-shot from the PC without
# needing to push the file first:
#   python aircard_dump/tools/sh.py \
#     "iptables -t mangle -D PREROUTING -d 10.9.1.0/24 -j MARK --set-mark 101" \
#     "iptables -t nat -D PREROUTING -d 10.9.1.0/24 -j NETMAP --to 192.168.100.0/24" \
#     "iptables -t nat -D POSTROUTING -o wlan0 -j MASQUERADE" \
#     "ip rule del fwmark 101 table 101" \
#     "ip route flush table 101" \
#     "wpa_cli -i wlan0 disable_network all" \
#     "dx -c Power.StandbyOnUsbIdleTimer 300" \
#     "dx -c Power.StandbyOnAcIdleTimer 300" \
#     "dx -c Power.StandbyOnBattIdleTimer 300"
# (each -D/del/flush is safe to run even if the rule/table was never set - it
# just no-ops with an error on stderr, which is why they're all suppressed.)

SLOT=1; IF=wlan0; TABLE=101; MARK=101; RM=10.9.1; DRONE_NET=192.168.100.0/24

echo "== [1/3] tearing down slot $SLOT NAT/routing =="
iptables -t mangle -D PREROUTING -d $RM.0/24 -j MARK --set-mark $MARK 2>/dev/null
iptables -t nat -D PREROUTING -d $RM.0/24 -j NETMAP --to $DRONE_NET 2>/dev/null
iptables -t nat -D POSTROUTING -o $IF -j MASQUERADE 2>/dev/null
ip rule del fwmark $MARK table $TABLE 2>/dev/null
ip route flush table $TABLE 2>/dev/null

echo "== [2/3] dropping the wlan0 station join to the drone =="
wpa_cli -i $IF disable_network all 2>&1

echo "== [3/3] restoring standby idle timers to factory default (300s) =="
dx -c Power.StandbyOnUsbIdleTimer  300
dx -c Power.StandbyOnAcIdleTimer   300
dx -c Power.StandbyOnBattIdleTimer 300

echo "done. AirCard network settings reverted (Wlan.WifiEnabled left at 1, unchanged)."
echo "PC side: run aircard_dump/tools/unfix-ac797-route.ps1 as admin to revert the RNDIS static IP/route too."
