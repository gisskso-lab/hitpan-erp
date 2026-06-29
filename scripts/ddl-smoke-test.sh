#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────
# 출하 DDL 스모크 테스트 (E2E 게이트 1단계) — 2026-06-23 신설
#
# 목적(헌법 #36): installer/hitpan_db_clean.sql 이 "빈 DB 단일 import 로 신규설치 완결"
#   되는지 실측한다. import 실패·테이블 누락·코드가 쓰는 핵심 컬럼 누락이면 즉시 FAIL.
#   13차 매출반품/17차 discount_rate/16차 tax_invoices 류 "신규설치 DOA" 를 가장 직접 차단.
#
# 배경: 전수조사가 "빌드 0/0" 만 보고 "완료" 라 부른 게 반쪽완료·다음회차 누수의 근원이었다.
#   (메타감사 2026-06-23: 2축 감시 자가검증 한계 + E2E 0회). 이 스크립트가 그 게이트의 1단계다.
#
# 사용: bash scripts/ddl-smoke-test.sh
#   exit 0 = PASS (신규설치 import 무결), exit 1 = FAIL (좌표 출력)
#   운영 DB(hitpan_erp) 절대 안 건드림 — 임시 DB(hitpan_ddl_smoke) 만 생성·삭제.
# ──────────────────────────────────────────────────────────────────────────
set -uo pipefail

MYSQL="/c/Program Files/MariaDB 11.4/bin/mysql.exe"
DDL="installer/hitpan_db_clean.sql"
SMOKE_DB="hitpan_ddl_smoke"
DBUSER="root"   # 로컬 개발 환경 root(무비번). 임시 DB 생성권한 필요.

fail() { echo "❌ FAIL: $1"; "$MYSQL" -u "$DBUSER" -e "DROP DATABASE IF EXISTS $SMOKE_DB;" 2>/dev/null; exit 1; }

[ -f "$DDL" ] || fail "출하 DDL 파일 없음: $DDL"

echo "── 1) 빈 DB 생성 ──"
"$MYSQL" -u "$DBUSER" -e "DROP DATABASE IF EXISTS $SMOKE_DB; CREATE DATABASE $SMOKE_DB CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;" 2>/dev/null \
  || fail "임시 DB 생성 실패(권한 확인: $DBUSER)"

echo "── 2) clean DDL 전체 import ──"
IMPORT_ERR=$("$MYSQL" -u "$DBUSER" "$SMOKE_DB" < "$DDL" 2>&1)
if [ -n "$IMPORT_ERR" ]; then
  echo "$IMPORT_ERR" | head -20
  fail "clean DDL import 중 에러(위 출력) — 신규설치 시 동일 실패"
fi

echo "── 3) 테이블 수 게이트(123) ──"   # 2026-06-29 local_update_status 편입 122→123
TBL_CNT=$("$MYSQL" -u "$DBUSER" -N -B "$SMOKE_DB" -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='$SMOKE_DB' AND table_type='BASE TABLE';" 2>/dev/null)
echo "   생성된 테이블: $TBL_CNT"
[ "$TBL_CNT" -ge 123 ] || fail "테이블 수 부족($TBL_CNT < 123) — DDL 일부 미생성"

echo "── 4) 핵심 컬럼 스모크(전수조사 DOA 좌표) ──"
# 형식: "테이블 컬럼"  — 코드가 SELECT/INSERT 하는데 누락되면 신규설치 500 났던 자리들
CHECKS=(
  "item_special_prices discount_rate"      # 17차 P0-1
  "tax_invoices delivery_id"                # 16차 P0 (CancelAsync JOIN)
  "sales_returns return_reason"             # 13/14차 매출반품
  "sales_return_items original_unit_price"  # 14차
  "stock_ledger source_id"                  # 11/12차 BOM
  "stock_ledger source_type"                # 18차
  "journal_entries source_type"             # 12차
  "item_stock current_qty"                  # 8/9차 재고
  "collections source_id"                   # 16차 수금
  "approval_settings doc_type"              # 15차
  "partner_special_prices discount_rate"    # 19차 업체특별단가 할인율
)
MISSING=0
for chk in "${CHECKS[@]}"; do
  T=$(echo "$chk" | awk '{print $1}'); C=$(echo "$chk" | awk '{print $2}')
  EXISTS=$("$MYSQL" -u "$DBUSER" -N -B "$SMOKE_DB" -e "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='$SMOKE_DB' AND table_name='$T' AND column_name='$C';" 2>/dev/null)
  if [ "$EXISTS" != "1" ]; then echo "   ❌ 누락: $T.$C"; MISSING=$((MISSING+1)); else echo "   ✅ $T.$C"; fi
done
[ "$MISSING" -eq 0 ] || fail "핵심 컬럼 $MISSING 개 누락 — 코드가 쓰는데 DDL에 없음(신규설치 DOA)"

echo "── 5) 정리 ──"
"$MYSQL" -u "$DBUSER" -e "DROP DATABASE IF EXISTS $SMOKE_DB;" 2>/dev/null

echo ""
echo "✅ PASS — clean DDL 단일 import 신규설치 무결(테이블 $TBL_CNT, 핵심 컬럼 ${#CHECKS[@]}개 전수 존재)"
exit 0
