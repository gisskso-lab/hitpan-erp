#!/usr/bin/env python3
# W-G1-51 갈래 Z 11차 D1′(작업지시서 고침 1 §8-1 ③ · §8-2 · §8-3 3 / 고침 2 §2 · §3 · §5) — 측정 글꼴 이름표 고침 + 파일 사실 기록(판정 0)
#   쓰는 법: /usr/bin/python3 font-rename.py <작업 폴더(원본 regular.ttf · bold.ttf · 업로드 뿌리 밖)> <사실 파일>
#   산출: <작업 폴더>/print-substitute-regular.ttf · print-substitute-bold.ttf(글리프 · 굵기 · 다른 표 무변 · name 표만)
#   사실 파일 = 글자만(글꼴 바이트 0) · 판정기(judge-print.js fontGate)가 이름 기록을 다시 센다
#   끝 코드: 0 = 사실 이상 없음 / 1 = 첫 줄에 이름 붙은 사유(C4′) — 판정은 T8 이 함(§3-3′ 1′)
import hashlib, json, os, re, sys  # 표준 라이브러리 + apt 꾸러미 fontTools 만(pip 0)

KEEP = {0, 7, 8, 9, 11, 12, 13, 14}  # 보존(법무 ㉢ · PM ③ · 바이트 그대로)
BAN = re.compile(r'nanum|naver|나눔|네이버', re.I)  # 셈 범위 = KEEP 뺀 모든 기록 · 플랫폼 · 언어(고침 2 §2 · ID 20 포함)
WIN_EN = (3, 1, 0x409)
ROLES = {'regular': ('Regular', 'PrintSubstitute-Regular', 'Print Substitute Regular'),  # 중립 이름(§8-2 · Nanum · Naver · Malgun 0)
         'bold': ('Bold', 'PrintSubstitute-Bold', 'Print Substitute Bold')}
REWRITE = (1, 2, 3, 4, 6, 16, 17, 18)  # 이 ID 는 모두 지우고 영어(윈도) 한 기록만 다시 씀(18 = 지움만)

def text(r):
    return r.toUnicode(errors='replace')

def kept(name):
    return sorted((r.nameID, r.platformID, r.platEncID, r.langID, bytes(r.string)) for r in name.names if r.nameID in KEEP)

def fix(TTFont, work, role):
    sub, ps, full = ROLES[role]
    src = os.path.join(work, role + '.ttf')
    dst = os.path.join(work, 'print-substitute-%s.ttf' % role)
    f = TTFont(src)
    name = f['name']
    before = kept(name)
    win = {r.nameID: text(r) for r in name.names if (r.platformID, r.platEncID, r.langID) == WIN_EN}
    ids = {r.nameID for r in name.names}
    style = lambda i: win[i] if i in win and not BAN.search(win[i]) else sub
    want = {1: 'Malgun Gothic', 2: style(2), 3: ps + ';ci', 4: full, 6: ps}
    if 16 in ids:
        want[16] = 'Malgun Gothic'
    if 17 in ids:
        want[17] = style(17)
    name.names = [r for r in name.names if r.nameID in KEEP or (r.nameID not in REWRITE and not BAN.search(text(r)))]
    for i, v in sorted(want.items()):
        name.setName(v, i, *WIN_EN)
    f.save(dst)
    g = TTFont(dst)
    rs = [(r.nameID, r.platformID, r.langID, text(r)) for r in g['name'].names]
    banned = sum(1 for (i, p, l, v) in rs if i not in KEEP and (BAN.search(v) or (i in (4, 6) and re.search('malgun', v, re.I))))
    fam = [v for (i, p, l, v) in rs if i in (1, 16)]
    data = open(dst, 'rb').read()
    fact = {
        'role': role, 'file': os.path.basename(dst), 'size': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
        'weight': g['OS/2'].usWeightClass, 'family': fam, 'id4': [v for (i, p, l, v) in rs if i == 4], 'id6': [v for (i, p, l, v) in rs if i == 6],
        'banned': banned, 'id21_22': sum(1 for x in rs if x[0] in (21, 22)), 'preserveSame': kept(g['name']) == before,
        'names': [{'id': i, 'p': p, 'l': l, 'v': v[:120]} for (i, p, l, v) in rs],
    }
    why = []
    if not fam or any(v != 'Malgun Gothic' for v in fam):
        why.append('글꼴 단계 — 파일 사실 불일치(%s family ≠ Malgun Gothic)' % role)
    if banned:
        why.append('글꼴 단계 — 파일 사실 불일치(%s 금지 낱말 %d)' % (role, banned))
    if not fact['preserveSame']:
        why.append('글꼴 단계 — 파일 사실 불일치(%s 보존 기록 다름)' % role)
    if fact['id6'] != [ps]:
        why.append('글꼴 단계 — 파일 사실 불일치(%s ID 6 ≠ 중립 이름)' % role)
    if fact['id21_22']:
        why.append('이름 고친 뒤 ID 21 또는 22 있음(%s)' % role)
    return fact, why

def main(work, out):
    facts = {'schema': 'font-facts/1', 'files': [], 'why': []}
    try:
        from fontTools.ttLib import TTFont
    except Exception as e:  # 도구 꾸러미 없음 = 이름 붙은 사유(빈 catch 0)
        facts['why'].append('글꼴 단계 — 이름 고침 실패(도구 불러오기 %s)' % type(e).__name__)
        TTFont = None
    for role in ROLES if TTFont else ():
        if not os.path.isfile(os.path.join(work, role + '.ttf')):
            facts['why'].append('글꼴 단계 — 원본 파일 없음(%s)' % role)
            continue
        try:
            fact, why = fix(TTFont, work, role)
        except Exception as e:  # 이름 고침 실패 = 이름 붙은 사유(빈 catch 0)
            facts['why'].append('글꼴 단계 — 이름 고침 실패(%s %s)' % (role, type(e).__name__))
            continue
        facts['files'].append(fact)
        facts['why'] += why
    facts['ok'] = not facts['why']
    with open(out, 'w', encoding='utf-8') as fp:
        json.dump(facts, fp, ensure_ascii=False, indent=1)
    print(facts['why'][0] if facts['why'] else '글꼴 단계 사실 — 이상 없음(보통 · 굵게)')
    return 0 if facts['ok'] else 1

if __name__ == '__main__':
    sys.exit(main(sys.argv[1], sys.argv[2]))
