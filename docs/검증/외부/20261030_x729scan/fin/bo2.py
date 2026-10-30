import json, sys, collections
rows = json.load(open(sys.argv[1], encoding='utf-8'))
pref = sys.argv[2]
sel = [r for r in rows if r['f'].startswith(pref) and (r['strong'] or r['weak'])]
q = [r for r in sel if "'" in r['text']]
print('sqlish', len(sel), 'with quote char (first400)', len(q))
c = collections.Counter()
BS = chr(92)
for r in sel:
    t = r['text']
    c['has_at'] += '@' in t
    c['has_backslash'] += BS in t
    c['has_hole'] += r['nholes'] > 0
    c['kind_' + r['kind']] += 1
    c['quoted_marks'] += sum(1 for m in r['marks'] if m[1].startswith('quote'))
print(dict(c))
for r in q[:5]:
    print(r['f'].split('/')[-1], r['ln'], repr(r['text'][:150]))
print('long>=400', sum(1 for r in sel if len(r['text']) >= 400))
bs = [r for r in sel if BS in r['text']]
for r in bs[:8]:
    i = r['text'].index(BS)
    print('BS', r['f'].split('/')[-1], r['ln'], r['kind'], repr(r['text'][max(0, i - 40):i + 20]))
