# -*- coding: utf-8 -*-
"""X733c 비교 잡 — 칸 증거를 기계로 대조한다 (작업지시서 20261030작 §3-6 · PM 결재 조건 ①~④ · PM 추가 지시 ①~⑥).

계측 가지 전용 · 머지 금지.

쓰임:  python3 -I tests/measure/x733c/compare_cells.py <칸 증거 폴더> <결과 폴더>
       <칸 증거 폴더> 아래에 칸마다 하위 폴더 하나(actions/download-artifact 기본 모양).

종료 코드:  0 = 같음 · 1 = 다름 · 2 = 무효
판정 글자:  「같음(비교 항목 N개)」 · 「다름」 · 「무효 — 전제/장치/가설 불일치 · 원인 미확정」
            — 「호환」「안전」「정상 적용」 같은 해석 낱말은 쓰지 않는다. 해석은 HQA·CTO 몫이다.

🔴 무효·다름·미측정을 같음(0)으로 바꾸는 길은 없다:
   - 전제 하나라도 어긋나면 비교 결과와 상관없이 무효(2).
   - 비교 항목 값이 하나라도 없으면(비교불가) 무효(2).
   - 비교 항목이 0개면 무효(2).
   - 이 스크립트가 예외로 죽으면 무효(2) 로 끝난다(다름 1 로도, 같음 0 으로도 안 간다).
   - (검증 지적 반영) 칸 잡 결론이 success 가 아니거나 env 가 없으면 무효(2) · 대조군 ② 는 정확한 값
     (2.6.2×LF = a=7,b=NULL + 서버 수신 둘째 줄 @P · 나머지 = a=7,b=7 · 오류 0) 밖이면 무효(2) ·
     칸마다 바닥값(파일 수 · 회차별 서버 수신 · 확장 모양 줄 수 · 백오피스 적용/수신) 아래면 무효(2) ·
     줄끝 CR 수는 칸 산출물에서 직접 다시 센다.
"""
import glob
import json
import os
import sys
import traceback
import xml.etree.ElementTree as ET

# 「값 없음」 표지 — None 은 정당한 값(예: 백오피스 오류 없음)이라 따로 둔다.
MISSING = object()

VERDICT_SAME = "같음"
VERDICT_DIFF = "다름"
VERDICT_INVALID = "무효 — 전제/장치/가설 불일치 · 원인 미확정"

MARIADB_CELLS = [("2.5.0", "lf"), ("2.5.0", "crlf"), ("2.6.2", "lf"), ("2.6.2", "crlf")]
MYSQL8_CELLS = [("2.5.0", "lf"), ("2.6.2", "lf")]
CONTROL2_DIFF_CELL = ("2.6.2", "lf")
CONTROL2_SAME_OUTCOME = "a=7,b=7"
CONTROL2_DIFF_OUTCOME = "a=7,b=NULL"  # [4] §5 오프라인 실측: 2.6.2 LF 는 둘째 `@P` 를 치환하지 않는다 → 서버에서 NULL
# 칸 잡 결론 — 워크플로가 env 로 넘긴다([3-V] R1). success 가 아니면 무효(2).
JOB_RESULT_ENVS = ["X733C_NEEDS_MEASURE", "X733C_NEEDS_MEASURE_BO_MYSQL8"]

EXPECTED_CSPROJ = sorted([
    "src/HitPan.API/HitPan.API.csproj",
    "src/HitPan.Application/HitPan.Application.csproj",
    "src/HitPan.Backoffice.API/HitPan.Backoffice.API.csproj",
    "src/HitPan.Infrastructure/HitPan.Infrastructure.csproj",
    "src/HitPan.IntegrationTests/HitPan.IntegrationTests.csproj",
    "tools/MdbMigrationBaselineRun/MdbMigrationBaselineRun.csproj",
    "tools/MigrationIdempotencyCheck/MigrationIdempotencyCheck.csproj",
    "tools/MigrationSmokeTest/MigrationSmokeTest.csproj",
])
MEASURE_GRAPH = [
    "src/HitPan.Application", "src/HitPan.Infrastructure", "src/HitPan.API",
    "src/HitPan.Backoffice.API", "src/HitPan.Tests",
]
EXPECTED_FILE_COUNT = {"erp": 113, "backoffice": 16, "control": 2}
SERVER_FACT_KEYS = [
    "VERSION()", "@@version_comment", "@@sql_mode", "@@character_set_server", "@@collation_server",
    "@@max_connections", "@@general_log", "@@log_output", "@@lower_case_table_names", "@@time_zone",
]
REQUIRED_FILES = [
    "cell_id.txt", "head_sha.txt", "tree_assert.txt", "sqllist.txt", "ddl_sha256.txt",
    "csproj_numstat.txt", "eol.json", "image.txt", "dotnet_version.txt", "runner_image.txt",
    "restore.log", "assets.txt", "nuget_audit.txt", "anchor.trx", "probe.trx", "probe.json",
]

HEADER_NOTES = [
    "🔴 합성 대조군 ② 생존 = 장치가 「`--` 바로 뒤 줄바꿈 다음 줄의 매개변수」라는 **그 특정 차이**를 잡는다는 증거일 뿐이다. "
    "실제 SQL 집합 전체에서 차이가 없다는(누락 없음) 증명이 아니다.",
    "🔴 「같음(비교 항목 N개)」 = 아래 표의 N개 항목이 칸 사이 같다는 뜻뿐이다. 「정상 적용」「호환」「안전」 판정이 아니다. "
    "같은 거부 · 같은 적용 수는 「같다」 일 뿐 「정상」 이 아니다.",
    "🔴 항목마다 「보장하는 범위 / 못 보는 범위」 를 함께 읽는다. 못 보는 범위는 이 런이 재지 않은 것이다.",
    "🔴 무효여도 네 칸 원결과 · 실제 로드된 조립품 판 · 줄끝 증거는 칸 산출물(x733c-cell-*)에 그대로 남는다.",
]

DB16_EXISTING_LIMIT = (
    "실제 설치/업데이트 경로의 기존 한계 (판정 안 함 · 이 런이 재는 것 아님): DB-16 은 `DELIMITER` 를 쓰는 유일한 마이그이고, "
    "제품 적용기(MigrationRunner → MySqlConnector)로는 판과 무관하게 돌지 않는다(G-W13-5 명단 `KnownNotReapplyable` · "
    "정적 회신 §1-3). 표의 「대조군 ① DB-16 원본 거부 대조」 는 이 한계를 재는 칸이 아니라, 그 거부 모양이 판·줄끝 사이에서 "
    "바뀌는지만 본다."
)


# ──────────────────────────────────────────────────────────────
# 읽기
# ──────────────────────────────────────────────────────────────

def read_text(path):
    with open(path, encoding="utf-8", errors="replace") as fh:
        return fh.read()


def read_json(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def canon(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True)


def parse_trx(path):
    root = ET.parse(path).getroot()
    counters = None
    outcomes = []
    for el in root.iter():
        tag = el.tag.split("}")[-1]
        if tag == "Counters":
            counters = {k: int(v) for k, v in el.attrib.items() if v.isdigit()}
        elif tag == "UnitTestResult":
            outcomes.append({"name": el.attrib.get("testName"), "outcome": el.attrib.get("outcome")})
    return {"counters": counters or {}, "results": outcomes}


def load_cell(folder):
    cell = {"folder": os.path.basename(folder), "missing": [], "files": {}}
    for name in REQUIRED_FILES:
        path = os.path.join(folder, name)
        if not os.path.exists(path):
            cell["missing"].append(name)
    cell["scrubbed"] = os.path.exists(os.path.join(folder, "secret_scrubbed.txt"))

    def opt(name, loader):
        path = os.path.join(folder, name)
        if not os.path.exists(path):
            return None
        try:
            return loader(path)
        except (OSError, ValueError, ET.ParseError) as exc:
            cell["missing"].append(f"{name} (읽기 실패: {exc})")
            return None

    cell_id = (opt("cell_id.txt", read_text) or "").strip()
    cell["id"] = cell_id
    parts = cell_id.split("-")
    cell["kind"], cell["driver"], cell["eol"] = (parts + ["", "", ""])[:3] if len(parts) >= 3 else ("", "", "")
    cell["head_sha"] = (opt("head_sha.txt", read_text) or "").strip()
    cell["tree_assert"] = (opt("tree_assert.txt", read_text) or "").strip()
    cell["sqllist"] = opt("sqllist.txt", read_text)
    cell["ddl_sha256"] = (opt("ddl_sha256.txt", read_text) or "").strip()
    cell["numstat"] = opt("csproj_numstat.txt", read_text)
    cell["eol_json"] = opt("eol.json", read_json)
    cell["image"] = parse_kv(opt("image.txt", read_text))
    cell["dotnet_version"] = (opt("dotnet_version.txt", read_text) or "").strip()
    cell["runner_image"] = (opt("runner_image.txt", read_text) or "").strip()
    cell["assets"] = opt("assets.txt", read_text)
    cell["nuget_audit"] = parse_kv(opt("nuget_audit.txt", read_text))
    cell["anchor"] = opt("anchor.trx", parse_trx)
    cell["probe_trx"] = opt("probe.trx", parse_trx)
    cell["probe"] = opt("probe.json", read_json)
    return cell


def parse_kv(text):
    out = {}
    for line in (text or "").splitlines():
        if "=" in line:
            k, v = line.split("=", 1)
            out[k.strip()] = v.strip()
    return out


def dig(obj, *keys):
    for k in keys:
        if obj is None:
            return None
        if isinstance(obj, dict):
            obj = obj.get(k)
        else:
            return None
    return obj


# ──────────────────────────────────────────────────────────────
# 칸 전제 (하나라도 어긋나면 무효)
# ──────────────────────────────────────────────────────────────

def cell_prereq(cell):
    """칸 하나의 전제 점검 — [(점검, 통과?, 상세)]."""
    checks = []

    def add(name, ok, detail=""):
        checks.append({"check": name, "ok": bool(ok), "detail": detail})

    add("필수 증거 파일", not cell["missing"], ", ".join(cell["missing"]))
    add("산출물에 비번 글자 없음", not cell["scrubbed"], "secret_scrubbed.txt 있음" if cell["scrubbed"] else "")
    add("제품 나무 = 바닥 6b15caf9", cell["tree_assert"] == "ok", cell["tree_assert"])

    driver, eol, kind = cell["driver"], cell["eol"], cell["kind"]

    # 판 증거 ① — 커밋 안 된 csproj 글자 바꾸기 = 정확히 8파일 8줄
    lines = [ln for ln in (cell["numstat"] or "").splitlines() if ln.strip()]
    if driver == "2.5.0":
        rows = [ln.split("\t") for ln in lines]
        ok = (len(rows) == 8 and all(len(r) == 3 and r[0] == "1" and r[1] == "1" for r in rows)
              and sorted(r[2] for r in rows) == EXPECTED_CSPROJ)
        add("판 증거 ① csproj 바뀐 줄 8파일 8줄", ok, f"{len(rows)}줄")
    else:
        add("판 증거 ① csproj 바뀐 줄 0", len(lines) == 0, f"{len(lines)}줄")

    # 판 증거 ② — project.assets.json 측정 그래프 5개
    want = f"MySqlConnector/{driver}"
    got = {}
    for ln in (cell["assets"] or "").splitlines():
        if " " in ln:
            p, k = ln.split(" ", 1)
            got[p.strip()] = k.strip()
    bad = [f"{p}={got.get(p)}" for p in MEASURE_GRAPH if got.get(p) != want]
    add("판 증거 ② project.assets.json 5개", not bad, "; ".join(bad))

    # 판 증거 ③ — 실제 올라온 조립품
    info = dig(cell["probe"], "runtime", "informational_version") or ""
    add("판 증거 ③ 실제 로드된 조립품 판", info.split("+")[0] == driver, info)

    # 판 증거 ④ — NU1902 수
    try:
        n1902 = int(cell["nuget_audit"].get("nu1902", ""))
    except ValueError:
        n1902 = None
    if driver == "2.5.0":
        add("판 증거 ④ NU1902 ≥ 1", n1902 is not None and n1902 >= 1, f"nu1902={n1902} · nu1900={cell['nuget_audit'].get('nu1900')}")
    else:
        add("판 증거 ④ NU1902 = 0", n1902 == 0, f"nu1902={n1902} · nu1900={cell['nuget_audit'].get('nu1900')}")

    # 줄끝
    ej = cell["eol_json"] or {}
    counts = ej.get("file_count", {})
    want_counts = dict(EXPECTED_FILE_COUNT)
    add("줄끝 단언(변환 직후)", ej.get("mode") == eol and ej.get("assert_ok") is True,
        f"mode={ej.get('mode')} · 실패 {len(ej.get('assert_failures') or [])}")
    add("SQL 집합 수 113 · 16 · 2", counts == want_counts, canon(counts))
    ddl = ej.get("untouched_shipping_ddl") or {}
    add("출하 DDL 은 줄끝 안 바뀜", ddl.get("sha256_before") and ddl.get("sha256_before") == ddl.get("sha256_after"), "")

    # 실행기가 읽는 폴더 재계수 = 변환 결과
    by_group = {}
    for rec in ej.get("files") or []:
        by_group.setdefault(rec["group"], {})[rec["name"]] = rec["after"]
    pairs = [("dir_recount_backoffice", "backoffice")]
    if kind == "mariadb":
        pairs.insert(0, ("dir_recount_runner", "erp"))
    for key, group in pairs:
        recount = dig(cell["probe"], key)
        if not isinstance(recount, list):
            add(f"실행기 폴더 재계수 ({group})", False, "기록 없음")
            continue
        mism = []
        names = {r["name"] for r in recount}
        if names != set(by_group.get(group, {}).keys()):
            mism.append("파일 목록 다름")
        for r in recount:
            a = by_group.get(group, {}).get(r["name"])
            if a is None or any(r.get(k) != a.get(k) for k in ("bytes", "lf", "cr", "crlf", "sha256")):
                mism.append(r["name"])
        add(f"실행기 폴더 재계수 ({group}) = 변환 결과", not mism, ", ".join(mism[:10]))

    # 줄끝 직접 재계수 — [4] 권고 1: assert_ok 깃발만 믿지 않고 칸 산출물의 CR·LF·CRLF 수를 다시 본다.
    #   lf 칸: 모든 파일 CR 0 · crlf 칸: 모든 파일 CR = LF = CRLF · 두 칸 모두 LF 합계 > 0
    eol_sources = [("eol.json after", [r.get("after") or {} for r in (ej.get("files") or [])])]
    for key, group in pairs:
        recount = dig(cell["probe"], key)
        eol_sources.append((f"실행기 폴더({group})", recount if isinstance(recount, list) else []))
    for label, recs in eol_sources:
        add(f"줄끝 직접 재계수 — {label}", *eol_recount_ok(eol, recs))

    # 닻(무수정 기존 시험)
    anchor = cell["anchor"] or {}
    c = anchor.get("counters", {})
    ok = (c.get("total", 0) >= 1 and c.get("passed", -1) == c.get("total", 0)
          and c.get("failed", 0) == 0 and c.get("notExecuted", 0) == 0)
    name = "닻 G-W13-5 무수정 초록" if kind == "mariadb" else "닻 BackofficeSchemaMysql8Gate 무수정 초록"
    add(name, ok, canon(c))

    # 측정 시험 자체
    pc = (cell["probe_trx"] or {}).get("counters", {})
    add("측정 시험 장치 단언 통과", pc.get("total", 0) == 1 and pc.get("passed", 0) == 1, canon(pc))
    probe = cell["probe"] or {}
    add("측정 시험 장치 오류 0", not probe.get("device_errors") and "aborted" not in probe,
        canon(probe.get("device_errors")) + (" · 중단" if "aborted" in probe else ""))

    if kind == "mariadb":
        erp = probe.get("erp") or {}
        measured = erp.get("measured", -1)
        add("G-W13-5 방식 측정 수 ≥ 100 · = 파일 수", measured >= 100 and measured == erp.get("all_count"),
            f"{measured}/{erp.get('all_count')}")
        add("측정 시험 깨짐 집합 = G-W13-5 명단",
            sorted(erp.get("broken_ids") or []) == sorted(erp.get("known_not_reapplyable") or ["<없음>"]),
            canon(sorted(erp.get("broken_ids") or [])))

    # 바닥값 단언 — [4] 필수 2: 네 칸이 똑같이 비어 「같음(0)」 이 나는 길을 막는다.
    for name, ok, detail in floor_checks(kind, probe):
        add(name, ok, detail)

    add("DB 이미지 다이제스트 고정", cell["image"].get("pinned") == "true",
        "다이제스트 미고정 = 장치 런" if cell["image"].get("pinned") != "true" else cell["image"].get("ref", ""))
    return checks


def eol_recount_ok(eol, recs):
    """(통과?, 상세) — 칸 산출물의 줄끝 수를 직접 본다."""
    if not recs:
        return False, "기록 없음"
    bad, lf_total = [], 0
    for r in recs:
        try:
            cr, lf, crlf = int(r["cr"]), int(r["lf"]), int(r["crlf"])
        except (KeyError, TypeError, ValueError):
            bad.append(f"{r.get('name', '?') if isinstance(r, dict) else '?'}(수 없음)")
            continue
        lf_total += lf
        if eol == "lf" and cr != 0:
            bad.append(f"{r.get('name', '?')}(CR {cr})")
        elif eol == "crlf" and not (cr == lf == crlf):
            bad.append(f"{r.get('name', '?')}(CR {cr} · LF {lf} · CRLF {crlf})")
        elif eol not in ("lf", "crlf"):
            bad.append(f"칸 줄끝 {eol!r}")
            break
    if lf_total <= 0:
        bad.append("LF 합계 0")
    return not bad, f"{len(recs)}파일 · 어긋남 {len(bad)}: " + ", ".join(bad[:8])


def floor_checks(kind, probe):
    """바닥값 — [(점검, 통과?, 상세)]. 값이 없거나 바닥 아래면 그 칸 무효."""
    out = []

    def shape_ok(key):
        shape = probe.get(key)
        lines = shape.get("lines") if isinstance(shape, dict) else None
        n = len(lines) if isinstance(lines, list) else -1
        cnt = shape.get("count") if isinstance(shape, dict) else None
        return n > 0 and cnt == n, f"lines={n} · count={cnt}"

    if kind == "mariadb":
        erp = probe.get("erp") if isinstance(probe.get("erp"), dict) else {}
        files = erp.get("files") if isinstance(erp.get("files"), list) else None
        nfiles = len(files) if files is not None else -1
        out.append(("바닥 ① ERP len(files) = all_count = measured",
                    nfiles >= 1 and nfiles == erp.get("all_count") == erp.get("measured"),
                    f"files={nfiles} · all_count={erp.get('all_count')} · measured={erp.get('measured')}"))
        broken = set(erp.get("broken_ids") or [])
        empty = []
        for f in files or []:
            passes = f.get("passes") if isinstance(f, dict) else None
            if not isinstance(passes, list) or not passes:
                empty.append(f"{dig(f, 'id')}(회차 0)")
                continue
            if f.get("id") in broken:
                continue  # 깨진 파일의 실패 회차는 드라이버가 서버에 보내기 전에 멈출 수 있다 — 바닥 대상 아님
            for x in passes:
                lq = x.get("log_queries") if isinstance(x, dict) else None
                if not isinstance(lq, list) or len(lq) < 1:
                    empty.append(f"{f.get('id')}#{dig(x, 'pass')}")
        out.append(("바닥 ② ERP 깨지지 않은 파일 회차마다 서버 수신 ≥ 1",
                    files is not None and not empty, f"빈 회차 {len(empty)}: " + ", ".join(empty[:8])))
        ok, detail = shape_ok("erp_shape")
        out.append(("바닥 ③ ERP 확장 모양 줄 수 > 0", ok, detail))

    bo = probe.get("backoffice") if isinstance(probe.get("backoffice"), dict) else {}
    applied = bo.get("applied_count")
    lq = bo.get("log_queries")
    nlq = len(lq) if isinstance(lq, list) else -1
    out.append(("바닥 ④ 백오피스 적용 ≥ 1 · 서버 수신 ≥ 1",
                type(applied) is int and applied >= 1 and nlq >= 1, f"applied={applied} · 수신={nlq}"))
    ok, detail = shape_ok("backoffice_shape")
    out.append(("바닥 ⑤ 백오피스 확장 모양 줄 수 > 0", ok, detail))
    return out


def job_result_reasons(job_results):
    """칸 잡 결론 — 하나라도 success 가 아니거나 없으면 무효 사유."""
    reasons = []
    for name in JOB_RESULT_ENVS:
        val = (job_results or {}).get(name)
        if val != "success":
            reasons.append(f"칸 잡 결론 {name}={val!r} (success 아님/없음) — {VERDICT_INVALID}")
    return reasons


# ──────────────────────────────────────────────────────────────
# 비교 항목
# ──────────────────────────────────────────────────────────────

def diff_detail(ref, other):
    if isinstance(ref, dict) and isinstance(other, dict):
        keys = sorted(set(ref) | set(other))
        bad = [k for k in keys if canon(ref.get(k)) != canon(other.get(k))]
        return f"다른 키 {len(bad)}개: " + ", ".join(map(str, bad[:15]))
    if isinstance(ref, list) and isinstance(other, list):
        try:
            a, b = set(map(canon, ref)), set(map(canon, other))
            only_r, only_o = sorted(a - b)[:8], sorted(b - a)[:8]
            if only_r or only_o:
                return f"기준에만 {len(a - b)} · 이 칸에만 {len(b - a)} · 예: -{only_r} +{only_o}"
        except TypeError:
            pass
        for i, (x, y) in enumerate(zip(ref, other)):
            if canon(x) != canon(y):
                return f"{i}번째부터 다름 (길이 {len(ref)}/{len(other)})"
        return f"길이 다름 {len(ref)}/{len(other)}"
    return f"{canon(ref)[:120]} ≠ {canon(other)[:120]}"


def make_item(item_id, title, axis, guarantee, blind, values):
    """values = {칸 id: 값 또는 None}. None 이 하나라도 있으면 「비교불가」(⇒ 무효)."""
    item = {"id": item_id, "title": title, "axis": axis, "guarantee": guarantee, "blind": blind,
            "cells": sorted(values.keys())}
    if not values or any(v is MISSING for v in values.values()):
        item["result"] = "비교불가"
        item["detail"] = "값 없음: " + ", ".join(k for k, v in values.items() if v is MISSING)
        return item
    ids = sorted(values.keys())
    ref = values[ids[0]]
    diffs = [f"{cid}: {diff_detail(ref, values[cid])}" for cid in ids[1:] if canon(values[cid]) != canon(ref)]
    item["result"] = VERDICT_SAME if not diffs else VERDICT_DIFF
    item["detail"] = f"기준 {ids[0]}" + ("" if not diffs else " · " + " | ".join(diffs))
    return item


def erp_items(cells):
    """MariaDB 네 칸 — 전체 비교 + 판 축 비교."""
    items = []

    def per(fn):
        out = {}
        for cid, cell in cells.items():
            try:
                out[cid] = fn(cell["probe"])
            except (KeyError, TypeError, AttributeError):
                out[cid] = MISSING
        return out

    G_RUN = "113 마이그를 출하 DDL 위에서 G-W13-5 순서(번호순 · 한 건씩 · 두 바퀴)로 실물 MigrationRunner 에 태운 결과가 칸 사이 같음"
    B_RUN = "성공 = 예외 없음일 뿐 「정상 적용」 아님 · 번호순 한 순서만(역순·다른 순서 미측정) · 업무 SQL(C# 안 문장) 미포함 · 윈도우 고객 PC·실제 설치판 미포함"

    items.append(make_item("E1", "ERP 깨짐 집합", "4칸 전체", G_RUN, B_RUN,
                           per(lambda p: sorted(p["erp"]["broken_ids"]))))
    items.append(make_item("E2", "ERP 깨짐 사유(회차 · 오류 번호 · 예외 형 · 메시지 첫 줄)", "4칸 전체",
                           "깨진 파일마다 러너가 보고한 사유 모양이 칸 사이 같음",
                           "메시지 첫 줄만 봄(둘째 줄 이하 미비교) · 사유가 같아도 원인이 같다는 증명 아님",
                           per(lambda p: {b["id"]: {k: b["detail"].get(k) for k in
                                                    ("pass", "err_no", "sqlstate", "ex_type", "ex_msg1", "runner_msg1", "failed_id")}
                                          for b in p["erp"]["broken"]})))
    items.append(make_item("E3", "ERP 적용 건수", "4칸 전체",
                           "러너가 「적용」 으로 보고한 건수가 칸 사이 같음",
                           "건수만 봄 — 적용된 내용의 의미 동일성은 E4·E5·B1 로만 일부 봄", per(lambda p: p["erp"]["applied"])))
    items.append(make_item("E4", "ERP 파일별 회차 결과(성공 · 적용 · 실패 위치)", "4칸 전체",
                           "113 파일 각각의 1·2회차 성공 여부와 실패 파일 위치가 칸 사이 같음",
                           "결과 표지만 봄(실행 시간·경고·영향 행 수 미비교)",
                           per(lambda p: {f["id"]: [[x["pass"], x["ok"], x["applied"], x["failed_id"]] for x in f["passes"]]
                                          for f in p["erp"]["files"]})))
    items.append(make_item("E5", "ERP 확장 모양(표 · 칸 형 · 널 · DEFAULT · COMMENT · 인덱스)", "4칸 전체",
                           "information_schema 의 표·칸(형·널·기본값·설명)·인덱스(이름·고유·칸 순서) 집합이 칸 사이 같음 (schema_migrations 제외)",
                           "트리거·루틴·뷰·이벤트·FK 조건·CHECK·데이터 행·표 엔진/정렬 규칙·AUTO_INCREMENT 값 미비교",
                           per(lambda p: p["erp_shape"]["lines"])))
    items.append(make_item("E6", "대조군 ① DB-16 원본 거부 대조", "4칸 전체",
                           "원본 DB-16(줄끝 축만 바뀜)을 한 건만 실물 러너에 태웠을 때 거부 모양(성공 여부·적용 0·예외 형·메시지 첫 줄)이 칸 사이 같음",
                           "실제 설치/업데이트 경로의 기존 한계(DELIMITER 를 제품 적용기가 못 돌림)는 이 칸이 재지 않는다 — 아래 별도 칸",
                           per(lambda p: {k: p["control1_db16"]["result"].get(k) for k in
                                          ("ok", "applied", "applied_count", "failed_id", "ex_type", "err_no", "sqlstate", "ex_msg1", "runner_msg1")})))
    items.extend(backoffice_items(cells, prefix="E", axis="4칸 전체"))

    # 판 축만 — 같은 줄끝끼리 2.5.0 ↔ 2.6.2
    for eol in ("lf", "crlf"):
        pair = {cid: c for cid, c in cells.items() if c["eol"] == eol}
        if len(pair) != 2:
            continue
        sub = lambda fn: {cid: v for cid, v in per(fn).items() if cid in pair}  # noqa: E731
        axis = f"판 축 · {eol}"
        items.append(make_item(f"B1-{eol}", "ERP 서버 수신 문장 바이트(general_log · 파일 · 회차별 sha256 열)", axis,
                               "실물 러너 연결이 서버에 보낸 COM_QUERY 를 general_log 가 저장한 바이트가 판 사이 같음 — 드라이버가 문장을 바꿨다면 여기서 드러남",
                               "general_log argument 칸 문자셋 변환(칸 기록 server.general_log_argument 참조 · utf8mb3 등이면 4바이트 글자는 저장 전에 바뀜) · "
                               "준비문(prepared) 경로 · 매개변수 있는 업무 문장(대조군 ② 한 줄만) · 줄끝이 다른 칸끼리는 비교 안 함",
                               sub(lambda p: {f["id"]: [[q["sha256"] for q in x["log_queries"]] for x in f["passes"]]
                                              for f in p["erp"]["files"]})))
        items.append(make_item(f"B2-{eol}", "ERP 러너 체크섬(schema_migrations.checksum)", axis,
                               "러너가 읽은 문자열의 sha256(줄끝 포함)이 판 사이 같음",
                               "줄끝이 다른 칸끼리는 당연히 다름 — 비교 안 함",
                               sub(lambda p: {f["id"]: f["checksum"] for f in p["erp"]["files"]})))
        items.append(make_item(f"B3-{eol}", "SQL 파일 sha256(변환 뒤)", axis,
                               "실행기에 들어간 파일 바이트가 판 사이 같음", "줄끝이 다른 칸끼리는 비교 안 함",
                               {cid: ({r["path"]: r["after"]["sha256"] for r in c["eol_json"]["files"]} if c["eol_json"] else MISSING)
                                for cid, c in pair.items()}))
        items.extend(backoffice_axis_items(pair, eol))
    return items


def backoffice_items(cells, prefix, axis):
    def per(fn):
        out = {}
        for cid, cell in cells.items():
            try:
                out[cid] = fn(cell["probe"])
            except (KeyError, TypeError, AttributeError):
                out[cid] = MISSING
        return out

    blind = ("`${VAR}` 치환 파일은 칸에 환경 변수가 없어 건너뜀(운영 동작 미측정) · 운영 MySQL 8 설정·연결 옵션 미포함 · "
             "MariaDB 칸은 통째 실행 경로만, MySQL 8 칸은 자체 분리 경로만 돈다")
    return [
        make_item(f"{prefix}7", "백오피스 적용 파일 수 · _schema_migrations 파일명 집합", axis,
                  "실물 SchemaMigrator.ApplyAsync(빈 DB · installer/backoffice)가 남긴 이력 파일명·건수가 칸 사이 같음", blind,
                  per(lambda p: {"count": p["backoffice"]["applied_count"], "names": p["backoffice"]["file_names"]})),
        make_item(f"{prefix}8", "백오피스 적용 오류", axis,
                  "적용기가 던진 예외 유무·모양이 칸 사이 같음", "메시지 첫 줄만 봄 · " + blind,
                  per(lambda p: p["backoffice"]["error"])),
        make_item(f"{prefix}9", "백오피스 확장 모양", axis,
                  "백오피스 DB 의 표·칸(형·널·기본값·설명)·인덱스 집합이 칸 사이 같음 (_schema_migrations 제외)",
                  "트리거·루틴·뷰·FK 조건·데이터 행 미비교 · " + blind,
                  per(lambda p: p["backoffice_shape"]["lines"])),
    ]


def backoffice_axis_items(pair, eol):
    def sub(fn):
        out = {}
        for cid, cell in pair.items():
            try:
                out[cid] = fn(cell["probe"])
            except (KeyError, TypeError, AttributeError):
                out[cid] = MISSING
        return out

    axis = f"판 축 · {eol}"
    return [
        make_item(f"BB1-{eol}", "백오피스 content_hash", axis,
                  "백오피스 적용기가 읽은 원본 문자열 sha256 이 판 사이 같음", "줄끝이 다른 칸끼리는 비교 안 함",
                  sub(lambda p: p["backoffice"]["content_hash"])),
        make_item(f"BB2-{eol}", "백오피스 서버 수신 문장 바이트(적용 전체의 문장 순서 sha256 열)", axis,
                  "SchemaMigrator 연결이 서버에 보낸 문장을 general_log 가 저장한 바이트 열이 판 사이 같음",
                  "파일별로 끊지 않은 전체 순서 · general_log 문자셋 변환 · 줄끝이 다른 칸끼리는 비교 안 함",
                  sub(lambda p: [q["sha256"] for q in p["backoffice"]["log_queries"]])),
    ]


# ──────────────────────────────────────────────────────────────
# 묶음 전제 — 칸 사이 같아야 하는 것 · 대조군 ② 생존
# ──────────────────────────────────────────────────────────────

def group_prereq(group_name, cells, expected):
    reasons = []
    present = {(c["driver"], c["eol"]) for c in cells.values()}
    missing = [f"{d}×{e}" for d, e in expected if (d, e) not in present]
    if missing:
        reasons.append(f"{group_name}: 칸 없음 {missing}")

    def same(label, fn):
        vals = {}
        for cid, c in cells.items():
            try:
                vals[cid] = canon(fn(c))
            except (KeyError, TypeError, AttributeError):
                vals[cid] = None
        if None in vals.values() or len(set(vals.values())) > 1:
            reasons.append(f"{group_name}: 칸 사이 {label} 다름/없음 — {vals}")

    same("SQL 목록(git ls-tree) 해시", lambda c: c["sqllist"])
    same("출하 DDL sha256", lambda c: c["ddl_sha256"] or None)
    same("DB 이미지(다이제스트 · 이미지 ID)", lambda c: [c["image"].get("repo_digests"), c["image"].get("image_id")])
    same("dotnet --version", lambda c: c["dotnet_version"] or None)
    same("러너 이미지", lambda c: c["runner_image"] or None)
    same("DB 서버 전제(판 · sql_mode · 문자셋 · 정렬 · max_connections · general_log)",
         lambda c: {k: c["probe"]["server"]["vars"].get(k) for k in SERVER_FACT_KEYS})

    # 대조군 ② 생존 — [4] 필수 1: 정확한 값으로 묶는다.
    #   2.6.2 × LF 칸 = 정확히 「a=7,b=NULL」 · 오류 0 · 서버 수신 문장 둘째 줄에 `@P` 가 그대로(첫 줄엔 없음)
    #   나머지 칸     = 정확히 「a=7,b=7」 · 오류 0
    #   그 밖(오류 · a=NULL,b=7 · 서버 수신 없음 등)은 전부 무효(2).
    outcomes, verdicts = {}, {}
    for cid, c in cells.items():
        key = (c["driver"], c["eol"])
        outcomes[key] = dig(c["probe"], "control2_param", "outcome")
        verdicts[key] = control2_cell_ok(key, dig(c["probe"], "control2_param"))
    table = {f"{d}×{e}": outcomes.get((d, e)) for d, e in expected}
    if any(v is None for v in table.values()):
        reasons.append(f"{group_name}: 대조군 ② 결과 없음 — {table} · {VERDICT_INVALID}")
    else:
        bad = {f"{d}×{e}": verdicts[(d, e)][1] for d, e in expected if not verdicts[(d, e)][0]}
        if bad:
            pattern = "모든 칸 같음" if len(set(table.values())) == 1 else "기대와 다른 칸이 있음"
            reasons.append(f"{group_name}: 대조군 ② 기대 불일치({pattern}) — {table} · 어긋난 칸 {bad} · "
                           f"{VERDICT_INVALID}")
    return reasons, table


def control2_cell_ok(key, c2):
    """대조군 ② 한 칸 — (통과?, 사유). 기대값 밖은 전부 False."""
    if not isinstance(c2, dict):
        return False, "기록 없음"
    outcome, error = c2.get("outcome"), c2.get("error")
    if error is not None:
        return False, f"오류 있음 ({canon(error)[:120]})"
    if key == CONTROL2_DIFF_CELL:
        if outcome != CONTROL2_DIFF_OUTCOME:
            return False, f"결과 {outcome!r} ≠ {CONTROL2_DIFF_OUTCOME!r}"
        received = c2.get("server_received")
        if not isinstance(received, list) or not received:
            return False, "서버 수신 문장 없음"
        for stmt in received:
            lines = str(stmt).split("\n")
            if len(lines) >= 2 and "@P" not in lines[0] and "@P" in lines[1]:
                return True, ""
        return False, f"서버 수신 문장 둘째 줄에 @P 없음 (수신 {len(received)}건)"
    if outcome != CONTROL2_SAME_OUTCOME:
        return False, f"결과 {outcome!r} ≠ {CONTROL2_SAME_OUTCOME!r}"
    return True, ""


# ──────────────────────────────────────────────────────────────
# 출력
# ──────────────────────────────────────────────────────────────

def md_escape(s):
    return str(s).replace("|", "\\|").replace("\n", " ")


def cell_summary(cell, checks):
    probe = cell["probe"] or {}
    return {
        "id": cell["id"],
        "folder": cell["folder"],
        "prereq_ok": all(c["ok"] for c in checks),
        "prereq": checks,
        "runtime_informational_version": dig(probe, "runtime", "informational_version"),
        "eol_mode": dig(cell["eol_json"], "mode"),
        "eol_assert_ok": dig(cell["eol_json"], "assert_ok"),
        "nuget_audit": cell["nuget_audit"],
        "image": cell["image"],
        "control2_outcome": dig(probe, "control2_param", "outcome"),
        "control2_server_received": dig(probe, "control2_param", "server_received"),
        "control2b_outcome": dig(probe, "control2b_qmark", "result"),
        "control1_db16": dig(probe, "control1_db16", "result"),
        "general_log_argument": dig(probe, "server", "general_log_argument"),
    }


def write_outputs(out_dir, verdict):
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, "verdict.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(verdict, fh, ensure_ascii=False, indent=1)

    md = ["# X733c MySqlConnector 실행 대조 — 비교 결과", ""]
    md += [f"> {n}" for n in verdict.get("header_notes", [])]
    md += ["", f"## 판정: **{verdict['label']}** (종료 코드 {verdict['exit_code']})", ""]
    if verdict.get("invalid_reasons"):
        md += ["### 무효 사유", ""] + [f"- {md_escape(r)}" for r in verdict["invalid_reasons"]] + [""]
    if verdict.get("error"):
        md += ["### 비교기 오류", "", f"- {md_escape(verdict['error'])}", ""]

    cells = verdict.get("cells", {})
    if cells:
        md += ["## 칸 × 전제", "", "| 칸 | 전제 | 로드된 조립품 | 줄끝 | NU1902 | 대조군 ② | 실패한 전제 |", "|---|---|---|---|---|---|---|"]
        for cid in sorted(cells):
            s = cells[cid]
            failed = "; ".join(f"{c['check']}({c['detail']})" for c in s["prereq"] if not c["ok"])
            md.append(f"| {cid} | {'통과' if s['prereq_ok'] else '어긋남'} | {md_escape(s['runtime_informational_version'])} | "
                      f"{s['eol_mode']} · 단언 {s['eol_assert_ok']} | {md_escape(s['nuget_audit'].get('nu1902'))} | "
                      f"{md_escape(s['control2_outcome'])} | {md_escape(failed)} |")
        md.append("")

    items = verdict.get("items", [])
    if items:
        md += ["## 비교 항목 (항목마다 보장하는 범위 / 못 보는 범위)", "",
               "| # | 항목 | 축 | 결과 | 보장하는 범위 | 못 보는 범위 | 상세 |", "|---|---|---|---|---|---|---|"]
        for it in items:
            md.append(f"| {it['id']} | {md_escape(it['title'])} | {it['axis']} | **{it['result']}** | "
                      f"{md_escape(it['guarantee'])} | {md_escape(it['blind'])} | {md_escape(it['detail'])[:400]} |")
        md.append("")

    info = verdict.get("informational", {})
    if info:
        md += ["## 기록만 (판정 안 함)", ""]
        for k, v in info.items():
            md.append(f"- **{md_escape(k)}**: {md_escape(v if isinstance(v, str) else canon(v))}")
        md.append("")

    with open(os.path.join(out_dir, "verdict.md"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(md) + "\n")


# ──────────────────────────────────────────────────────────────
# 본체
# ──────────────────────────────────────────────────────────────

def judge(cells_root, job_results):
    folders = sorted(d for d in glob.glob(os.path.join(cells_root, "*")) if os.path.isdir(d))
    loaded = [load_cell(f) for f in folders]

    invalid = job_result_reasons(job_results)
    summaries = {}
    groups = {"mariadb": {}, "mysql8": {}}
    for cell in loaded:
        checks = cell_prereq(cell)
        cid = cell["id"] or cell["folder"]
        summaries[cid] = cell_summary(cell, checks)
        for c in checks:
            if not c["ok"]:
                invalid.append(f"{cid}: {c['check']} — {c['detail']}")
        if cell["kind"] in groups:
            if cid in groups[cell["kind"]]:
                invalid.append(f"{cid}: 같은 칸이 두 번 있다")
            groups[cell["kind"]][cid] = cell
        else:
            invalid.append(f"{cid}: 칸 종류를 못 읽었다")

    heads = {c["head_sha"] for c in loaded}
    if len(heads) != 1 or "" in heads:
        invalid.append(f"칸 사이 HEAD SHA 다름/없음 — {sorted(heads)}")

    r1, t1 = group_prereq("MariaDB", groups["mariadb"], MARIADB_CELLS)
    r2, t2 = group_prereq("MySQL 8", groups["mysql8"], MYSQL8_CELLS)
    invalid += r1 + r2

    items = []
    if groups["mariadb"]:
        items += erp_items(groups["mariadb"])
    if groups["mysql8"]:
        items += backoffice_items(groups["mysql8"], prefix="M", axis="MySQL 8 · 판 축 · lf")
        items += backoffice_axis_items(groups["mysql8"], "lf (MySQL 8)")

    for it in items:
        if it["result"] == "비교불가":
            invalid.append(f"비교 항목 {it['id']} 비교불가 — {it['detail']}")
    if not items:
        invalid.append("비교 항목 0개 — 아무것도 재지 않았다")

    n = len(items)
    if invalid:
        code, label, verdict_word = 2, VERDICT_INVALID, "무효"
    elif any(it["result"] == VERDICT_DIFF for it in items):
        code, label, verdict_word = 1, VERDICT_DIFF, VERDICT_DIFF
    elif n > 0 and all(it["result"] == VERDICT_SAME for it in items):
        code, label, verdict_word = 0, f"{VERDICT_SAME}(비교 항목 {n}개)", VERDICT_SAME
    else:
        code, label, verdict_word = 2, VERDICT_INVALID, "무효"

    informational = {
        "대조군 ② 결과(MariaDB)": t1,
        "대조군 ② 결과(MySQL 8)": t2,
        "대조군 ②′ .sql 경로 변형(MariaDB · 기록만)": {
            cid: dig(c["probe"], "control2b_qmark", "result") and {
                k: dig(c["probe"], "control2b_qmark", "result").get(k)
                for k in ("ok", "applied", "ex_type", "err_no", "ex_msg1", "runner_msg1")}
            for cid, c in groups["mariadb"].items()},
        "DB-16 — 실제 설치/업데이트 경로의 기존 한계": DB16_EXISTING_LIMIT,
    }

    return {
        "schema": "x733c-verdict/1",
        "verdict": verdict_word,
        "label": label,
        "exit_code": code,
        "compared_items": n,
        "header_notes": HEADER_NOTES,
        "invalid_reasons": invalid,
        "cells": summaries,
        "items": items,
        "informational": informational,
    }


def main():
    if len(sys.argv) != 3:
        print("쓰임: compare_cells.py <칸 증거 폴더> <결과 폴더>", file=sys.stderr)
        return 2
    cells_root, out_dir = sys.argv[1], sys.argv[2]
    try:
        # [3-V] R1 — 칸 잡 결론(needs.*.result)은 워크플로가 env 로 넘긴다. 없으면 무효.
        verdict = judge(cells_root, {k: os.environ.get(k) for k in JOB_RESULT_ENVS})
    except Exception as exc:  # 비교기 자체 고장 — 무효(2)로 끝낸다. 같음·다름으로 가지 않는다.
        traceback.print_exc()
        verdict = {
            "schema": "x733c-verdict/1", "verdict": "무효", "label": VERDICT_INVALID, "exit_code": 2,
            "compared_items": 0, "header_notes": HEADER_NOTES,
            "invalid_reasons": ["비교기 예외로 판정 못 함"], "error": f"{type(exc).__name__}: {exc}",
            "cells": {}, "items": [], "informational": {},
        }
    write_outputs(out_dir, verdict)
    print(f"[X733c] 판정: {verdict['label']} · 비교 항목 {verdict['compared_items']} · 무효 사유 {len(verdict['invalid_reasons'])}")
    for r in verdict["invalid_reasons"][:40]:
        print(f"  - {r}")
    code = verdict["exit_code"]
    return code if code in (0, 1, 2) else 2


if __name__ == "__main__":
    # 🔴 PM 추가 지시 ⑤ — 어떤 예외도 1(다름)·0(같음)으로 새지 않게 한다. 잡히지 않은 예외의
    #    파이썬 기본 종료 코드는 1(= 다름)이라, 출력·쓰기 단계에서 죽어도 무효(2)로 끝낸다.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
        rc = main()
    except BaseException as fatal:  # noqa: BLE001 — SystemExit 포함 전부 무효로
        print(f"[X733c] 비교기 치명 오류 — 무효(2): {type(fatal).__name__}: {fatal}", file=sys.stderr)
        rc = 2
    sys.exit(rc)
