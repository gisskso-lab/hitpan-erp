#!/usr/bin/env bash
# 인덱스 마이그 선언 누락 검사 — 인덱스를 만드는 DB-NN_*.sql 은 '-- @verify-index:' 를 선언해야 한다
#
# 근거: 20260921작2 §14-4(CI) · §15-1 P-21 PM 결재 / 설계 §16-2 S-3'
#
# 왜 이 검사가 있나 (2026-09-21):
#   인덱스 마이그는 **실패해도 오류 0건 · 화면 정상**이다. 조용히 느려지고 조용히 넓게 잠근다.
#   그래서 업데이트 검증(워치독 VerifyNewVersionAsync 3번째 조건)이 "선언된 인덱스가 실제로 있는가"를
#   확인하는데, 그 확인 목록을 **코드에 하드코딩하지 않는다** — 마이그 파일 머리말의 선언에서 읽는다.
#
#   🔴 그러면 남는 구멍은 하나다: **다음 마이그가 선언을 안 적는 것.**
#     선언이 없으면 확인 대상이 0개라 워치독은 아무것도 안 보고 통과시킨다 = 구멍이 그대로 다시 난다.
#     사람이 매번 기억하는 것에 맡기지 않는다. CI 가 대신 기억한다.
#
# 판정 규칙:
#   src/HitPan.API/Migrations/SQL/DB-NN*_*.sql 중
#     · ADD KEY / ADD INDEX / ADD UNIQUE KEY|INDEX / CREATE [UNIQUE] INDEX 를 담고 있고
#     · '-- @verify-index:' 선언이 한 줄도 없으면  → FAIL
#   주석(-- 로 시작하는 줄)에 있는 ADD KEY 는 세지 않는다(되돌리기용 DROP INDEX 주석 등).
#
#   기준 번호(BASELINE): 이 규약이 생기기 전의 마이그는 소급 적용하지 않는다.
#     옛 파일을 지금 고치는 것은 #1(덮어쓰기 금지)에 걸리고, 이미 적용된 DB 를 바꾸지도 못한다.
#     새로 들어오는 것만 막는다.
#
# 종료 코드: 0 = 통과, 1 = 선언 누락(빌드 실패시킬 것)

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SQL_DIR="$ROOT/src/HitPan.API/Migrations/SQL"

# 이 번호부터 규약 적용(20260921작2 T3-2 의 DB-125 가 첫 대상).
BASELINE=125

if [ ! -d "$SQL_DIR" ]; then
  echo "[verify-index] 마이그 폴더가 없다: $SQL_DIR"
  exit 1
fi

fail=0
checked=0

for f in "$SQL_DIR"/DB-*.sql; do
  [ -e "$f" ] || continue
  base="$(basename "$f")"

  num="$(printf '%s' "$base" | sed -n 's/^DB-0*\([0-9][0-9]*\).*/\1/p')"
  [ -n "$num" ] || continue
  [ "$num" -ge "$BASELINE" ] || continue

  # 주석 줄을 뺀 본문에서만 인덱스 생성문을 찾는다.
  body="$(grep -v '^[[:space:]]*--' "$f" || true)"
  if printf '%s' "$body" | grep -Eiq '(ADD[[:space:]]+(UNIQUE[[:space:]]+)?(KEY|INDEX))|(CREATE[[:space:]]+(UNIQUE[[:space:]]+)?INDEX)'; then
    checked=$((checked + 1))
    if ! grep -Eq '^[[:space:]]*--.*@verify-index:[[:space:]]*[A-Za-z0-9_]+\.[A-Za-z0-9_]+[[:space:]]*$' "$f"; then
      echo "❌ [verify-index] $base — 인덱스를 만드는데 '-- @verify-index: <table>.<index_name>' 선언이 없다."
      fail=1
    else
      n="$(grep -Ec '^[[:space:]]*--.*@verify-index:' "$f")"
      echo "✅ [verify-index] $base — 선언 ${n}줄"
    fi
  fi
done

if [ "$fail" -ne 0 ]; then
  cat <<'EOF'

🔴 인덱스 마이그에는 머리말 선언이 필요하다 (20260921작2 §14-4 · 설계 §16-2 S-3').

  마이그 파일 머리말에 만들기로 한 인덱스를 한 줄씩 적는다:

    -- @verify-index: sales_deliveries.idx_sd_tenant_partner_src
    -- @verify-index: collections.idx_coll_tenant_doc_cover

  이 선언이 업데이트 검증의 확인 목록이다. 없으면 워치독은 확인할 대상이 0개라
  "인덱스가 안 만들어진 채 신버전 코드만 올라간 상태" 에 초록을 준다.
  인덱스 마이그는 실패해도 오류 0건 · 화면 정상이라 아무도 못 본다 — 그래서 여기서 막는다.
EOF
  exit 1
fi

echo "[verify-index] 통과 — 인덱스 마이그 ${checked}건 검사(기준 DB-${BASELINE} 이상)"
exit 0
