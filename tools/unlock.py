#!/usr/bin/env python3
import socket, time, re, sys
from openlock import Keygen

HOST, PORT = "192.168.1.1", 5510

def recv(s, t=1.5):
    s.settimeout(t)
    buf = b""
    try:
        while True:
            d = s.recv(4096)
            if not d: break
            buf += d
    except socket.timeout:
        pass
    return buf.decode(errors="replace")

def send(s, cmd, t=1.5):
    s.sendall((cmd + "\r\n").encode())
    time.sleep(0.2)
    return recv(s, t)

s = socket.create_connection((HOST, PORT), timeout=5)
recv(s, 0.5)

r = send(s, "AT!OPENLOCK?")
print("== AT!OPENLOCK? ==\n" + r.strip())
m = re.search(r'\b([0-9A-Fa-f]{16})\b', r)
if not m:
    print("!! no challenge found"); sys.exit(1)
challenge = m.group(1).upper()
resp = Keygen().response(challenge)
print(f"\nchallenge = {challenge}\nresponse  = {resp}\n")

r = send(s, f'AT!OPENLOCK="{resp}"')
print(f'== AT!OPENLOCK="{resp}" ==\n' + r.strip())

# verify: these errored before unlock
for c in ["AT!USBCOMP?", "AT!CUSTOM=?", "AT!ENTERCND?"]:
    print(f"\n== {c} ==\n" + send(s, c).strip())
s.close()
