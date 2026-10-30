# -*- coding: utf-8 -*-
"""X733c 계측 장치 — SQL 줄끝 바꾸기 (작업지시서 20261030작 §3-3).

계측 가지 전용 · 머지 금지. CI 칸 안에서만 돈다(체크아웃 직후 · restore/빌드 전).

쓰임:  python3 -I tests/measure/x733c/eol_convert.py <lf|crlf> <결과 JSON 경로>
       (레포 루트에서 실행한다)

하는 일
  1. 대상 = src/HitPan.API/Migrations/SQL/DB-*.sql + installer/backoffice/*.sql
           + tests/measure/x733c/controls/*.sql
     출하 DDL(installer/hitpan_db_clean.sql)은 바꾸지 않는다 — CLI 경로라 드라이버와 무관하고
     네 칸의 DB 초기 상태를 같게 두는 쪽이 고정 항목이다.
  2. 바이트로 CRLF -> LF 정규화. 정규화 뒤 홀로 남은 CR 이 있으면 멈춘다(종료 3).
  3. crlf 칸만 LF -> CRLF. 마지막 줄은 원래 있던 LF 만 바뀐다(맨 CR 을 덧붙이지 않는다).
  4. 파일마다 전후 바이트 수 · LF 수 · CR 수 · CRLF 수 · sha256 을 JSON 으로 남긴다.
  5. 단언 — lf 칸: 모든 파일 CR 0 · crlf 칸: 모든 파일 CR = LF = CRLF. 어긋나면 종료 4.

판정은 하지 않는다. 같다/다르다/무효 는 compare_cells.py 의 몫이다.
"""
import glob
import hashlib
import json
import os
import sys

TARGET_GLOBS = [
    ("erp", "src/HitPan.API/Migrations/SQL/DB-*.sql"),
    ("backoffice", "installer/backoffice/*.sql"),
    ("control", "tests/measure/x733c/controls/*.sql"),
]

UNTOUCHED = "installer/hitpan_db_clean.sql"


def counts(data: bytes) -> dict:
    return {
        "bytes": len(data),
        "lf": data.count(b"\n"),
        "cr": data.count(b"\r"),
        "crlf": data.count(b"\r\n"),
        "sha256": hashlib.sha256(data).hexdigest(),
    }


def main() -> int:
    if len(sys.argv) != 3 or sys.argv[1] not in ("lf", "crlf"):
        print("쓰임: eol_convert.py <lf|crlf> <결과 JSON 경로>", file=sys.stderr)
        return 2
    mode, out_path = sys.argv[1], sys.argv[2]

    files = []
    for group, pattern in TARGET_GLOBS:
        hits = sorted(glob.glob(pattern))
        if not hits:
            print(f"[X733c] 대상 0건: {pattern} — 레포 루트에서 돌렸나", file=sys.stderr)
            return 2
        files.extend((group, p.replace(os.sep, "/")) for p in hits)

    untouched_before = None
    if os.path.exists(UNTOUCHED):
        with open(UNTOUCHED, "rb") as fh:
            untouched_before = hashlib.sha256(fh.read()).hexdigest()

    records = []
    bad = []
    for group, path in files:
        with open(path, "rb") as fh:
            before = fh.read()
        norm = before.replace(b"\r\n", b"\n")
        if b"\r" in norm:
            print(f"[X733c] 홀로 남은 CR 발견 — 멈춘다: {path}", file=sys.stderr)
            return 3
        after = norm.replace(b"\n", b"\r\n") if mode == "crlf" else norm
        if after != before:
            with open(path, "wb") as fh:
                fh.write(after)
        with open(path, "rb") as fh:
            reread = fh.read()
        a = counts(reread)
        if mode == "lf" and a["cr"] != 0:
            bad.append(f"{path}: lf 칸인데 CR {a['cr']}")
        if mode == "crlf" and not (a["cr"] == a["lf"] == a["crlf"]):
            bad.append(f"{path}: crlf 칸인데 CR {a['cr']} · LF {a['lf']} · CRLF {a['crlf']}")
        records.append({
            "group": group,
            "path": path,
            "name": os.path.basename(path),
            "before": counts(before),
            "after": a,
        })

    untouched_after = None
    if os.path.exists(UNTOUCHED):
        with open(UNTOUCHED, "rb") as fh:
            untouched_after = hashlib.sha256(fh.read()).hexdigest()

    result = {
        "mode": mode,
        "file_count": {g: sum(1 for r in records if r["group"] == g) for g, _ in TARGET_GLOBS},
        "untouched_shipping_ddl": {
            "path": UNTOUCHED,
            "sha256_before": untouched_before,
            "sha256_after": untouched_after,
        },
        "assert_ok": not bad,
        "assert_failures": bad,
        "files": records,
    }
    with open(out_path, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(result, fh, ensure_ascii=False, indent=1)

    print(f"[X733c] 줄끝 {mode} · 파일 {len(records)} · "
          + " · ".join(f"{g} {n}" for g, n in result["file_count"].items()))
    if bad:
        for line in bad:
            print(f"[X733c] 단언 실패 — {line}", file=sys.stderr)
        return 4
    return 0


if __name__ == "__main__":
    sys.exit(main())
