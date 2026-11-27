"""Compara o caminho midstate+nonce (espelho do shader) com hashlib."""
import hashlib, os, random, struct, subprocess, sys

here = os.path.dirname(os.path.abspath(__file__))
exe = os.path.join(here, "test_sha256_cpu")
subprocess.check_call(["g++", "-O2", "-std=c++17", "-o", exe,
                       os.path.join(here, "test_sha256_cpu.cpp")])

GENESIS = ("01000000" + "00" * 32 +
           "3ba3edfd7a7b12b27ac72c3e67768f617fc81bc3888a51323a9fb8aa4b1e5e4a"
           "29ab5f49" "ffff001d" "1dac2b7c")
cases = [(bytes.fromhex(GENESIS), 2083236893)]
random.seed(7)
for _ in range(300):
    cases.append((os.urandom(80), random.getrandbits(32)))

inp = "\n".join(f"{h.hex()} {n}" for h, n in cases) + "\n"
out = subprocess.run([exe], input=inp, capture_output=True, text=True, check=True).stdout.split("\n")

bad = 0
for (h, n), line in zip(cases, out):
    full = h[:76] + struct.pack("<I", n)
    ref = hashlib.sha256(hashlib.sha256(full).digest()).digest()
    top = struct.unpack("<I", ref[28:32])[0]          # 32 bits mais significativos (LE)
    digest_hex, top_hex = line.split()
    if digest_hex != ref.hex() or int(top_hex, 16) != top:
        bad += 1
print(f"{len(cases)} casos, {bad} divergências")

g = out[0].split()[0]
print("genesis (ordem de exibição):", bytes.fromhex(g)[::-1].hex())
sys.exit(1 if bad else 0)
