#!/usr/bin/env python3
"""Pull files/partitions off the AirCard (root telnet) to the PC.
Design: PC connects TO the device (avoids Windows inbound firewall).
For each item, device runs `<producer> | nc -l -p PORT`; PC connects, drains to file.
"""
import socket, time, sys, os, hashlib

DEV = "192.168.1.1"
TELNET_PORT = 23
XFER_PORT = 9000
OUTDIR = r"C:\Users\johnb\source\repos\HS175D\aircard_dump"
IAC,DONT,DO,WONT,WILL=255,254,253,252,251

def neg(data,s):
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

def read_prompt(s, timeout=8):
    s.settimeout(0.5); buf=b""; start=time.time()
    while time.time()-start<timeout:
        try:
            d=s.recv(4096)
            if not d: break
            buf+=neg(d,s)
            if buf.rstrip().endswith(b"#"): break
        except socket.timeout:
            if buf.rstrip().endswith(b"#"): break
    return buf

class Shell:
    def __init__(self):
        self.s=socket.create_connection((DEV,TELNET_PORT),timeout=6)
        read_prompt(self.s,4)
    def cmd_noblock(self, c):
        self.s.sendall((c+"\n").encode())
    def resync(self):
        return read_prompt(self.s,6).decode("utf-8","replace")
    def cmd(self, c, t=12):
        self.s.sendall((c+"\n").encode())
        return read_prompt(self.s,t).decode("utf-8","replace")

def pull(sh, producer, outpath, expect=None, label=""):
    # start listener on device (blocks the shell until transfer done)
    sh.cmd_noblock(f"{producer} | nc -l -p {XFER_PORT}")
    time.sleep(1.2)  # let nc bind
    # connect from PC, retrying
    conn=None
    for attempt in range(15):
        try:
            conn=socket.create_connection((DEV,XFER_PORT),timeout=6); break
        except OSError:
            time.sleep(0.5)
    if conn is None:
        print(f"  !! {label}: could not connect to listener"); return False
    conn.settimeout(30)
    total=0; h=hashlib.md5()
    t0=time.time()
    with open(outpath,"wb") as f:
        while True:
            try: d=conn.recv(65536)
            except socket.timeout: break
            if not d: break
            f.write(d); h.update(d); total+=len(d)
    conn.close()
    dt=time.time()-t0
    sh.resync()  # shell returns to prompt after nc exits
    ok = (expect is None) or (total==expect)
    flag = "OK" if ok else f"SIZE MISMATCH (got {total} exp {expect})"
    print(f"  {label:14s} {total:>10d} bytes  {total/1e6/max(dt,0.01):5.1f} MB/s  md5={h.hexdigest()}  {flag}")
    return ok

# partition table (name, mtdblock index, size bytes) from /proc/mtd
PARTS = [
    ("sbl",0,0x140000),("mibib",1,0x140000),("efs2",2,0x1680000),("tz",3,0x100000),
    ("rpm",4,0xa0000),("aboot",5,0x100000),("boot",6,0xa40000),("scrub",7,0x40000),
    ("modem",8,0x46c0000),("misc",9,0x180000),("recovery",10,0xa60000),("fota_none",11,0xc0000),
    ("recoveryfs",12,0x16c0000),("system",13,0x8920000),("pad1",14,0xf80000),("userrw",15,0x1440000),
    ("hdata",16,0x3ac0000),("ntgfota",17,0x75c0000),("cust",18,0xa00000),("persist",19,0x600000),
]

# SKIP boot-critical / modem QC partitions on a LIVE system (never-rewrite anyway):
# 1 mibib (crashed on this), 3 tz, 4 rpm, 5 aboot, 8 modem.  0 sbl + 19 persist already done.
SKIP = {1,3,4,5,8}
# order safe partitions small->large to build confidence
SAFE_ORDER = [7,9,11,18,6,10,14,15,12,2,16,17,13]

def health(sh):
    out=sh.cmd("cat /proc/uptime; cat /sys/class/thermal/thermal_zone0/temp",6)
    up=None; temp=None
    for tok in out.split():
        try:
            v=float(tok)
            if up is None: up=v
            elif temp is None and v>1000: temp=v/1000.0
        except ValueError: pass
    return up, temp

def main():
    which = sys.argv[1] if len(sys.argv)>1 else "safe"
    sh=Shell()
    os.makedirs(os.path.join(OUTDIR,"raw"),exist_ok=True)
    if which=="tar":
        prod=("tar czf - -C / --exclude=proc --exclude=sys --exclude=dev "
              "--exclude=tmp --exclude=run --exclude=media --exclude=var/volatile .")
        pull(sh, prod, os.path.join(OUTDIR,"rootfs_full.tar.gz"), None, "rootfs.tar.gz")
        sh.s.close(); print("[done]"); return
    order = SAFE_ORDER if which=="safe" else [i for (_,i,_) in PARTS]
    by_i = {i:(n,sz) for (n,i,sz) in PARTS}
    last_up,_ = health(sh)
    print(f"[*] start uptime={last_up:.0f}s")
    for i in order:
        if which!="safe" and i in SKIP:
            print(f"  mtd{i:<2d} SKIP (boot-critical)"); continue
        n,sz = by_i[i]
        out = os.path.join(OUTDIR,"raw",f"mtd{i}_{n}.img")
        if os.path.exists(out) and os.path.getsize(out)==sz:
            print(f"  mtd{i}_{n:<12s} already complete, skip"); continue
        ok = pull(sh, f"cat /dev/mtdblock{i}", out, sz, f"mtd{i}_{n}")
        up,temp = health(sh)
        tinfo = f"{temp:.0f}C" if temp else "?"
        if up is None or up < last_up:
            print(f"  !! REBOOT DETECTED after mtd{i}_{n} (uptime {up} < {last_up}). Aborting."); break
        print(f"     health: uptime={up:.0f}s temp={tinfo}")
        last_up = up
        time.sleep(2)  # throttle between partitions
    sh.s.close()
    print("[done]")

if __name__=="__main__":
    main()
