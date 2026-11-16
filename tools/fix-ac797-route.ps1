# B7 fix: stop the AC797 (AirCard 797S) RNDIS adapter from contending for the
# Windows default route. It DHCPs a gateway/DNS (192.168.1.1) with no upstream
# internet, which ties with the real adapters at metric 35 and can make
# Windows send general traffic (or DNS) to the Netgear device instead.
#
# This script only touches the RNDIS adapter (matched by MAC 00:a0:c6:00:00:00,
# alias may shift between "Ethernet 2" / "Ethernet 3" / etc. across replugs).
# Real adapters (Ethernet, Wi-Fi) are never modified.
#
# Usage:  powershell -File fix-ac797-route.ps1
# Revert: powershell -File unfix-ac797-route.ps1

$ErrorActionPreference = "Stop"

$mac = "00-A0-C6-00-00-00"
$adapter = Get-NetAdapter | Where-Object { $_.MacAddress -eq $mac }

if (-not $adapter) {
    Write-Error "AC797 RNDIS adapter (MAC $mac) not found. Plug it in via USB first."
    exit 1
}

$alias = $adapter.InterfaceAlias
Write-Output "Found AC797 adapter: $alias (ifIndex $($adapter.ifIndex))"

Set-NetIPInterface -InterfaceAlias $alias -Dhcp Disabled -Confirm:$false

# Remove any leftover IPv4 addresses left behind by disabling DHCP
Get-NetIPAddress -InterfaceAlias $alias -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue

New-NetIPAddress -InterfaceAlias $alias -IPAddress 192.168.1.50 -PrefixLength 24 -Confirm:$false | Out-Null
Set-DnsClientServerAddress -InterfaceAlias $alias -ServerAddresses @() -Confirm:$false

# Persistent route so the drone hub subnet (10.9.x.x, see aircard_dump/README.md
# Phase 8) stays reachable through the AC797 even with no default gateway on it.
route -p add 10.9.0.0 mask 255.255.0.0 192.168.1.1 | Out-Null

Write-Output "Done. $alias is now static 192.168.1.50/24, no gateway, no DNS."
Write-Output "Persistent route added: 10.9.0.0/16 via 192.168.1.1"
Write-Output "Run unfix-ac797-route.ps1 to revert to DHCP."
