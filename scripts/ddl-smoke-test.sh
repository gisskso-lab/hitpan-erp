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

# 20260723작1 G3 — CI(ubuntu MariaDB 서비스 컨테이너) env 파라미터화.
#   하위호환 절대: env 미주입 시 로컬(윈도 MariaDB 경로·root 무비번) 그대로 동작.
#   개발자 `bash scripts/ddl-smoke-test.sh` 손실행 = 종전과 100% 동일.
#   CI 는 HITPAN_MYSQL=mysql / HITPAN_DB_USER=root / HITPAN_DB_PASS=<service 비번> 주입.
MYSQL="${HITPAN_MYSQL:-/c/Program Files/MariaDB 11.4/bin/mysql.exe}"
DDL="installer/hitpan_db_clean.sql"
SMOKE_DB="hitpan_ddl_smoke"
DBUSER="${HITPAN_DB_USER:-root}"   # 로컬 기본 root(무비번). CI 는 서비스 컨테이너 root.
# 접속 옵션(host/port/pw)은 env 로만 주입(선택). 미주입 시 옵션 자체를 안 붙여
#   로컬(소켓 접속·무비번)과 100% 정합. CI(TCP·비번)는 아래 3개를 주입한다.
#   ⚠️ MYSQL 은 실행파일 경로 하나만 — host/port 를 여기 섞으면 "$MYSQL" 호출이 깨진다.
MYSQL_CONN_OPT=()
[ -n "${HITPAN_DB_HOST:-}" ] && MYSQL_CONN_OPT+=("--host=$HITPAN_DB_HOST")
[ -n "${HITPAN_DB_PORT:-}" ] && MYSQL_CONN_OPT+=("--port=$HITPAN_DB_PORT")
[ -n "${HITPAN_DB_PASS:-}" ] && MYSQL_CONN_OPT+=("-p$HITPAN_DB_PASS")

fail() { echo "❌ FAIL: $1"; "$MYSQL" -u "$DBUSER" "${MYSQL_CONN_OPT[@]}" -e "DROP DATABASE IF EXISTS $SMOKE_DB;" 2>/dev/null; exit 1; }

[ -f "$DDL" ] || fail "출하 DDL 파일 없음: $DDL"

echo "── 1) 빈 DB 생성 ──"
"$MYSQL" -u "$DBUSER" "${MYSQL_CONN_OPT[@]}" -e "DROP DATABASE IF EXISTS $SMOKE_DB; CREATE DATABASE $SMOKE_DB CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;" 2>/dev/null \
  || fail "임시 DB 생성 실패(권한 확인: $DBUSER)"

echo "── 2) clean DDL 전체 import ──"
IMPORT_ERR=$("$MYSQL" -u "$DBUSER" "${MYSQL_CONN_OPT[@]}" "$SMOKE_DB" < "$DDL" 2>&1)
if [ -n "$IMPORT_ERR" ]; then
  echo "$IMPORT_ERR" | head -20
  fail "clean DDL import 중 에러(위 출력) — 신규설치 시 동일 실패"
fi

echo "── 3) 테이블 수 게이트(125) ──"   # 2026-06-29 schema_migrations 편입 123→124 (고리4 ①) / 2026-07-20 local_update_apply_status 편입 124→125 (고리4 §W4-6)
TBL_CNT=$("$MYSQL" -u "$DBUSER" "${MYSQL_CONN_OPT[@]}" -N -B "$SMOKE_DB" -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='$SMOKE_DB' AND table_type='BASE TABLE';" 2>/dev/null)
echo "   생성된 테이블: $TBL_CNT"
[ "$TBL_CNT" -ge 125 ] || fail "테이블 수 부족($TBL_CNT < 125) — DDL 일부 미생성"

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
  EXISTS=$("$MYSQL" -u "$DBUSER" "${MYSQL_CONN_OPT[@]}" -N -B "$SMOKE_DB" -e "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='$SMOKE_DB' AND table_name='$T' AND column_name='$C';" 2>/dev/null)
  if [ "$EXISTS" != "1" ]; then echo "   ❌ 누락: $T.$C"; MISSING=$((MISSING+1)); else echo "   ✅ $T.$C"; fi
done
[ "$MISSING" -eq 0 ] || fail "핵심 컬럼 $MISSING 개 누락 — 코드가 쓰는데 DDL에 없음(신규설치 DOA)"

echo "── 5) schema_migrations 시드 자기참조 게이트 (20260722작4 CTO B-1/B-2) ──"
# 왜: clean DDL 의 schema_migrations 시드가 신규설치 DB 에 마이그 이력을 채운다(작4 봉합 #1).
#   이게 비거나 소스 마이그 파일과 어긋나면, 자동업데이트 교차검증 게이트가 신규 고객을
#   전량 차단한다(2026-07-22 실측 사고). 손 매직넘버(55) 금지 — 소스 파일 집합과 자동 대조한다.
#   다음 릴리스에 DB-85 를 추가하고 시드 INSERT 를 안 늘리면 여기서 CI 가 빨간불이 된다.
MIG_DIR="src/HitPan.API/Migrations/SQL"
[ -d "$MIG_DIR" ] || fail "마이그 폴더 없음: $MIG_DIR"
# 게이트(UpdateOrchestrator.NormalizeMigrationId)와 동일 규칙으로 고유 DB-NN(접미사 포함) 집합 카운트.
#   파일명 DB-<num><suffix>_... → DB-<num 2자리 패딩><suffix>. 예: DB-8b_x.sql → DB-08b.
FILE_IDS=$(ls "$MIG_DIR"/DB-*.sql 2>/dev/null \
  | sed -E 's|.*/||; s/^DB-([0-9]+)([a-zA-Z]*)_.*/\1 \2/' \
  | awk '{ printf "DB-%02d%s\n", $1, $2 }' \
  | sort -u)
FILE_CNT=$(echo "$FILE_IDS" | grep -c . )
SEED_CNT=$("$MYSQL" -u "$DBUSER" "${MYSQL_CONN_OPT[@]}" -N -B "$SMOKE_DB" -e "SELECT COUNT(*) FROM schema_migrations WHERE success=1;" 2>/dev/null)
echo "   소스 마이그 파일(고유 DB-NN): $FILE_CNT / clean DDL 시드(success=1): $SEED_CNT"
if [ "$FILE_CNT" != "$SEED_CNT" ]; then
  # 어느 쪽이 빠졌는지 좌표로 찍는다(손으로 헤매지 않게).
  SEED_IDS=$("$MYSQL" -u "$DBUSER" "${MYSQL_CONN_OPT[@]}" -N -B "$SMOKE_DB" -e "SELECT migration_id FROM schema_migrations WHERE success=1 ORDER BY migration_id;" 2>/dev/null)
  echo "   ▸ 파일엔 있는데 시드에 없음:"; comm -23 <(echo "$FILE_IDS") <(echo "$SEED_IDS" | sort -u) | sed 's/^/       /'
  echo "   ▸ 시드엔 있는데 파일에 없음:"; comm -13 <(echo "$FILE_IDS") <(echo "$SEED_IDS" | sort -u) | sed 's/^/       /'
  fail "schema_migrations 시드($SEED_CNT) != 소스 마이그 파일($FILE_CNT) — clean DDL 시드 갱신 필요(작4 #1). 어긋나면 신규 고객 자동업데이트 전량 차단."
fi
echo "   ✅ 시드 == 파일 ($SEED_CNT) — 신규설치 자동업데이트 게이트 통과 보장"

echo "── 6) 정리 ──"
"$MYSQL" -u "$DBUSER" "${MYSQL_CONN_OPT[@]}" -e "DROP DATABASE IF EXISTS $SMOKE_DB;" 2>/dev/null

echo ""
echo "✅ PASS — clean DDL 단일 import 신규설치 무결(테이블 $TBL_CNT, 핵심 컬럼 ${#CHECKS[@]}개 전수 존재)"
exit 0
