import json, sys, collections
rows = json.load(open(sys.argv[1], encoding='utf-8'))
mode = sys.argv[2]
def proj(f):
    p = f.split('/')
    return p[0] + '/' + p[1]
if mode == 'q':
    c = collections.Counter()
    for r in rows:
        if r['hasq']:
            c[(proj(r['f']), 'strong' if r['strong'] else ('weak' if r['weak'] else 'none'))] += 1
    for k, v in sorted(c.items()): print(k, v)
    print('--- strong/weak with ? ---')
    for r in rows:
        if r['hasq'] and (r['strong'] or r['weak']):
            qs = [m for m in r['marks'] if m[0] == '?']
            print(f"{r['f']}:{r['ln']} [{r['kind']}] qmarks={qs} :: {r['text'][:160]!r}")
elif mode == 'qnone':
    for r in rows:
        if r['hasq'] and not (r['strong'] or r['weak']):
            print(f"{r['f']}:{r['ln']} :: {r['text'][:90]!r}")
elif mode == 'bo':
    pref = sys.argv[3]
    sel = [r for r in rows if r['f'].startswith(pref) and (r['strong'] or r['weak'])]
    print('literals(sqlish)', len(sel), 'strong', sum(r['strong'] for r in sel), 'files', len({r['f'] for r in sel}))
    agg = collections.Counter()
    for r in sel:
        for m in r['marks']:
            agg[(m[0], m[1], m[2] if m[0] == '--' else '')] += 1
        if r['unbalanced']: agg[('UNBAL', '', '')] += 1
    for k, v in sorted(agg.items()): print(k, v)
    print('--- detail (non-trivial marks) ---')
    for r in sel:
        ms = [m for m in r['marks'] if not (m[0] == '--' and m[1] == 'code' and m[2] not in ('EOL', '\n', '\r', '\t'))]
        if ms or r['unbalanced']:
            print(f"{r['f']}:{r['ln']} unbal={r['unbalanced']} marks={ms} :: {r['text'][:140]!r}")
