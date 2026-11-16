#!/usr/bin/env python3
# Clean reimplementation of the Sierra MDM9x40 AT!OPENLOCK keygen.
# Faithful port of the algorithm (SierraInit/SierraPreInit/SierraAlgo) for
# device generation MDM9x40. Validated against a known-answer test vector.
from binascii import unhexlify, hexlify

# MDM9x40 parameters (prodtable entry)
INIT = [7, 3, 0, 1, 5]
# keytable index 11 * 16  (NTG9X40C / AC815s / MR1100 OpenLock key)
KEY = bytearray([0x8A, 0x56, 0x03, 0xF0, 0xBB, 0x9C, 0x13, 0xD2,
                 0x4E, 0xB2, 0x45, 0xAD, 0xC4, 0x0A, 0xE7, 0x52])
CLEN = 8

class Keygen:
    def __init__(self):
        self.tbl = [0] * 0x100
        self.rtbl = [0] * 0x14

    def pre_init(self, counter, key, keylen, challengelen, mcount):
        if counter != 0:
            tmp2 = 0
            i = 1
            while i < counter:
                i = 2 * i + 1
            while True:
                tmp = mcount
                mcount = tmp + 1
                challengelen = (key[tmp & 0xFF] + self.tbl[(challengelen & 0xFF)]) & 0xFF
                if mcount >= keylen:
                    mcount = 0
                    challengelen = ((challengelen & 0xFF) + keylen) & 0xFF
                tmp2 += 1
                tmp3 = ((challengelen & 0xFF) & i) & 0xFF
                if tmp2 >= 0xB:
                    tmp3 = counter % tmp3
                if tmp3 <= counter:
                    break
            counter = tmp3 & 0xFF
        return counter, challengelen, mcount

    def init(self, key, keylen):
        self.tbl = [(i & 0xFF) for i in range(0x100)]
        mcount = 0
        cl = keylen & 0xffffff00
        i = 0xFF
        while i > -1:
            t, cl, mcount = self.pre_init(i, key, keylen, cl, mcount)
            m = self.tbl[i]
            self.tbl[i] = self.tbl[t & 0xFF]
            i -= 1
            self.tbl[t & 0xFF] = m
        for k in range(5):
            self.rtbl[k] = self.tbl[INIT[k]] if INIT[k] != 0 else self.tbl[cl & 0xFF]

    def algo(self, challenge, a, b, c, d, e, ret, ret2, flag):
        v6 = self.rtbl[e]
        v0 = (v6 + 1) & 0xFF
        self.rtbl[e] = v0
        self.rtbl[c] = (self.tbl[v6 + flag & 0xFF] + self.rtbl[c]) & 0xFF
        v4 = self.rtbl[c] & 0xFF
        v2 = self.rtbl[b] & 0xFF
        v1 = self.tbl[v2 & 0xFF]
        self.tbl[v2 & 0xFF] = self.tbl[v4 & 0xFF]
        v5 = self.rtbl[d] & 0xFF
        self.tbl[v4 & 0xFF] = self.tbl[v5 & 0xFF]
        self.tbl[v5 & 0xFF] = self.tbl[v0 & 0xFF]
        self.tbl[v0] = v1 & 0xFF
        u = self.tbl[(self.tbl[(self.tbl[((self.rtbl[a] + self.tbl[v1 & 0xFF]) & 0xFF)] +
                                self.tbl[v5 & 0xFF] + self.tbl[v2 & 0xFF] & 0xff)] & 0xFF)]
        v = self.tbl[((self.tbl[v4 & 0xFF] + v1) & 0xFF)]
        self.rtbl[ret] = u ^ v ^ challenge
        self.rtbl[a] = (self.tbl[v1 & 0xFF] + self.rtbl[a]) & 0xFF
        self.rtbl[ret2] = challenge & 0xFF
        return self.rtbl[ret] & 0xFF

    def response(self, challenge_hex):
        challenge = bytearray(unhexlify(challenge_hex))
        self.init(KEY, 16)
        out = bytearray(CLEN)
        for i in range(CLEN):
            # MDM9x40 run: SierraAlgo(challenge[i], 4,2,1,0,3, 2,0,0)
            out[i] = self.algo(challenge[i], 4, 2, 1, 0, 3, 2, 0, 0)
        return hexlify(out).decode().upper()

if __name__ == "__main__":
    import sys
    # self-test
    tv_c, tv_r = "BE96CBBEE0829BCA", "1033773720F6EE66"
    got = Keygen().response(tv_c)
    print(f"[selftest] {tv_c} -> {got}  expected {tv_r}  {'PASS' if got==tv_r else 'FAIL'}")
    if len(sys.argv) > 1:
        ch = sys.argv[1].strip().upper()
        print(f"[live]     {ch} -> {Keygen().response(ch)}")
