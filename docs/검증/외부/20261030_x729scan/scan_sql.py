# -*- coding: utf-8 -*-
# 읽기 전용 정적 스캐너: git blob(고정 SHA) 원문을 읽어 SQL 꼴을 센다. DB 접속 0.
import subprocess, sys, re, json, io

REPO = sys.argv[1]
SHA = sys.argv[2]
OUT = sys.argv[3]

def git(*a):
    return subprocess.run(["git", "-C", REPO] + list(a), stdout=subprocess.PIPE, check=True).stdout

def files(pattern):
    out = git("ls-tree", "-r", "--name-only", SHA, "--", pattern).decode("utf-8")
    return [l for l in out.splitlines() if l]

SETS = {
    "ERP_DB": [f for f in files("src/HitPan.API/Migrations/SQL") if re.search(r"/DB-[^/]*\.sql$", f)],
    "BO": [f for f in files("installer/backoffice") if f.endswith(".sql")],
}

# ---- 서버 기준 렉서 (MySQL/MariaDB 규칙 근사) ----
# 반환: 코드 영역에서 본 '?' 위치, '@이름' 위치, DELIMITER 위치, 문장끝 ';' 수
# client 모드:
#   'server' : '--' 뒤 공백/제어문자면 주석, 그 줄 끝(\n)까지. /*! */ 안은 코드.
#   'c25'    : '--' 뒤 ' ' 일 때만 주석(그 \n 까지). /* */ 전부 주석.
#   'c26'    : '--' 뒤 글자 c<=0x20 또는 0x7F 면 주석. c 를 먹고 다음 \n 까지 (c=='\n' 이면 다음 줄 끝까지).
def lex(s, mode):
    i, n = 0, len(s)
    qs, ats, delims, semis = [], [], [], 0
    line_start = True
    while i < n:
        c = s[i]
        if c == '-' and i + 1 < n and s[i+1] == '-':
            nxt = s[i+2] if i + 2 < n else ''
            is_c = False
            if mode == 'server':
                is_c = (nxt == '' or ord(nxt) <= 0x20 or nxt == '\x7f')
                if is_c:
                    j = s.find('\n', i + 2)
                    i = n if j < 0 else j  # \n 은 다음 반복에서 코드로 처리
                    continue
            elif mode == 'c25':
                is_c = (nxt == ' ')
                if is_c:
                    j = s.find('\n', i + 3)
                    i = n if j < 0 else j
                    continue
            elif mode == 'c26':
                is_c = (nxt != '' and (ord(nxt) <= 0x20 or nxt == '\x7f'))
                if is_c:
                    j = s.find('\n', i + 3)   # 시작 글자(nxt)를 먹은 뒤부터 \n 을 찾는다
                    i = n if j < 0 else j
                    continue
            i += 2
            continue
        if c == '#':
            j = s.find('\n', i)
            i = n if j < 0 else j
            continue
        if c == '/' and i + 1 < n and s[i+1] == '*':
            if mode == 'server' and i + 2 < n and s[i+2] == '!':
                i += 3
                # 버전 숫자 건너뜀 → 내용은 코드로 계속
                while i < n and s[i].isdigit():
                    i += 1
                continue
            j = s.find('*/', i + 2)
            i = n if j < 0 else j + 2
            continue
        if mode == 'server' and c == '*' and i + 1 < n and s[i+1] == '/':
            i += 2
            continue
        if c in "'\"`":
            q = c
            j = i + 1
            while j < n:
                if s[j] == '\\' and q != '`':
                    j += 2
                    continue
                if s[j] == q:
                    if j + 1 < n and s[j+1] == q:
                        j += 2
                        continue
                    break
                j += 1
            i = j + 1
            continue
        if c == '?':
            qs.append(i)
        elif c == '@':
            m = re.match(r"@@?[A-Za-z0-9_$.]+|@[`'\"]", s[i:i+80])
            ats.append((i, m.group(0) if m else '@'))
            if m:
                i += len(m.group(0))
                continue
        elif c == ';':
            semis += 1
        elif c in 'Dd' and re.match(r"(?i)DELIMITER\b", s[i:i+10]):
            # 문장 머리(줄 처음 공백 뒤)일 때만 지시문
            k = s.rfind('\n', 0, i)
            if s[k+1:i].strip() == '':
                delims.append(i)
        i += 1
    return qs, ats, delims, semis

def lineof(s, pos):
    return s.count('\n', 0, pos) + 1

def raw_counts(s):
    L = s.split('\n')
    r = {k: [] for k in ["dash_eol", "dash_tab", "dash_nows", "hash", "block", "vercomment",
                          "q_any", "at_user", "at_quoted", "atat", "prepare", "delimiter",
                          "bs_quote", "dbl_quote_esc"]}
    for no, ln in enumerate(L, 1):
        if re.search(r"--$", ln): r["dash_eol"].append(no)
        if "--\t" in ln: r["dash_tab"].append(no)
        if re.search(r"(?<!-)--[^\s-]", ln) and not ln.lstrip().startswith("--"): r["dash_nows"].append(no)
        if re.search(r"(^|\s)#", ln): r["hash"].append(no)
        if "/*" in ln: r["block"].append(no)
        if "/*!" in ln: r["vercomment"].append(no)
        if "?" in ln: r["q_any"].append(no)
        if re.search(r"(?<!@)@[A-Za-z_]", ln): r["at_user"].append(no)
        if re.search(r"@[`'\"]", ln): r["at_quoted"].append(no)
        if "@@" in ln: r["atat"].append(no)
        if re.search(r"(?i)^\s*(PREPARE|EXECUTE|DEALLOCATE)\b", ln): r["prepare"].append(no)
        if re.search(r"(?i)^\s*DELIMITER\b", ln): r["delimiter"].append(no)
        if "\\'" in ln: r["bs_quote"].append(no)
        if re.search(r"'[^']*''[^']*'", ln): r["dbl_quote_esc"].append(no)
    return r

result = {}
for setname, fl in SETS.items():
    per = []
    for f in fl:
        s_lf = git("show", f"{SHA}:{f}").decode("utf-8", errors="replace")
        has_cr = '\r' in s_lf
        s_lf = s_lf.replace('\r\n', '\n')
        s_crlf = s_lf.replace('\n', '\r\n')
        rc = raw_counts(s_lf)
        diff = {}
        for eol, s in (("LF", s_lf), ("CRLF", s_crlf)):
            sv = lex(s, 'server'); c25 = lex(s, 'c25'); c26 = lex(s, 'c26')
            def proj(x):
                return {"q": sorted(lineof(s, p) for p in x[0]),
                        "at": sorted((lineof(s, p), t) for p, t in x[1]),
                        "delim": sorted(lineof(s, p) for p in x[2]),
                        "semi": x[3]}
            diff[eol] = {"server": proj(sv), "c25": proj(c25), "c26": proj(c26)}
        per.append({"file": f, "blob_has_cr": has_cr, "lines": s_lf.count('\n') + 1,
                    "raw": rc, "lex": diff})
    result[setname] = per

with io.open(OUT, "w", encoding="utf-8") as fp:
    json.dump(result, fp, ensure_ascii=False)
print("ok", {k: len(v) for k, v in result.items()})
