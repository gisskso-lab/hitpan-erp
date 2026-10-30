# -*- coding: utf-8 -*-
# 「주석을 여는 첫 '--' 가 줄 끝」인 줄과 그 다음 줄의 성격을 센다 (LF 기준 = 2.6.x 에서 다음 줄을 먹는 꼴)
import subprocess, sys, re, io, collections
REPO, SHA, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
pats = sys.argv[4:]
def git(*a):
    return subprocess.run(["git", "-C", REPO] + list(a), stdout=subprocess.PIPE, check=True).stdout
fl = []
for p in pats:
    fl += [l for l in git("ls-tree", "-r", "--name-only", SHA, "--", p).decode("utf-8").splitlines()
           if re.search(r"\.sql$", l) and ("Migrations/SQL/DB-" in l or "installer/backoffice/" in l)]
out = io.open(OUT, "w", encoding="utf-8")
kinds = collections.Counter()
for f in fl:
    s = git("show", f"{SHA}:{f}").decode("utf-8", errors="replace").replace("\r\n", "\n")
    L = s.split("\n")
    for i, ln in enumerate(L):
        # 줄 안에서 따옴표 밖 첫 '--' 찾기 (단순: 따옴표 짝 세기)
        q = None; pos = -1; j = 0
        while j < len(ln):
            c = ln[j]
            if q:
                if c == '\\' and q != '`': j += 2; continue
                if c == q: q = None
            else:
                if c in "'\"`": q = c
                elif c == '#': break
                elif ln.startswith('--', j):
                    nx = ln[j+2] if j + 2 < len(ln) else '\n'
                    if nx == '\n' or ord(nx) <= 0x20: pos = j
                    break
            j += 1
        if pos >= 0 and pos + 2 == len(ln):
            nxt = L[i+1] if i + 1 < len(L) else '<EOF>'
            st = nxt.strip()
            if nxt == '<EOF>': k = 'EOF'
            elif st == '': k = 'blank'
            elif st.startswith('--') or st.startswith('#'): k = 'comment'
            else: k = 'CODE'
            kinds[k] += 1
            if k == 'CODE' or (k != 'comment' and k != 'blank'):
                out.write(f"{f}:{i+1}  next[{k}]: {nxt[:150]}\n")
            if k == 'CODE':
                if re.search(r"[?@'\";]|/\*|DELIMITER", nxt, re.I):
                    out.write(f"   !! next line has risky char\n")
out.write(f"KINDS {dict(kinds)} files {len(fl)}\n")
out.close()
print(dict(kinds))
