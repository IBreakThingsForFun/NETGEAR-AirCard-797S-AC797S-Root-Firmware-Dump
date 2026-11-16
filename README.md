<img width="2048" height="768" alt="I Break Things for Fun" src="https://github.com/user-attachments/assets/615b22a5-63fb-4888-992c-e75281209725" />


# NETGEAR AirCard 797S (AC797S) — Root & Firmware Dump

**A complete, replicable walkthrough of gaining root on a NETGEAR AirCard 797S
mobile hotspot and dumping its firmware — done 2026‑07‑10.**

This is a standalone lab notebook. Everything below can be reproduced from
scratch on any AC797S with only a USB cable and the device's admin password.

<img width="800" height="800" alt="image" src="https://github.com/user-attachments/assets/68817ed3-ef38-4b5f-b8e5-215ef8dfc386" />


---
## Web Admin

### Before Root 🥱💤
<img width="807" height="628" alt="old_admin" src="https://github.com/user-attachments/assets/20991511-8db0-4fca-b4b3-4b4bcaab0584" />


### After Root + Reverse Proxy 😎🎉
<img width="807" height="589" alt="new_admin" src="https://github.com/user-attachments/assets/0180a5c3-3e38-49f8-a7fc-b5832386933e" />


### Add new on device pages and/or customize existing ones.
<img width="565" height="425" alt="hello" src="https://github.com/user-attachments/assets/435ccd39-0cbc-402c-ad3b-d7fa7de2cfac" />


---

## In a nutshell 🥜

| | |
|---|---|
| **Device** | NETGEAR AirCard 797S, HW 1.0, FW `NTG9X40C_30.00.08.04` (2019‑11‑21) |
| **SoC** | Qualcomm **MDM9640** (MDM9x40 family), ARMv7, Linux 3.10.49 |
| **Attack chain** | USB‑RNDIS → web admin API → AT‑command port (TCP 5510) → crack Sierra OEM lock → enable telnet → **unauthenticated root shell** |
| **Result** | Full root, complete browsable filesystem + 11 raw partition images |
| **Reversible?** | Yes — set `TELNETENABLE`/`ADBENABLE` back to 0 and reboot |

Nothing in this process **writes** to the device except two engineering flags
(telnet + adb enable). Reads cannot corrupt flash. The device rebooted twice
during raw dumping (see §6) but recovered cleanly every time.

---

## 1. The device

NETGEAR's AirCard 797S is a Qualcomm‑based LTE mobile hotspot. Internally it is
a small ARM Linux computer (the "application processor") sharing silicon with the
cellular baseband. It is a close sibling of the **NETGEAR MR1100 ("Nighthawk
M1")** and **AC815s** — they share the MDM9x40 platform, so MR1100 rooting notes
apply almost verbatim.

```
Manufacturer : Netgear, Incorporated
Model        : AirCard 797S
Revision     : NTG9X40C_30.00.08.04 r6048  2019/11/21
Hardware     : Qualcomm Technologies, Inc MDM9640
Kernel       : Linux 3.10.49-svn5662 armv7l (BusyBox v1.20.2)
RAM          : ~150 MB
```

---

## 2. Prerequisites

- The hotspot connected to the PC by **USB**. It enumerates as a *Remote NDIS*
  network adapter; the PC gets `192.168.1.4`, the device is `192.168.1.1`.
- The device **admin password** (printed value for this unit: `7bLAKi2P`).
- Python 3 on the PC. No special drivers — everything is done over TCP/IP.
- (Everything below uses the helper scripts in [`tools/`](tools/).)

---

## 3. Process — step by step

### Phase 1 — Web admin & the hidden JSON API
The admin UI lives at `http://192.168.1.1`. It is session‑cookie based
(`sessionId`) with a CSRF `secToken`. Logging in and reading full device state:

```bash
# log in (grab the secToken from the home page first), then read everything:
curl -s -c cj.txt -b cj.txt http://192.168.1.1/ -o home.html          # get secToken
TOKEN=$(grep -oE '"secToken":\s*"[^"]+"' home.html | grep -oE '[A-Za-z0-9]{20,}')
curl -s -b cj.txt -c cj.txt http://192.168.1.1/Forms/config \
     --data-urlencode "session.password=7bLAKi2P" \
     --data-urlencode "session.lang=en" \
     --data-urlencode "token=$TOKEN"
curl -s -b cj.txt http://192.168.1.1/model.json      # <-- entire device state tree
```

Control endpoints (all `POST`): `/Forms/config · profile · pinChange · portCfg ·
portFilter · wifiACL · accesscontrol · SchedulerList`.

### Phase 2 — The AT‑command port (TCP 5510)
A full TCP port scan of `192.168.1.1` shows **53, 80, and 5510**. Port 5510 is a
raw **AT‑command modem interface** (it is literally `netcat` piping to the AT
channel, started by `/etc/init.d/atotcp.sh` → `runatotcp.sh`):

```bash
printf 'ATI\r\n' | nc 192.168.1.1 5510        # returns modem identity
```

Many read‑only `AT!` commands work with no auth (`ATI`, `AT!GSTATUS?`,
`AT!CUSTOM?`, `AT!BAND?`). The powerful ones (`AT!USBCOMP`, `AT!CUSTOM=`, band
writes) are gated behind Sierra's OEM engineering lock. This port is firewalled
off WiFi clients (`iptables … dpt:5510 … --physdev-in wlan+ … REJECT`) — reachable
only via USB/local interfaces.

### Phase 3 — Cracking the Sierra OEM lock  ⭐
The gate is a challenge/response:

```
AT!OPENLOCK?  →  <16 hex-char challenge>          (changes every query)
AT!OPENLOCK="<computed response>"  →  OK          (unlocks engineering cmds)
```

The stock password (`A710`) does **not** work — NETGEAR changed it. Instead the
response is computed with the reverse‑engineered **Sierra MDM9x40 keygen**
(algorithm from `bkerler/edl`, `sierrakeygen.py`). We reimplemented it cleanly in
[`tools/openlock.py`](tools/openlock.py) and validated it against a known‑answer
test vector before touching the device:

```
Device generation : MDM9x40
Key (keytable[11]) : 8A 56 03 F0 BB 9C 13 D2 4E B2 45 AD C4 0A E7 52
Init vector        : [7, 3, 0, 1, 5]
Per-byte op        : SierraAlgo(challenge[i], 4,2,1,0,3, 2,0,0)
Self-test          : BE96CBBEE0829BCA  ->  1033773720F6EE66   ✓
```

Run it live (queries the challenge and answers it in one TCP session):

```bash
python tools/unlock.py         # -> "AT!OPENLOCK=... : OK", then AT!CUSTOM=? works
```

### Phase 4 — Enable telnet (+ adb) and reboot
Once unlocked, `AT!CUSTOM=?` lists every engineering flag, including
`TELNETENABLE`, `ADBENABLE`, `NMEAENABLE`, `USBDMDISABLE (DIAG)`, `LOGENABLE`.
Turning on telnet gives a root shell on the LAN with no USB reconfiguration:

```bash
python tools/setflags.py       # re-unlock, set TELNETENABLE=1 + ADBENABLE=1, verify
# then reboot to apply (re-unlock is required after the socket resets):
#   AT!OPENLOCK? / AT!OPENLOCK="..." / AT!RESET
```

The `TELNETENABLE` flag is stored in NV, so **telnet survives reboots** (including
the accidental ones in §6).

### Phase 5 — Root shell
After ~60 s the device returns and telnet is open on port 23, dropping **straight
to a root prompt with no password**:

```
$ telnet 192.168.1.1
/ # id
uid=0(root) gid=0(root)
```

(The scripts talk raw sockets and handle telnet IAC negotiation — see
[`tools/sh.py`](tools/sh.py), a reusable "run commands as root" helper.)

### Phase 6 — Firmware dump
No SD card, but the device has `nc`, `dd`, `tar`. To avoid Windows inbound‑firewall
issues, the **PC connects *to* the device**: the device runs a one‑shot listener
per file, the PC drains it to disk.

```
device:  <producer> | nc -l -p 9000        # e.g. cat /dev/mtdblock6, or tar czf - ...
PC:      connect 192.168.1.1:9000 → write file
```

Two dump types (both done by [`tools/pull.py`](tools/pull.py)):

1. **Browsable filesystem** — `tar czf -` of the live rootfs → `rootfs_full.tar.gz`.
2. **Raw partition images** — `cat /dev/mtdblockN` per partition → `raw/mtdN_name.img`
   (uses the ECC‑corrected `mtdblock` nodes, not the raw `mtd` char devices).

```bash
python tools/pull.py tar        # the browsable filesystem archive
python tools/pull.py safe       # data/Linux partitions, health-checked, one at a time
```

> ### ⚠️ Watchdog gotcha (important!)
> **Raw‑reading partitions that the live modem/boot subsystem owns crashes the
> device** (a hardware watchdog reboots it). Confirmed offenders: **`mibib`,
> `efs2`** (and by extension `tz`, `rpm`, `modem`). Ordinary Linux/data
> partitions read fine. This is *not* damage — reads never corrupt flash, and the
> box recovers with root intact — but do **not** try to dump the modem/boot
> partitions on a running system. Get them offline via Qualcomm EDL mode if you
> ever truly need them (and never write them back).
>
> `pull.py safe` skips the dangerous partitions and runs a uptime/temp health
> check between each read so it aborts the moment anything wobbles.

### Phase 7 — WiFi station mode (AP+STA) and keeping WiFi up on USB

Goal: demonstrate that the device can join an *existing* WiFi network as a
**client** (station) while still acting as an access point, and that the WiFi
radio can be kept alive while the device is USB‑tethered.

**The "offload network" list is a dead end — do not use it.** The data engine
exposes `Wlan.Offload.SimplifiedNetworkList` (schema hint
`<SSID>,<passphrase>,<encryption_type>,<display_name>,<priority>`) but the
record‑store path is **OEM‑gated/stubbed**:

- `dx -c Wlan.Offload.SimplifiedNetworkList -a '<data>'` → `Error: Format/Parse`
  for **any** value — swept every encryption enum (0–10 + strings) and every
  field count (1–15), even a bare `AAA`. (The `-a` mechanism itself works: adding
  a correctly‑formatted record to `Wlan.Offload.BlockList` returns `tag:0`.)
- The privileged web handler `POST /Forms/offloadNetwork` returns HTTP 204 but
  persists **nothing** (neither in‑memory nor NV).
- Static RE of `NetgearWebApp` confirms the stored record is 5 fields
  (`formatOffloadNetwork` → `snprintf "%s,%s,%s,%s,%u"`); the 17‑field template
  `%s,%u,%u,%u.%u.%u.%u,…` belongs to the *enterprise* `NetworkList`, not
  Simplified. So no format trick is missing — the store path is simply dead.

**The real station path is live and driveable.** QCMAP (`QCMAP_ConnectionManager
/etc/mobileap_cfg.xml`) runs the device in **`<WlanMode>AP-STA`**:

| iface | role | process |
|-------|------|---------|
| **wlan1** | Access Point | `hostapd -B /tmp/hostapd.conf` (SSID `NTGR_ACE7`) |
| **wlan0** | **Station** (outward client) | `wpa_supplicant -i wlan0 -Dnl80211 -c /tmp/wpa_supplicant.conf` |

Drive the station directly, bypassing the gated list (simplified example —
replace `SOME_SSID`/`SOME_PASS` with a real network):

```bash
wpa_cli -i wlan0 status          # wpa_state=DISCONNECTED (ctrl sock /var/run/wpa_supplicant/wlan0)
wpa_cli -i wlan0 add_network     # → 0
wpa_cli -i wlan0 set_network 0 ssid '"SOME_SSID"'
wpa_cli -i wlan0 set_network 0 psk '"SOME_PASS"'
wpa_cli -i wlan0 enable_network 0
# then route wlan0 (uplink) → wlan1 / rndis0 as needed
```

Caveat: QCMAP owns the supplicant, so `wpa_cli`‑added networks may need to
coexist with / beat QCMAP's own reconfig. `QCMAP_StaInterface` (8 KB ELF) is
only event‑notify glue (→ `/etc/qcmap_sta_uds_file`), not where networks are
set.

**WiFi + USB at the same time — it's a power‑save behavior, not a lock.**
Two flags already permit WiFi on USB (`Power.WiFiOffOnPCUsb=0`,
`Power.WiFiOffOnTethered=0`), but `<WLANEnableAtBootup>0` (off at boot) plus the
**idle‑standby timers** put the whole radio subsystem to sleep — which is why the
hotspot vanished from the WiFi list when the screen went off. Fix (persists to NV):

```bash
dx -c Power.StandbyOnUsbIdleTimer  0   # was 300 — the USB culprit
dx -c Power.StandbyOnAcIdleTimer   0   # was 300 — wall charger, no PC
dx -c Power.StandbyOnBattIdleTimer 0   # was 300 — battery only
dx -c Wlan.WifiEnabled 1
dx -c Wlan.WifiRestart 1               # QCMAP respawns hostapd + wpa_supplicant
```

After this the LCD can still dim/sleep, but the radio stays up → SSID remains in
the WiFi list and associable while USB is connected. Revert any timer with
`dx -c Power.Standby...IdleTimer 300`. (Trade‑off: with all three at 0 the unit
never idle‑sleeps, so slightly more battery drain / heat off‑USB.) Note the main
AP encryption uses the **compact** enum `WPA2AES`; the station config uses the
underscore form `WPA2_Personal_AES` — different tables.

### Phase 8 — Bridging multiple access points that share the same gateway IP  ⭐

Problem: several separate Wi‑Fi devices — each its own small AP, each handing
out `192.168.100.1` to its clients, with no way to change that — can't be
joined to the same PC at once: their subnets and gateway IP collide. Solved
**entirely on the AirCard**, without touching the other devices: a per-target
station interface + **1:1 NETMAP** + **fwmark policy routing** gives each one
its own private subnet on the PC side.

Radio capability (`iw phy` interface combos): one PHY, `#{managed} <= 3,
#channels <= 2` → up to **2 targets on different channels, or 3 sharing
channels** (throughput is time-sliced across whichever share one channel).

Mechanism (needs no `veth`/`macvlan` — neither is compiled in): per-target
station VAP + 1:1 NETMAP remap + fwmark policy routing. Each target is
presented to the PC as a private subnet:

```
AP A  wlan0 → 10.9.1.1        AP B  wlan2 → 10.9.2.1   (both really 192.168.100.1)
```

Packet path: PC→`10.9.N.1` → mangle marks it N → NETMAP rewrites dst to the
real `192.168.100.x` → fwmark routes it out that target's radio → MASQUERADE
so the target replies to us → conntrack reverses everything. Per-interface
neigh entries (`192.168.100.1 dev wlanN`) prevent ARP conflict.

```bash
sh /tmp/multidrone.sh add 1 NETWORK_A   # slot1 → 10.9.1.1 (reuses QCMAP's wlan0 STA)
sh /tmp/multidrone.sh add 2 NETWORK_B   # slot2 → new wlan2 VAP + own supplicant → 10.9.2.1
sh /tmp/multidrone.sh status
sh /tmp/multidrone.sh del 2
```

On the PC, one summary route covers every slot: `route add 10.9.0.0 mask
255.255.0.0 192.168.1.1`.

> **Automated in this repo's app (slot 1).** The dashboard's **Drone Bridge**
> tab (`DroneBridge.Run`, in [`AC797/`](AC797/)) does the whole slot‑1 path.
> It's named for its original use — joining several Holy Stone FPV drones,
> each a fixed-IP AP of exactly this kind — but the mechanism itself is
> generic to any number of same-subnet, unconfigurable targets. Same thing
> from the CLI: `AC797.exe bridge <ssid>`.

### Phase 9 — Patching the device's own native UI (not just reading it)  ⭐
Both the admin portal's `index.html.tmpl` (`/mnt/hdata/index.html.tmpl`) and the
LCD screen's own `index.html.tmpl` (`/mnt/hdata/lcd/index.html.tmpl`) turned out to
be **not plain HTML** — they're a compiled, length-prefixed binary record format:
`1-byte opcode + 2-byte little-endian length + that many content bytes`. Opcode
`1` is a plain literal run (raw bytes, pass straight through); other opcodes
(symbol references, `available`/`writable` conditionals, multi-locale `?text`
blocks) encode dynamic directives that were **not** fully reverse-engineered —
and didn't need to be, since every edit below only ever splices new bytes into an
*existing* opcode-1 literal record, then rewrites that one record's 2-byte length
field. No other record's position or length changes, so the edit is safe without
understanding the rest of the format.

**A new tab added to the real stock portal, as a worked example.** The Settings
page's "AC797S Manager" field is bound to a read-only computed value
(`router.managerUrl`); the real, writable key behind it is the ordinary dx/NV key
`router.hostName`. [`tools/patch_general2.js`](tools/patch_general2.js) splices a
new `<li>` into the Settings nav `<ul>` and a new `<div id="settings_general2">`
panel into two existing literal records, with an inline `<script>` that reads/
writes `router.hostName` via the same `/model.json` + `POST /Forms/config`
mechanism every other setting on the device uses. No `script.js` changes were
needed at all — the portal's hash router is purely convention-driven
(`#settings/general2` → `document.getElementById("settings_general2")`), so
matching IDs is sufficient. One gotcha: `NetgearWebApp` caches the compiled
template **in memory**, so an on-disk edit doesn't take effect until the process
is restarted — kill it (`ps | grep NetgearWebApp`) and its watchdog
(`/usr/sbin/restartNetgearWebApp`) respawns it within seconds, now serving the
patched file.

**Three new screens on the physical LCD, reachable by its own Next/Prev buttons.**
[`tools/patch_lcd_screen.js`](tools/patch_lcd_screen.js) does the same binary
splice on the LCD's `index.html.tmpl` to add three new panels, plus a
*separate*, harder patch to `script.js`: the LCD's screen navigation is a real
finite-state machine (minified class `la`), and **every** feature module
(wifi/wps/sms/dataUsage/...) re-sets the *entire* Next/Prev cycle array each
time it loads — except there's exactly one module, `navigationStructureUI`,
registered dead last in the file, whose only job is to set the final,
authoritative cycle after every other module has run. The new screens are
registered on the shared root FSM and added to *that one* module's cycle
list, in order, right before "About"; patching any of the other, earlier
cycle-setters would've been silently overwritten. The three screens: a plain
**"AC797 / Hello from AC797"** proof-of-concept, a terminal-styled **"ROOT"**
screen showing the shell session that root access actually grants (`$ telnet
192.168.1.1` → `uid=0(root)`), and a **"SYSTEM"** screen listing real,
unchanging facts about this unit that no stock screen shows (kernel build,
RAM, the `wlan0`/`wlan1` MAC addresses) — read once off the device with
`AC797.exe sh` and baked in, since they don't change across reboots.

**The physical button isn't just a tap, either — SYSTEM demonstrates a hold
gesture too.** The LCD has exactly one physical button; its own firmware
(`script.js`'s `buttonUI` module) already tells a quick tap (dispatched as a
`NextEvent` on release) apart from a hold, firing a `holdInterval` event once
per second held. Whichever screen is currently showing gets that as a
`hold_interval(seconds)` callback on its own state *if it defines one* — the
exact mechanism the stock firmware already uses for its own hold-to-confirm
language/APN-selector screens and the WPS-cancel countdown. SYSTEM defines
one: holding the button (~1s) flips between two `<dl>`s on the same screen —
Kernel/Built/RAM, then SoC/`wlan0`/`wlan1` — a toggle rather than a new
top-level screen, so short Next/Prev presses keep cycling screens normally.
It fires on the very first tick to stay clear of two *unrelated*, button-wide
behaviors layered on top of every screen's hold_interval by other stock
modules: a real "Power" action at the 3s mark, and an unconditional jump to
the shutdown/factory-reset confirmation at 4s regardless of which screen was
showing (pre-existing stock behavior, left untouched). One more gotcha of its
own: the button's keyup handler dispatches `NextEvent` on *every* release,
held or not — there's no built-in distinction between "that was a tap" and
"that was a hold just ending" — so without more, releasing after a hold both
flips the page *and* advances to the next screen. Fixed with a `held` flag
(private to each screen's own registration, via a wrapping IIFE) that
`hold_interval` sets and a `next` override checks: if a hold just happened,
swallow that one `NextEvent`; otherwise fall through to the normal cycle.

A second, unrelated gotcha:
the LCD's `styles.css` only defines `z-index` rules for a fixed set of 23 panel
class names (`zero` through `twentytwo`), all already in use by the real
screens — a panel given an unstyled class name gets no stacking order and stays
invisible even while "active" in the FSM, since every panel is `position:
absolute` and stacks on top of each other. Fixed by reusing the existing
`twentytwo` class (harmless, since only one panel is ever visible — the one with
the `panelshow` class — at a time). The native `Gui` process (DirectFB+WebKit,
renders the LCD) isn't supervised the same way `NetgearWebApp` is for the web
portal, but it does have its own watchdog, `restartGui` — kill the `Gui` PID and
it respawns with the patched files within seconds.

### Custom LCD screens

Cycle past "About" with the physical Next button to reach the three screens
above; hold the button down (~1s) on **SYSTEM** for its second page.

<img width="565" height="425" alt="hello" src="https://github.com/user-attachments/assets/e63236b6-d943-4d5a-b6cf-318ee143f837" />
<img width="558" height="422" alt="root" src="https://github.com/user-attachments/assets/75efca9f-7d93-4b88-a14a-98eef0bc310c" />
<img width="562" height="422" alt="system" src="https://github.com/user-attachments/assets/3f625b08-998c-43c8-9063-e128a8ac6792" />
<img width="565" height="417" alt="more" src="https://github.com/user-attachments/assets/918b6a6c-833f-482d-9d08-6e9cbf29dd47" />




 
Both patches back up the original file on-device
(`/tmp/index.html.tmpl.orig.bak`, `/tmp/lcd_index.html.tmpl.orig.bak`,
`/tmp/lcd_script.js.orig.bak`) before deploying, and are regenerated from the
pristine source by their respective committed scripts rather than hand-edited —
`node tools/patch_general2.js <src> <out>` / `node tools/patch_lcd_screen.js <src
dir> <out dir>`, then pushed with `AC797.exe pushbin <file> /tmp/x.bin` (binary-
safe over telnet, with MD5 verification both directions) and copied into place
after `mount -o remount,rw /mnt/hdata`.

**Browser dashboard — a reverse proxy onto the real portal.** Running
`AC797.exe` with no arguments starts this server in the background and puts an
icon in the Windows task tray instead of opening a window; right-clicking it
offers **Open Upgraded Portal** (this dashboard), **Open Original Portal**
(the device's own stock UI, unproxied), and **Exit** — each opens the
default browser. (The same server is also reachable headless via `AC797.exe
webui`, and the original WinForms GUI still exists in the codebase for anyone
who wants it, just no longer wired up as the default.) It starts a local
server at `http://127.0.0.1:8787/`. Every path except
`/_tool/*` transparently reverse-proxies to the device's own stock admin portal
at `http://192.168.1.1/`, so the **entire real portal** (power/LCD/sleep
settings, WiFi bands, port forwarding/filtering/DMZ, APN, SIM security,
schedulers, access control, battery status) is reachable at this app's own
address, without the browser ever navigating to the device's IP. Cookies pass
through byte-for-byte in both directions; the device's own absolute-URL
redirects (its session-cookie handshake) get rewritten back to this origin so
login never leaves the proxy. On top of the proxy, the portal's own left-hand
nav gets one new top-level entry per custom capability (Status, dx Console, AT
Console, Root, WiFi, Drone Bridge, Files, Firmware Backup, SMS), built from the portal's own
markup idioms so it looks native rather than embedded — this works because the
portal's router is generic, matching the URL hash to an element `id` via
`document.getElementById`. The header also gets a small brand mark + GitHub
link (to this repo) next to the portal's own unused logo slot, and the stock
footer gets a second GitHub link (to the account's profile) — both plain
HTML/CSS insertions, no functional change.

New capabilities beyond status/console/wifi: a **Files** tab (root-telnet
browser — list/read/download any path as `.tar.gz`, with image preview), and a
**Firmware Backup** tab that dumps raw NAND partitions straight off `/proc/mtd`
via their read-only `/dev/mtdNro` nodes (never the read-write node — no erase
risk), gzipped on-device, downloadable individually or bundled into one `.zip`.
AT commands go straight to the modem over `/dev/smd7` via the root shell, not
the OEM's `:5510` `nc` bridge. **SMS needs a SIM inserted** — on this unit
`AT+CPIN?` reports `SIM not inserted`, so `AT+CMGF`/`CMGW`/`CMGL` all answer
`ERROR`; the portal's own `#messaging` page is empty for the same reason.

---

## 4. What's in this folder

```
AirCard797/
├─ README.md                ← this file
├─ .gitignore               ← keeps big/generated artifacts out of git
├─ AC797/                    ← the C# app (WinForms GUI + CLI + tray icon) — implements everything below
│   ├─ Aircard.cs            ← OpenLockKeygen, WebApi (incl. GetBytesAsync/PostMultipartAsync),
│   │                           AtPort, AtShell, RootShell (incl. PushFile)
│   ├─ AircardRoot.cs        ← AircardRoot.EnsureRoot, OffloadStation (station join)
│   ├─ DroneBridge.cs        ← DroneBridge.Run — multi-AP bridge (Phase 8), embeds multidrone.sh
│   ├─ PcNet.cs              ← PC-side half of the bridge: static IP + 10.9.0.0/16 route,
│   │                           embeds fix-ac797-route.ps1 the same way
│   ├─ WebUi.cs              ← `AC797.exe webui` — reverse proxy onto the real portal, with this
│   │                           app's own tabs (Status/dx/AT/Root/WiFi/Drone Bridge/Files/
│   │                           Firmware Backup/SMS) injected as native top-level nav entries;
│   │                           own dashboard + `/api/*` moved under `/_tool`
│   ├─ Cli.cs / Program.cs   ← headless CLI driver (incl. `pushbin`) + tray-icon/GUI entry point
│   └─ Form1.cs / .Designer.cs ← WinForms GUI (Status / Settings / dx / AT tabs) — kept in the
│                                 codebase but no longer the default entry point (see below)
├─ rootfs_full.tar.gz       ← the complete BROWSABLE filesystem (4037 files, 85 MB) — gitignored
├─ tools/                   ← standalone reference scripts (same logic AC797/ embeds)
│   ├─ openlock.py          ← Sierra MDM9x40 OPENLOCK keygen (self-validating)
│   ├─ unlock.py            ← live: query challenge → answer → unlock
│   ├─ setflags.py          ← unlock → enable TELNETENABLE + ADBENABLE
│   ├─ sh.py                ← reusable root-telnet "run command" helper
│   ├─ pull.py              ← firmware/partition dumper (health-checked)
│   ├─ multidrone.sh        ← on-device multi-AP bridge script (Phase 8), pushed by DroneBridge.Run
│   ├─ unbridge.sh          ← reverts multidrone.sh's NAT/routing + the station join (Phase 8)
│   ├─ fix-ac797-route.ps1  ← PC-side static IP + 10.9.0.0/16 route for the bridge (Phase 8)
│   ├─ unfix-ac797-route.ps1 ← reverts fix-ac797-route.ps1
│   ├─ patch_general2.js    ← adds the General2 settings tab to the real admin portal (Phase 9)
│   └─ patch_lcd_screen.js  ← adds three new screens to the physical LCD's own nav (Phase 9)
└─ raw/                     ← byte-exact partition images (11 of 20; see below) — gitignored
```

### Partitions captured (raw, byte‑exact)
`sbl · boot · scrub · misc · recovery · fota_none · recoveryfs · pad1 · userrw ·
cust · persist` — **103.7 MB total**, checksums in `MANIFEST.txt`.

### Partitions intentionally **not** captured
- **Modem/boot‑owned (unsafe to read live):** `mibib · efs2 · tz · rpm · modem`
  — these reboot the device; they are also *never‑rewrite* partitions.
- **Redundant with the tar:** `system` (= the rootfs, already browsable in the
  tarball) and `hdata` (= `/mnt/hdata`, also in the tarball).
- **Skipped by choice:** `ntgfota` (117 MB FOTA update store — the only genuinely
  new item left; not pulled to avoid another watchdog reboot).

### Full flash map (for reference)
```
mtd0  sbl        mtd5  aboot     mtd10 recovery    mtd15 userrw
mtd1  mibib      mtd6  boot      mtd11 fota_none   mtd16 hdata
mtd2  efs2       mtd7  scrub     mtd12 recoveryfs  mtd17 ntgfota
mtd3  tz         mtd8  modem     mtd13 system      mtd18 cust
mtd4  rpm        mtd9  misc      mtd14 pad1        mtd19 persist
```

---

## 5. Using the dump

**Browse the filesystem** (everything readable, no special tools):
```bash
mkdir rootfs && tar xzf rootfs_full.tar.gz -C rootfs
# then explore rootfs/etc, rootfs/usr, rootfs/firmware/image (modem+wifi blobs), ...
```

**Unpack the UBIFS raw images** (`userrw`, and if you ever grab `system`/`hdata`)
with `ubi_reader`:
```bash
pip install ubi_reader
ubireader_extract_files raw/mtd15_userrw.img -o userrw_extracted
```

**Firmware blobs** — `rootfs/firmware/image/` holds the loadable modem
(`modem.b00`–`modem.b20`, `modem.mdt`), WiFi (`qwlan30.bin`, `bdwlan30*.bin`), and
DSP/BT firmware as files. Load these directly into Ghidra/IDA; no partition
extraction needed.

---

## 6. Access & credentials reference (this unit)

| What | Value / location |
|---|---|
| Device IP / host | `192.168.1.1` / `mywebui` |
| Admin web password | `7bLAKi2P`  (also in `/mnt/userrw/ntgnv/ADMINPASS`) |
| WiFi SSID / pass | `NTGR_ACE7` / `d436UoP6` (guest `NTGR_ACE7-5G`) |
| Root shell | `telnet 192.168.1.1` (port 23, no password) |
| Root pw hash | `/etc/passwd`: `root:C98ULvDZe7zQ2` (DES; likely factory‑default) |
| AT port | `nc 192.168.1.1 5510` |
| Config engine | `dx <group>` to read, `dx -c <group.item> <val>` to write |
| NV store | 329 plaintext files in `/mnt/userrw/ntgnv/` |

---

## 7. Reverting / cleanup

Telnet + adb root are currently **on and unauthenticated** — anyone on this
device's WiFi/USB can reach the root shell. Fine for a bench unit; to close it
back up when finished:

```
# via the AT port (re-unlock first), or from the root shell using dx / at:
AT!OPENLOCK?  /  AT!OPENLOCK="..."
AT!CUSTOM="TELNETENABLE",0
AT!CUSTOM="ADBENABLE",0
AT!RESET
```

A factory reset (web UI or `AT!` ) also clears the flags. The device additionally
has a `recovery` partition and Qualcomm EDL mode as independent fallbacks.

To restore **stock power/standby behavior** (see Phase 7 — these were zeroed so
WiFi stays up on USB):

```
dx -c Power.StandbyOnUsbIdleTimer  300
dx -c Power.StandbyOnAcIdleTimer   300
dx -c Power.StandbyOnBattIdleTimer 300
```

---

## 8. Don't‑brick rules

- **Never write** `aboot`, `tz`, `rpm`, `mibib`, `modem`, `efs2` — bricking or
  loss of cellular calibration lives here.
- **Never** `nandwrite` a raw image back unless you fully understand OOB/ECC; the
  images here are ECC‑corrected `mtdblock` reads, not raw‑with‑spare.
- Don't cut power during any flag write / reboot — let it reboot itself.
- Reads are always safe; the reboots seen here were watchdog resets from reading
  modem‑owned partitions, not corruption.

---

## 9. Prior art — who's done this before (and where they stopped)

Short version: **the family method is well documented on sibling models; this
exact model — and this depth — is not.** None of the work below was used here
(this was done independently from the AT port up), but it's worth recording for
context.

### On the sibling models (MDM9x40 family) — extensively documented
The **NETGEAR MR1100 "Nighthawk M1"** shares the same Qualcomm MDM9x40 platform.
Two independent public routes to root exist:

1. **AT!OPENLOCK + sierrakeygen → enable telnet** — the same chain used here.
   Documented on forums like *Wireless Joint* and *cdmagurus*. The keygen
   (`bkerler/edl`) explicitly supports device generation `MDM9x40`.
2. **Command‑injection vuln** — Michael Callahan's Medium writeup used a DefCon 27
   config‑API injection bug plus a firmware downgrade to inject `busybox telnetd`,
   landing at `root/oelinux123`.

**How far they got:** almost everyone **stopped at "I have a root shell."**
Callahan's article ends at the shell (it was step 1 of a carrier‑conversion
project). The forum community mostly used root for **LTE band‑locking** and
**carrier conversion** — not filesystem/partition archaeology. No sibling writeup
found does a full partition‑level firmware dump.

### On the AC797S specifically — essentially nobody, and not for this
Search the AC797S / "AT&T Unite Express 2" and virtually **everything is carrier
SIM‑unlocking** (Howard Forums, DC‑unlocker, cellcorner, YouTube) — a completely
different goal (unlock the *SIM* for another network, not root the OS). No public
writeup of rooting the AC797S, enabling telnet, or dumping its firmware was found.
Tellingly, sierrakeygen's MDM9x40 device list names the **MR1100, AC815s, AC785s**
— **not** the AC797S. The family method was proven on cousins; this exact unit
wasn't in anyone's published list.

### What this notebook adds
| | Typical sibling writeups | This work |
|---|---|---|
| Target model | MR1100 / AC81x / AC78x | **AC797S** (not in documented lists) |
| Method | AT!OPENLOCK keygen *or* injection vuln | AT!OPENLOCK keygen (reimplemented + self‑validated) |
| Endpoint reached | root shell | root shell **+ full recon** |
| Firmware dump | not documented | **browsable rootfs + 11 byte‑exact partitions** |
| New findings | — | **modem‑owned‑partition watchdog reboot**; dormant `Wlan.Offload` (`Supported=1`) station mode; native UI patching (web portal + LCD) |

They proved the door could be opened on the cousins and walked away at the
threshold. Here we opened it on a model nobody had published, then mapped the
whole house.

**Sources:** Callahan – *Gaining root on the MR1100* (Medium) · Wireless Joint –
*enable root telnet* (wirelessjoint.com) · cdmagurus – *MR1100 adb/root* ·
`bkerler/edl` sierrakeygen · DC‑unlocker & Howard Forums – *AC797S carrier unlock*.

---

## 10. References

- `bkerler/edl` → `edlclient/Tools/sierrakeygen.py` (the OPENLOCK algorithm)
- MR1100 rooting write‑ups (mt‑tech.fi "Enable diag on Netgear MR1100") — same
  MDM9x40 recipe, applies to the AC797S.
- Qualcomm MDM9640 / Sierra Wireless AT command reference.
