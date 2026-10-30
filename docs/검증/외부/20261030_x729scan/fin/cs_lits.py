# C# string literal extractor + SQL-region classifier (static, read-only)
# usage: py -3 -I cs_lits.py <worktree> <filelist.txt> <out.json>
import sys, json, re, os

def lex(src):
    """yield (line, kind, content, holes_text) for every string literal; holes replaced by \x00"""
    out = []
    i, n, line = 0, len(src), 1
    def adv(k):
        nonlocal i, line
        line += src.count('\n', i, i + k)
        i += k
    def read_code_until_brace(depth_close):
        # lex C# code inside an interpolation hole until matching '}' * depth_close at depth 0
        nonlocal i, line
        depth = 0
        start = i
        while i < n:
            c = src[i]
            if c == '/' and src.startswith('//', i):
                j = src.find('\n', i); j = n if j < 0 else j
                adv(j - i); continue
            if c == '/' and src.startswith('/*', i):
                j = src.find('*/', i + 2); j = n if j < 0 else j + 2
                adv(j - i); continue
            if c in '"$@' or (c == "'"):
                if lit_at():
                    continue
            if c in '([{':
                depth += 1; adv(1); continue
            if c in ')]':
                depth -= 1; adv(1); continue
            if c == '}':
                if depth == 0:
                    return src[start:i]
                depth -= 1; adv(1); continue
            adv(1)
        return src[start:i]
    def lit_at():
        # try to lex a literal at i; return True if consumed
        nonlocal i, line
        m = re.compile(r'(\$*)(@?)(\$*)("+)').match(src, i)
        if src[i] == "'":
            # char literal
            j = i + 1
            if j < n and src[j] == '\\': j += 2
            else: j += 1
            while j < n and src[j] != "'" and j - i < 12: j += 1
            adv(j + 1 - i); return True
        if not m:
            return False
        d1, at, d2, q = m.groups()
        dollars = len(d1) + len(d2)
        if src[i] in '$@' and not m: return False
        startline = line
        if len(q) >= 3:  # raw string
            nq = len(q)
            adv(m.end() - i)
            buf = []; holes = []
            while i < n:
                if src.startswith('"' * nq, i):
                    adv(nq); break
                if dollars and src.startswith('{' * dollars, i) and not src.startswith('{' * (dollars + 1), i):
                    adv(dollars)
                    h = read_code_until_brace(dollars)
                    holes.append(h); buf.append('\x00')
                    if i < n: adv(dollars)
                    continue
                buf.append(src[i]); adv(1)
            out.append((startline, 'raw', ''.join(buf), holes)); return True
        if len(q) == 2 and not at:
            # empty string ""
            adv(m.end() - i); out.append((startline, 'reg', '', [])); return True
        if len(q) == 2 and at:
            # @"" then maybe more; treat as verbatim start where first " opens and next " ... handle generically
            pass
        verbatim = bool(at)
        adv(m.start(4) - i + 1)  # past opening quote
        buf = []; holes = []
        while i < n:
            c = src[i]
            if verbatim:
                if c == '"':
                    if i + 1 < n and src[i + 1] == '"':
                        buf.append('"'); adv(2); continue
                    adv(1); break
            else:
                if c == '\\':
                    buf.append(src[i:i + 2]); adv(2); continue
                if c == '"':
                    adv(1); break
                if c == '\n':
                    break
            if dollars:
                if src.startswith('{{', i):
                    buf.append('{'); adv(2); continue
                if src.startswith('}}', i):
                    buf.append('}'); adv(2); continue
                if c == '{':
                    adv(1)
                    h = read_code_until_brace(1)
                    holes.append(h); buf.append('\x00')
                    if i < n: adv(1)
                    continue
            buf.append(c); adv(1)
        out.append((startline, 'verb' if verbatim else 'reg', ''.join(buf), holes)); return True
    while i < n:
        c = src[i]
        if c == '/' and src.startswith('//', i):
            j = src.find('\n', i); j = n if j < 0 else j
            adv(j - i); continue
        if c == '/' and src.startswith('/*', i):
            j = src.find('*/', i + 2); j = n if j < 0 else j + 2
            adv(j - i); continue
        if c in '"$@\'':
            if lit_at(): continue
        adv(1)
    return out

SQLKW = re.compile(r'\b(SELECT|INSERT\s+INTO|UPDATE\s+\w+|DELETE\s+FROM|FROM|WHERE|JOIN|VALUES|ON\s+DUPLICATE|LIMIT|ORDER\s+BY|GROUP\s+BY|CREATE\s+TABLE|ALTER\s+TABLE|SET\s+\w+\s*=)\b', re.I)
SQLKW_STRONG = re.compile(r'\b(SELECT\b.*\bFROM|INSERT\s+INTO|DELETE\s+FROM|UPDATE\s+`?\w+`?\s+(\w+\s+)?SET|WHERE\s+\w|JOIN\s+\w|CREATE\s+TABLE|ALTER\s+TABLE|ON\s+DUPLICATE\s+KEY)', re.I | re.S)

def sql_regions(s):
    """walk SQL text; return list of (char, region) for interesting chars and flags"""
    res = []
    i, n = 0, len(s)
    st = 'code'
    q = None
    while i < n:
        c = s[i]
        if st == 'code':
            if c in "'\"`":
                st = 'quote'; q = c; i += 1; continue
            if s.startswith('--', i):
                nxt = s[i + 2] if i + 2 < n else 'EOL'
                res.append(('--', 'code', i, nxt))
                j = s.find('\n', i); i = n if j < 0 else j; continue
            if s.startswith('/*', i):
                res.append(('/*', 'code', i, None))
                j = s.find('*/', i + 2); i = n if j < 0 else j + 2; continue
            if c == '#':
                res.append(('#', 'code', i, None))
            if c == '?':
                res.append(('?', 'code', i, None))
            i += 1; continue
        else:
            if c == '\\':
                res.append(('\\', 'quote' + q, i, s[i + 1:i + 2])); i += 2; continue
            if c == q:
                if i + 1 < n and s[i + 1] == q:
                    i += 2; continue
                st = 'code'; i += 1; continue
            if c in '?@':
                res.append((c, 'quote' + q, i, None))
            if c == '-' and s.startswith('--', i):
                res.append(('--', 'quote' + q, i, None))
            i += 1
    return res, (st != 'code')

def main():
    wt, flist, outp = sys.argv[1:4]
    files = [l.strip() for l in open(flist, encoding='utf-8') if l.strip()]
    rows = []
    for f in files:
        p = os.path.join(wt, f)
        src = open(p, 'rb').read().decode('utf-8-sig', errors='replace')
        for (ln, kind, content, holes) in lex(src):
            if not content: continue
            strong = bool(SQLKW_STRONG.search(content))
            weak = bool(SQLKW.search(content))
            reg, unbalanced = sql_regions(content)
            rows.append(dict(f=f, ln=ln, kind=kind, strong=strong, weak=weak,
                             hasq='?' in content, unbalanced=unbalanced,
                             marks=[(a, b, d) for (a, b, _, d) in reg],
                             nholes=len(holes), holes=[h[:160] for h in holes], holesq=any('?' in h for h in holes),
                             text=content[:400]))
    json.dump(rows, open(outp, 'w', encoding='utf-8'), ensure_ascii=False)
    print('files', len(files), 'literals', len(rows), 'sql_strong', sum(r['strong'] for r in rows))

main()
