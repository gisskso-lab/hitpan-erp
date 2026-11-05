import json, sys, collections
rows = json.load(open(sys.argv[1], encoding='utf-8'))
def proj(f):
    p = f.split('/'); return p[0] + '/' + p[1]
tot = collections.defaultdict(collections.Counter)
det = []
for r in rows:
    if not (r['strong'] or r['weak']): continue
    if proj(r['f']) == 'src/HitPan.Web': continue
    t = tot[proj(r['f'])]
    t['lits'] += 1
    t['files_' + r['f']] = 1
    if r['unbalanced']: t['unbalanced'] += 1; det.append(('UNBAL', r))
    for m in r['marks']:
        key = m[0] + '@' + m[1]
        if m[0] == '--' and m[1] == 'code':
            key = '--code_next=' + ('EOL' if m[2] in ('\r', '\n', 'EOL') else ('TAB' if m[2] == '\t' else 'other'))
        t[key] += 1
        if m[1].startswith('quote') or m[0] in ('?', '#', '/*'):
            det.append((key, r))
for p, t in sorted(tot.items()):
    nf = sum(1 for k in t if k.startswith('files_'))
    print(p, 'files', nf, {k: v for k, v in t.items() if not k.startswith('files_')})
print('--- detail ---')
for k, r in det:
    print(k, r['f'], r['ln'], repr(r['text'][:120]))
