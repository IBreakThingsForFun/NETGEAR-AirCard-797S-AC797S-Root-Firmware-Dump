#!/usr/bin/env python3
import socket, time, re, sys
from openlock import Keygen
HOST, PORT = "192.168.1.1", 5510

def recv(s, t=1.5):
    s.settimeout(t); buf=b""
    try:
        while True:
            d=s.recv(4096)
            if not d: break
            buf+=d
    except socket.timeout: pass
    return buf.decode(errors="replace")

def send(s, cmd, t=1.5):
    s.sendall((cmd+"\r\n").encode()); time.sleep(0.25); return recv(s,t)

s=socket.create_connection((HOST,PORT),timeout=5); recv(s,0.5)

# re-unlock
r=send(s,"AT!OPENLOCK?")
ch=re.search(r'\b([0-9A-Fa-f]{16})\b',r).group(1).upper()
resp=Keygen().response(ch)
u=send(s,f'AT!OPENLOCK="{resp}"')
print(f"unlock: {ch} -> {resp} : {'OK' if 'OK' in u else 'FAIL: '+u.strip()}")

# set flags
for flag in ["TELNETENABLE","ADBENABLE"]:
    r=send(s,f'AT!CUSTOM="{flag}",1')
    print(f'set {flag}=1 -> {"OK" if "OK" in r and "ERROR" not in r else "ERROR: "+r.strip()}')

# read back
print("\n== AT!CUSTOM? (verify) ==")
r=send(s,"AT!CUSTOM?")
for line in r.splitlines():
    if any(k in line for k in ("TELNETENABLE","ADBENABLE","USBSERIALENABLE","NATENABLED")):
        print("   "+line.strip())
s.close()
