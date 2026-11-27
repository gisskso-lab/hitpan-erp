#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
G16 교체 시간 실측 — SQL 생성기 · 결과 정리기 (측정 전용 · 제품 코드 아님 · 머지 금지 가지)

근거: W-G1-52 설계 §15-2 (가-2) 한 트랜잭션 지우고 붓기 · §15-9 G16 · R11
     작업지시서 §7-1 K2 · §7-3 #12 (문턱 X = PM 이 실측 뒤 정함)

이 파일은 DB 에 붙지 않는다. g16-swap-measure.sh 가 CI 러너의 mariadb:11.4 컨테이너에서
information_schema 를 TSV 로 떠 오면, 그것을 읽어 SQL 을 stdout 으로 낸다.

부속 명령:
  plan     meta 디렉터리 → plan.json (표 목록 · 교체 대상 · 위상 순서 · 근사 조정 기록)
  seed     plan.json + (calib N | target-bytes + bpr.json) → 자료 붓기 SQL
  calib    sizes.tsv + counts.tsv → bpr.json (행당 바이트)
  copy     rs_new__ 표 만들기 + 붓기 SQL (제한 계정이 돌림)
  swap     교체 트랜잭션 SQL (제한 계정이 돌림)
  diverge  rs_new__ 쪽 잎 표에서 일부 행을 빼는 SQL (대조군 — 「교체됨」 과 「되돌려짐」 을 가름)
  counts   행 수 SQL (--prefix '' 또는 rs_new__)
  checksum CHECKSUM TABLE SQL
  analyze  ANALYZE TABLE SQL
  mkrec    한 측정 결과를 JSON 한 줄로
  report   results.jsonl → result.md + result.json
"""
import argparse
import hashlib
import json
import os
import sys

DB = 'g16'
PREFIX = 'rs_new__'

# ── 「지금 값」 목록 (설계 §5 ① ~ ④ + §15-4 지금 값 추가 21) ──────────────────────────
#   분류 상수(가칭 RestoreTableClassification)는 아직 코드에 없다([3] 몫).
#   측정은 이 근사 목록으로 「백업 값 = 나머지」 를 교체 대상으로 삼는다.
#   §5 ③-예외 3(stock_adjust_logs · haccp_logs · mold_production_log) · idempotency_keys 는
#   설계 추천대로 백업 값(교체 대상) 쪽.
CURRENT = [
    # ① 계정
    'users', 'user_permissions', 'user_terms_consent', 'refresh_tokens', 'user_sessions',
    # ② 회사정보
    'tenants', 'local_company', 'local_subscription', 'subscriptions', 'tenant_settings',
    'tenant_certificates', 'tenant_etax_settings', 'tenant_devices',
    # ③ 로그
    'audit_logs', 'audit_trail', 'login_attempts', 'force_edit_logs', 'device_login_logs',
    'security_alerts', 'ai_usage_logs',
    # ④ 구조 · 설정
    '__efmigrationshistory', 'backup_settings', 'backup_history', 'restore_history',
    'email_settings', 'billing_invoices', 'billing_payment_attempts', 'billing_payment_methods',
    'billing_settings', 'billing_subscriptions', 'common_codes', 'form_templates',
    'approval_settings', 'workflow_settings', 'sync_tokens',
    # §15-4 지금 값 추가 21
    'schema_migrations', 'device_register_tokens', 'device_slot_policy_settings',
    'ai_export_consents', 'local_update_consents', 'local_update_status',
    'local_update_apply_status', 'local_update_attempts', 'cs_requests', 'cs_outbox',
    'cs_replies', 'cs_rating_outbox', 'cs_forbidden_rejects', 'tenant_devices_snapshot',
    'tenant_employees_snapshot', 'fax_settings', 'geocoding_settings', 'chat_file_settings',
    'print_common_settings', 'labor_policy_settings', 'hitpan_knowledge',
]

# ── 큰 표 비중 (교체 대상 업무 자료 바이트 중 몫) ─────────────────────────────────────
#   고객 DB 실측 분포는 없다(설계 §15-9 「고객 DB 크기 = 확인 필요」). 아래는 업무 흐름 구조로 세운 가설:
#   · 재고 원장 = 입고 · 출고 · 반품 줄마다 1행 + 조정 → 가장 큼
#   · 분개 줄 = 전표마다 2행 이상 → 둘째
#   · 판매 · 매입 · 계산서 · 견적 = 머리 1 : 줄 3 ~ 5
#   · 수금 · 지급 · 통장 · 현금출납 · 상태이력 = 중간
#   나머지 교체 대상 표 = 표마다 30행(작은 표).
WEIGHTS = {
    'stock_ledger': 20.0,
    'journal_lines': 14.0,
    'journal_entries': 5.0,
    'sales_delivery_items': 9.0,
    'sales_deliveries': 3.0,
    'sales_order_items': 6.0,
    'sales_orders': 2.0,
    'purchase_receipt_items': 6.0,
    'purchase_receipts': 2.0,
    'purchase_order_items': 4.0,
    'purchase_orders': 1.5,
    'tax_invoice_items': 4.0,
    'tax_invoices': 2.0,
    'quotation_items': 3.0,
    'quotations': 1.0,
    'status_history': 3.0,
    'collections': 2.0,
    'payments': 2.0,
    'bank_transactions': 2.0,
    'cashbook': 2.0,
    'card_payment_lines': 1.0,
    'inventory_lots': 1.0,
    'ledger_balance_snapshot': 1.0,
    'approval_documents': 0.8,
    'approval_history': 0.7,
    'items': 1.0,
    'item_stock': 0.5,
    'partners': 0.5,
}
SMALL_ROWS = 30
CHUNK = 200000


# ───────────────────────────── 메타 읽기 ─────────────────────────────

def unesc(s):
    out = []
    i = 0
    while i < len(s):
        c = s[i]
        if c == '\\' and i + 1 < len(s):
            n = s[i + 1]
            out.append({'t': '\t', 'n': '\n', '\\': '\\', '0': '\0'}.get(n, n))
            i += 2
        else:
            out.append(c)
            i += 1
    return ''.join(out)


def read_tsv(path):
    rows = []
    with open(path, encoding='utf-8') as f:
        for line in f:
            line = line.rstrip('\n').rstrip('\r')
            if not line:
                continue
            rows.append([unesc(x) for x in line.split('\t')])
    return rows


def q(name):
    return '`' + name.replace('`', '``') + '`'


def load_meta(d):
    tables = [r[0] for r in read_tsv(os.path.join(d, 'tables.tsv'))]
    cols = {t: [] for t in tables}
    for r in read_tsv(os.path.join(d, 'cols.tsv')):
        t, name, _pos, dt, ct, nullable, key, extra, ln, prec, scale, gen = r[:12]
        if t not in cols:
            continue
        cols[t].append({
            'name': name, 'dt': dt.lower(), 'ct': ct, 'nullable': nullable == 'YES',
            'key': key, 'extra': extra.lower(), 'len': int(ln or 0),
            'prec': int(prec or 0), 'scale': int(scale or 0),
            'generated': (gen.upper() == 'ALWAYS') or ('generated' in extra.lower()),
        })
    pk = {t: [] for t in tables}
    for r in read_tsv(os.path.join(d, 'pk.tsv')):
        if r[0] in pk:
            pk[r[0]].append(r[1])
    uniq = {t: set() for t in tables}
    for r in read_tsv(os.path.join(d, 'uniq.tsv')):
        if r[0] in uniq:
            uniq[r[0]].add(r[1])
    fks = {}
    for r in read_tsv(os.path.join(d, 'fk.tsv')):
        cname, t, c, _ord, rt, rc = r[:6]
        key = (t, cname)
        fks.setdefault(key, {'name': cname, 't': t, 'cols': [], 'rt': rt, 'rcols': []})
        fks[key]['cols'].append(c)
        fks[key]['rcols'].append(rc)
    return {'tables': tables, 'cols': cols, 'pk': pk, 'uniq': {k: sorted(v) for k, v in uniq.items()},
            'fks': list(fks.values())}


def topo(tables, fks):
    tset = set(tables)
    deps = {t: set() for t in tables}
    for fk in fks:
        if fk['t'] in tset and fk['rt'] in tset and fk['t'] != fk['rt']:
            deps[fk['t']].add(fk['rt'])
    order, done, cyc = [], set(), []
    remaining = sorted(tables)
    while remaining:
        ready = [t for t in remaining if deps[t] <= done]
        if not ready:
            cyc = list(remaining)
            order.extend(remaining)
            break
        for t in ready:
            order.append(t)
            done.add(t)
        remaining = [t for t in remaining if t not in done]
    return order, cyc


def cmd_plan(a):
    m = load_meta(a.meta)
    tables = m['tables']
    notes = []
    missing_current = [t for t in CURRENT if t not in tables]
    if missing_current:
        notes.append('지금 값 목록에 있으나 DDL 에 없는 표: ' + ', '.join(missing_current))
    current = [t for t in tables if t in CURRENT]
    swap = [t for t in tables if t not in CURRENT]
    # 설계 §15-4 G13 ⓒ 「지금 값 자식 → 백업 값 부모」 외래키가 있으면 교체 중 DELETE 가 막힌다.
    # 측정 근사 목록이므로 그 자식을 교체 대상으로 옮기고 기록한다(설계 판정 아님).
    changed = True
    while changed:
        changed = False
        for fk in m['fks']:
            if fk['t'] in current and fk['rt'] in swap:
                current.remove(fk['t'])
                swap.append(fk['t'])
                notes.append('근사 조정: %s(지금 값) → %s(교체 대상) 외래키 %s 때문에 %s 를 교체 대상으로 옮김'
                             % (fk['t'], fk['rt'], fk['name'], fk['t']))
                changed = True
    all_order, cyc_all = topo(tables, m['fks'])
    swap_order, cyc_swap = topo(swap, m['fks'])
    if cyc_swap:
        notes.append('교체 대상 외래키 고리: ' + ', '.join(cyc_swap))
    self_refs = sorted({fk['t'] for fk in m['fks'] if fk['t'] == fk['rt']})
    children = {fk['rt'] for fk in m['fks'] if fk['t'] != fk['rt']}
    big = [t for t in WEIGHTS if t in swap]
    absent_big = [t for t in WEIGHTS if t not in swap]
    if absent_big:
        notes.append('큰 표 비중 목록 중 교체 대상에 없는 표(비중에서 뺌): ' + ', '.join(absent_big))
    leaf_big = [t for t in big if t not in children]
    plan = {
        'tables': tables, 'current': sorted(current), 'swap': sorted(swap),
        'all_order': all_order, 'swap_order': swap_order, 'cycles_all': cyc_all,
        'self_refs': self_refs, 'big': big, 'leaf_big': leaf_big, 'notes': notes,
        'counts': {'base_tables': len(tables), 'current': len(current), 'swap': len(swap),
                   'fks': len(m['fks'])},
    }
    json.dump(plan, sys.stdout, ensure_ascii=False, indent=1)


# ───────────────────────────── 값 만들기 ─────────────────────────────

def colhash(t, c):
    return int(hashlib.md5(('%s.%s' % (t, c)).encode('utf-8')).hexdigest()[:6], 16) % 9973 + 1


def enum_values(coltype):
    inner = coltype[coltype.index('(') + 1: coltype.rindex(')')]
    vals, cur, inq, i = [], '', False, 0
    while i < len(inner):
        ch = inner[i]
        if inq:
            if ch == "'" and i + 1 < len(inner) and inner[i + 1] == "'":
                cur += "''"
                i += 2
                continue
            if ch == '\\' and i + 1 < len(inner):
                cur += inner[i:i + 2]
                i += 2
                continue
            if ch == "'":
                inq = False
                vals.append("'" + cur + "'")
                cur = ''
                i += 1
                continue
            cur += ch
        elif ch == "'":
            inq = True
        i += 1
    return vals


STRING_TYPES = ('char', 'varchar', 'tinytext', 'text', 'mediumtext', 'longtext', 'json')


def val_expr(t, col, E):
    dt, ct = col['dt'], col['ct'].lower()
    L, p, s = col['len'], col['prec'], col['scale']
    h = colhash(t, col['name'])
    if col['name'] == 'tenant_id':
        # 한 PC = 한 회사(로컬 ERP) — 모든 행이 같은 tenant_id (색인 모양을 실물에 가깝게)
        if dt in ('tinyint', 'smallint', 'mediumint', 'int', 'integer', 'bigint'):
            return '1'
        if dt in ('char', 'varchar'):
            return "LEFT('g16tenant',%d)" % max(1, L)
    if dt == 'tinyint':
        return 'MOD(%s,2)' % E if ct.startswith('tinyint(1)') else 'MOD(%s,100)' % E
    if dt == 'smallint':
        return 'MOD(%s,30000)' % E
    if dt == 'mediumint':
        return 'MOD(%s,8000000)' % E
    if dt in ('int', 'integer', 'bigint'):
        return '(%s)' % E
    if dt in ('decimal', 'numeric'):
        ip = max(p - s, 0)
        if ip == 0:
            return 'ROUND(MOD(%s,97)/100,%d)' % (E, s) if s >= 2 else '0'
        mx = 10 ** min(ip, 12) - 1
        if mx <= 1:
            return '0'
        return 'ROUND(MOD(%s*7919+%d,%d)+0.37,%d)' % (E, h, mx, s)
    if dt in ('float', 'double', 'real'):
        return '(MOD(%s,10000)+0.5)' % E
    if dt == 'bit':
        return '0'
    if dt in ('char', 'varchar'):
        if L <= 0:
            return "''"
        cap = min(L, 40)
        if cap >= 12:
            return "LEFT(CONCAT('한',%s,'-',MD5(%s+%d)),%d)" % (E, E, h, cap)
        return 'LEFT(CONCAT(%s),%d)' % (E, cap)
    if dt in ('tinytext', 'text', 'mediumtext', 'longtext', 'json'):
        return "CONCAT('{\"v\":\"',%s,'-',MD5(%s+%d),'\"}')" % (E, E, h)
    if dt == 'date':
        return "DATE_ADD('2021-01-01', INTERVAL MOD(%s+%d,1800) DAY)" % (E, h)
    if dt in ('datetime', 'timestamp'):
        return "TIMESTAMPADD(SECOND, MOD(%s*37+%d,155000000), '2021-01-01 00:00:00')" % (E, h)
    if dt == 'time':
        return 'SEC_TO_TIME(MOD(%s+%d,86400))' % (E, h)
    if dt == 'year':
        return '(2020+MOD(%s,6))' % E
    if dt == 'enum':
        v = enum_values(col['ct'])
        if not v:
            return "''"
        return 'ELT(1+MOD(%s+%d,%d),%s)' % (E, h, len(v), ','.join(v))
    if dt == 'set':
        v = enum_values(col['ct'])
        return v[0] if v else "''"
    if dt in ('binary', 'varbinary'):
        return "LEFT(UNHEX(MD5(CONCAT(%d,'-',%s))),%d)" % (h, E, max(1, min(L, 16)))
    if dt in ('tinyblob', 'blob', 'mediumblob', 'longblob'):
        return "UNHEX(MD5(CONCAT(%d,'-',%s)))" % (h, E)
    if dt == 'uuid':
        return ("INSERT(INSERT(INSERT(INSERT(MD5(CONCAT(%d,'-',%s)),9,0,'-'),14,0,'-'),19,0,'-'),24,0,'-')"
                % (h, E))
    if dt == 'inet4':
        return 'INET_NTOA(167772160+%s)' % E
    return 'NULL' if col['nullable'] else "''"


def insertable_cols(m, t):
    return [c for c in m['cols'][t] if not c['generated']]


def seed_table_sql(m, t, lo, hi, N):
    cols = insertable_cols(m, t)
    pkset = set(m['pk'][t])
    uq = set(m['uniq'][t])
    referenced = {(fk['rt'], rc) for fk in m['fks'] for rc in fk['rcols']}
    fkcol = {}
    for fk in m['fks']:
        if fk['t'] != t:
            continue
        for i, c in enumerate(fk['cols']):
            if c not in fkcol:
                fkcol[c] = (fk, i)
    exprs = []
    E = 'n.n'
    for c in cols:
        name = c['name']
        if name in fkcol:
            fk, i = fkcol[name]
            rt, rc = fk['rt'], fk['rcols'][i]
            pcol = next((x for x in m['cols'].get(rt, []) if x['name'] == rc), None)
            if pcol is None:
                exprs.append('NULL' if c['nullable'] else val_expr(t, c, E))
                continue
            if rt == t:
                exprs.append('NULL' if c['nullable'] else val_expr(rt, pcol, E))
                continue
            np_ = max(1, N.get(rt, SMALL_ROWS))
            off = colhash(t, fk['name']) % np_
            idx = '(1+MOD(%s-1+%d,%d))' % (E, off, np_)
            exprs.append(val_expr(rt, pcol, idx))
            continue
        if 'auto_increment' in c['extra']:
            exprs.append(E)
            continue
        ex = val_expr(t, c, E)
        if (c['nullable'] and name not in pkset and name not in uq and (t, name) not in referenced
                and c['dt'] in STRING_TYPES):
            ex = 'IF(MOD(%s+%d,3)=0,NULL,%s)' % (E, colhash(t, name), ex)
        exprs.append(ex)
    return ('INSERT IGNORE INTO %s (%s) SELECT %s FROM g16_aux.nums n WHERE n.n BETWEEN %d AND %d;'
            % (q(t), ','.join(q(c['name']) for c in cols), ','.join(exprs), lo, hi))


def targets(plan, a):
    N = {t: SMALL_ROWS for t in plan['tables']}
    if a.calib:
        for t in plan['big']:
            N[t] = a.calib
        return N
    bpr = json.load(open(a.bpr, encoding='utf-8'))
    wsum = sum(WEIGHTS[t] for t in plan['big'])
    for t in plan['big']:
        b = bpr.get(t)
        if not b or b <= 0:
            continue
        N[t] = max(SMALL_ROWS, int(a.target_bytes * WEIGHTS[t] / wsum / b))
    return N


def cmd_seed(a):
    m = load_meta(a.meta)
    plan = json.load(open(a.plan, encoding='utf-8'))
    N = targets(plan, a)
    if a.targets_out:
        json.dump(N, open(a.targets_out, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    out = ["SET SESSION sql_mode='';", 'SET SESSION foreign_key_checks=1;', 'SET SESSION unique_checks=1;',
           'SET SESSION max_statement_time=0;']
    for t in plan['all_order']:
        n = N[t]
        lo = 1
        while lo <= n:
            hi = min(n, lo + CHUNK - 1)
            out.append(seed_table_sql(m, t, lo, hi, N))
            lo = hi + 1
    print('\n'.join(out))


def cmd_calib(a):
    sizes = {r[0]: int(r[1]) for r in read_tsv(a.sizes)}
    counts = {r[1]: int(r[2]) for r in read_tsv(a.counts) if r and r[0] == 'CNT'}
    bpr = {}
    for t, c in counts.items():
        if c > 0 and t in sizes:
            bpr[t] = sizes[t] / c
    json.dump(bpr, sys.stdout, ensure_ascii=False, indent=1)


def cmd_copy(a):
    m = load_meta(a.meta)
    plan = json.load(open(a.plan, encoding='utf-8'))
    out = []
    for t in plan['swap_order']:
        cl = ','.join(q(c['name']) for c in insertable_cols(m, t))
        out.append('DROP TABLE IF EXISTS %s;' % q(PREFIX + t))
        out.append('CREATE TABLE %s LIKE %s;' % (q(PREFIX + t), q(t)))
        out.append('INSERT INTO %s (%s) SELECT %s FROM %s;' % (q(PREFIX + t), cl, cl, q(t)))
    print('\n'.join(out))


def cmd_swap(a):
    """설계 §15-2 (가-2) · 작업지시서 K2: 세션 첫 줄 엄격 sql_mode · FOREIGN_KEY_CHECKS=1 ·
    자식 먼저 DELETE · 부모 먼저 INSERT … SELECT(칸 목록 명시) · 트랜잭션 안 DDL 0 · TRUNCATE 0."""
    m = load_meta(a.meta)
    plan = json.load(open(a.plan, encoding='utf-8'))
    order = plan['swap_order']
    out = ["SET SESSION sql_mode='STRICT_ALL_TABLES,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION';",
           'SET SESSION foreign_key_checks=1;', 'SET SESSION unique_checks=1;',
           "SELECT 'MARK','begin',DATE_FORMAT(NOW(6),'%Y-%m-%d %H:%i:%s.%f');",
           'START TRANSACTION;']
    for t in reversed(order):
        out.append('DELETE FROM %s;' % q(t))
    out.append("SELECT 'MARK','deleted',DATE_FORMAT(NOW(6),'%Y-%m-%d %H:%i:%s.%f');")
    for t in order:
        cl = ','.join(q(c['name']) for c in insertable_cols(m, t))
        out.append('INSERT INTO %s (%s) SELECT %s FROM %s;' % (q(t), cl, cl, q(PREFIX + t)))
    out.append("SELECT 'MARK','inserted',DATE_FORMAT(NOW(6),'%Y-%m-%d %H:%i:%s.%f');")
    out.append('COMMIT;')
    out.append("SELECT 'MARK','committed',DATE_FORMAT(NOW(6),'%Y-%m-%d %H:%i:%s.%f');")
    print('\n'.join(out))


def cmd_diverge(a):
    m = load_meta(a.meta)
    plan = json.load(open(a.plan, encoding='utf-8'))
    out = ['SET SESSION foreign_key_checks=1;']
    for t in plan['leaf_big']:
        pk = m['pk'].get(t) or [c['name'] for c in insertable_cols(m, t)][:1]
        key = 'CONCAT_WS(\'|\',%s)' % ','.join(q(c) for c in pk)
        out.append('DELETE FROM %s WHERE MOD(CRC32(%s),100)=%d;' % (q(PREFIX + t), key, a.slice))
    print('\n'.join(out))


def cmd_counts(a):
    plan = json.load(open(a.plan, encoding='utf-8'))
    parts = ["SELECT 'CNT','%s',COUNT(*) FROM %s" % (t, q(a.prefix + t)) for t in plan['swap_order']]
    print(' UNION ALL\n'.join(parts) + ';')


def cmd_checksum(a):
    plan = json.load(open(a.plan, encoding='utf-8'))
    print('CHECKSUM TABLE %s EXTENDED;' % ', '.join(q(t) for t in plan['swap_order']))


def cmd_analyze(a):
    plan = json.load(open(a.plan, encoding='utf-8'))
    ts = plan['tables'] if a.all else plan['swap_order']
    for i in range(0, len(ts), 40):
        print('ANALYZE TABLE %s;' % ', '.join(q(t) for t in ts[i:i + 40]))


# ───────────────────────────── 결과 ─────────────────────────────

def parse_marks(path):
    marks = {}
    if path and os.path.exists(path):
        for r in read_tsv(path):
            if len(r) >= 3 and r[0] == 'MARK':
                marks[r[1]] = r[2]
    return marks


def ts(s):
    from datetime import datetime
    return datetime.strptime(s, '%Y-%m-%d %H:%M:%S.%f')


def cmd_mkrec(a):
    rec = {}
    for kv in a.kv or []:
        k, _, v = kv.partition('=')
        try:
            rec[k] = json.loads(v)
        except ValueError:
            rec[k] = v
    for kv in a.jsonfile or []:
        k, _, p = kv.partition('=')
        try:
            rec[k] = json.load(open(p, encoding='utf-8'))
        except (OSError, ValueError) as ex:
            rec[k] = {'_읽기실패': str(ex)}
    marks = parse_marks(a.marks)
    rec['marks'] = marks
    try:
        if 'begin' in marks and 'deleted' in marks:
            rec['delete_sec'] = round((ts(marks['deleted']) - ts(marks['begin'])).total_seconds(), 3)
        if 'deleted' in marks and 'inserted' in marks:
            rec['insert_sec'] = round((ts(marks['inserted']) - ts(marks['deleted'])).total_seconds(), 3)
        if 'begin' in marks and 'committed' in marks:
            rec['txn_sec'] = round((ts(marks['committed']) - ts(marks['begin'])).total_seconds(), 3)
    except ValueError as ex:
        rec['marks_err'] = str(ex)
    if a.err and os.path.exists(a.err):
        txt = open(a.err, encoding='utf-8', errors='replace').read()
        rec['stderr_tail'] = txt[-600:]
        codes = sorted(set(w.split()[1] for w in txt.splitlines() if w.startswith('ERROR ') and len(w.split()) > 1))
        rec['error_codes'] = codes
        rec['err1206'] = any(c.startswith('1206') for c in codes)
    print(json.dumps(rec, ensure_ascii=False))


def mb(x):
    try:
        return '%.1f' % (float(x) / 1048576.0)
    except (TypeError, ValueError):
        return '-'


def cmd_report(a):
    recs = []
    if os.path.exists(a.results):
        for line in open(a.results, encoding='utf-8'):
            line = line.strip()
            if line:
                recs.append(json.loads(line))
    json.dump(recs, open(a.json_out, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    L = []
    L.append('# G16 교체 시간 실측 결과 (측정 장치 출력 · 판정 아님 · 문턱 X = PM)')
    L.append('')
    head = [r for r in recs if r.get('kind') == 'env']
    for r in head:
        L.append('- 환경: ' + ' · '.join('%s=%s' % (k, v) for k, v in r.items() if k != 'kind'))
    g0 = [r for r in recs if r.get('kind') == 'g0']
    for r in g0:
        L.append('- G0(제한 계정 CREATE DATABASE): 기대 1044 · 실제 `%s` → %s' % (r.get('got'), r.get('verdict')))
    L.append('')
    L.append('## 교체 트랜잭션 (가-2)')
    L.append('')
    L.append('| 표본(명목 MB) | 실제 교체 자료 MB | 교체 행 수 | 설정 | 결과 | 트랜잭션 초 | DELETE 초 | INSERT 초 | 벽시계 초 '
             '| undo 전→후 MB | ibdata1 전→후 | ibtmp1 후 | binlog 후 MB | 디스크 최대 증가 MB | purge 초 | 행 수 대조 |')
    L.append('|' + '---|' * 16)
    for r in recs:
        if r.get('kind') != 'swap':
            continue
        b, af = r.get('before') or {}, r.get('after') or {}
        res = r.get('result', '?')
        L.append('| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s→%s | %s→%s | %s | %s | %s | %s | %s |' % (
            r.get('sample_mb'), mb(r.get('swap_bytes')), r.get('swap_rows'), r.get('config'), res,
            r.get('txn_sec', '-'), r.get('delete_sec', '-'), r.get('insert_sec', '-'), r.get('wall_sec', '-'),
            mb(b.get('undo')), mb(af.get('undo')), mb(b.get('ibdata1')), mb(af.get('ibdata1')),
            mb(af.get('ibtmp1')), mb(af.get('binlog')), mb(r.get('peak_fs_delta')),
            r.get('purge_sec', '-'), r.get('count_check', '-')))
    skips = [r for r in recs if r.get('kind') == 'skip']
    if skips:
        L.append('')
        for r in skips:
            L.append('- 건너뜀: 표본 %s · %s — %s' % (r.get('sample_mb'), r.get('config'), r.get('why')))
    r11 = [r for r in recs if r.get('kind') == 'r11']
    L.append('')
    L.append('## R11 — 교체 도중 서버 강제 종료 → 재기동 → 되돌림')
    L.append('')
    if not r11:
        L.append('- 기록 없음')
    for r in r11:
        L.append('| 항목 | 값 |')
        L.append('|---|---|')
        for k in ('sample_mb', 'config', 'kill_after_sec', 'mark_at_kill', 'ready_sec', 'rollback_trx0_sec',
                  'rollback_log_sec', 'rollback_lockprobe_sec', 'probe_table', 'counts_vs_before', 'counts_vs_new', 'checksum_vs_before', 'verdict_hint',
                  'note'):
            L.append('| %s | %s |' % (k, r.get(k, '-')))
        af = r.get('after') or {}
        L.append('| 재기동 뒤 undo MB · ibdata1 MB | %s · %s |' % (mb(af.get('undo')), mb(af.get('ibdata1'))))
    L.append('')
    L.append('## PM 이 정할 칸')
    L.append('')
    L.append('- 문턱 X(분): ____ (결과 표를 보고 PM 이 적음 · 이 장치는 숫자를 정하지 않는다)')
    open(a.md_out, 'w', encoding='utf-8').write('\n'.join(L) + '\n')


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    p = argparse.ArgumentParser()
    sp = p.add_subparsers(dest='cmd', required=True)
    for name in ('plan', 'seed', 'copy', 'swap', 'diverge', 'counts', 'checksum', 'analyze'):
        s = sp.add_parser(name)
        s.add_argument('--meta')
        s.add_argument('--plan')
        if name == 'seed':
            s.add_argument('--calib', type=int, default=0)
            s.add_argument('--target-bytes', type=int, default=0)
            s.add_argument('--bpr')
            s.add_argument('--targets-out')
        if name == 'diverge':
            s.add_argument('--slice', type=int, default=0)
        if name == 'counts':
            s.add_argument('--prefix', default='')
        if name == 'analyze':
            s.add_argument('--all', action='store_true')
    s = sp.add_parser('calib')
    s.add_argument('--sizes')
    s.add_argument('--counts')
    s = sp.add_parser('mkrec')
    s.add_argument('--kv', action='append')
    s.add_argument('--jsonfile', action='append')
    s.add_argument('--marks')
    s.add_argument('--err')
    s = sp.add_parser('report')
    s.add_argument('--results')
    s.add_argument('--md-out')
    s.add_argument('--json-out')
    a = p.parse_args()
    {'plan': cmd_plan, 'seed': cmd_seed, 'calib': cmd_calib, 'copy': cmd_copy, 'swap': cmd_swap,
     'diverge': cmd_diverge, 'counts': cmd_counts, 'checksum': cmd_checksum, 'analyze': cmd_analyze,
     'mkrec': cmd_mkrec, 'report': cmd_report}[a.cmd](a)


if __name__ == '__main__':
    main()
