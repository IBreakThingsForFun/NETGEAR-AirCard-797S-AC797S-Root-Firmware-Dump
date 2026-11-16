# Reverts fix-ac797-route.ps1: puts the AC797 (AirCard 797S) RNDIS adapter
# back to plain DHCP (its factory/default behavior) and removes the
# persistent 10.9.0.0/16 route added by the fix.
#
# Usage: powershell -File unfix-ac797-route.ps1

$ErrorActionPreference = "Stop"

$mac = "00-A0-C6-00-00-00"
$adapter = Get-NetAdapter | Where-Object { $_.MacAddress -eq $mac }

if (-not $adapter) {
    Write-Error "AC797 RNDIS adapter (MAC $mac) not found. Plug it in via USB first."
    exit 1
}

$alias = $adapter.InterfaceAlias
Write-Output "Found AC797 adapter: $alias (ifIndex $($adapter.ifIndex))"

# Remove the static IP we assigned
Get-NetIPAddress -InterfaceAlias $alias -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue

Set-NetIPInterface -InterfaceAlias $alias -Dhcp Enabled -Confirm:$false

# DNS back to "obtain automatically" (DHCP-provided)
Set-DnsClientServerAddress -InterfaceAlias $alias -ResetServerAddresses

# Remove the persistent route added by the fix script
route delete 10.9.0.0 | Out-Null

Write-Output "Done. $alias is back on DHCP; persistent 10.9.0.0/16 route removed."
Write-Output "Unplug/replug the AC797 (or run: ipconfig /renew) if it doesn't get an address immediately."
