import json,sys,collections
rows=json.load(open(sys.argv[1],encoding='utf-8'))
c=collections.Counter()
for r in rows:
    if not(r['strong'] or r['weak']): continue
    for m in r['marks']:
        if m[0]=='--' and m[1]=='code' and m[2] in ('\r','\n','EOL','\t'):
            c[(r['f'],repr(m[2]))]+=1
for k,v in sorted(c.items()): print(k,v)
print(sum(c.values()))
