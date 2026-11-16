#!/usr/bin/env python3
"""Reusable root-telnet shell for the AirCard (192.168.1.1:23).
Usage: python sh.py "cmd1" "cmd2" ...   (reads each until the shell prompt)
Or import run_cmds([...]).
"""
import socket, time, sys
IAC,DONT,DO,WONT,WILL=255,254,253,252,251
PROMPT=b"# "

def _neg(data,s):
    out=bytearray(); i=0
    while i<len(data):
        b=data[i]
        if b==IAC and i+2<len(data):
            c,o=data[i+1],data[i+2]
            if c in (DO,DONT): s.sendall(bytes([IAC,WONT,o]))
            elif c in (WILL,WONT): s.sendall(bytes([IAC,DONT,o]))
            i+=3; continue
        out.append(b); i+=1
    return bytes(out)

def _read_until_prompt(s, timeout=8):
    s.settimeout(0.6); buf=b""; start=time.time()
    while time.time()-start < timeout:
        try:
            d=s.recv(4096)
            if not d: break
            buf+=_neg(d,s)
            if buf.rstrip().endswith(b"#"):
                # small grace read
                time.sleep(0.1)
                try:
                    while True:
                        d=s.recv(4096)
                        if not d: break
                        buf+=_neg(d,s)
                except socket.timeout: pass
                break
        except socket.timeout:
            if buf.rstrip().endswith(b"#"): break
    return buf.decode("utf-8","replace")

def run_cmds(cmds, host="192.168.1.1", port=23):
    s=socket.create_connection((host,port),timeout=6)
    _read_until_prompt(s,4)          # eat banner/prompt
    results=[]
    for c in cmds:
        s.sendall((c+"\n").encode())
        out=_read_until_prompt(s,12)
        # strip echoed command (first line) and trailing prompt
        lines=out.replace("\x00","").split("\n")
        if lines and c.split("\n")[0][:30] in lines[0]:
            lines=lines[1:]
        body="\n".join(lines)
        # drop trailing prompt line
        body="\n".join([l for l in body.split("\n") if l.strip()!="/ #" and not l.strip().endswith("/ #")]).rstrip()
        results.append((c,body))
    s.close()
    return results

def main():
    for c,out in run_cmds(sys.argv[1:]):
        print("\n===== $ %s ====="%c)
        print(out)

if __name__=="__main__":
    main()
