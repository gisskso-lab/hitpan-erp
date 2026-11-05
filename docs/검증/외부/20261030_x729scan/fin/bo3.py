import json, sys, re
rows = json.load(open(sys.argv[1], encoding='utf-8'))
pref = sys.argv[2]
allr = [r for r in rows if r['f'].startswith(pref)]
print('== literals with interpolation holes (sqlish) ==')
for r in allr:
    if (r['strong'] or r['weak']) and r['nholes']:
        print(r['f'].split('/')[-1], r['ln'], r['kind'], repr(r['text'][:200]))
print('== non-sqlish literals that look like SQL fragments (@param or = or AND/OR/IN) ==')
FR = re.compile(r'(@[A-Za-z_]\w*|\bAND\b|\bOR\b|\bIN\s*\(|\bIS\s+NULL|\bNOW\(|\bLIKE\b|\bCOUNT\(|\bASC\b|\bDESC\b)')
for r in allr:
    if not (r['strong'] or r['weak']) and FR.search(r['text']):
        print(r['f'].split('/')[-1], r['ln'], r['kind'], 'marks=', r['marks'], repr(r['text'][:140]))
