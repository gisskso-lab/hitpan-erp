#!/usr/bin/env bash
# 권한 메뉴 코드 정합 검사 — 프론트 ErpMenus ↔ 백엔드 MenuList
#
# 왜 이 검사가 있나 (2026-08-09 봉합):
#   프론트가 하드코딩한 메뉴 코드 4개가 백엔드와 달랐다.
#     ITEM_MASTER vs ITEM · PARTNER_MASTER vs PARTNER · PURCHASE_RECEIPT vs PURCHASE · RETURN(백엔드 없음)
#   화면은 "권한이 저장됐습니다" 라고 말하는데, 실제 권한 조회는 백엔드 코드로 하므로 항상 0.
#   사장님이 몇 번을 체크해도 직원은 계속 403 이고, 화면에는 원인 표시가 한 줄도 없었다.
#   또 백엔드에만 있던 USERS·APPROVAL 등은 화면에서 사라져 부여할 방법 자체가 없었다.
#
#   주석은 잊혀진다. 그래서 CI 가 대신 기억한다.
#
# 종료 코드: 0 = 일치, 1 = 불일치(빌드 실패시킬 것)

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BACK="$ROOT/src/HitPan.Application/Services/PermissionService.cs"
FRONT="$ROOT/src/HitPan.Web/Pages/Settings/PermissionPage.razor.cs"

fail() { echo "[permission-sync] 🔴 $*" >&2; exit 1; }

[ -f "$BACK" ]  || fail "백엔드 파일 없음: $BACK"
[ -f "$FRONT" ] || fail "프론트 파일 없음: $FRONT"

# 목록 블록만 잘라서 ("CODE", "이름") 의 CODE 만 뽑는다.
# 주석줄(//)은 제외한다 — 주석 속 예시 코드가 섞이면 오탐이 난다.
extract() {
  local file="$1" start_pat="$2"
  awk -v pat="$start_pat" '
    $0 ~ pat { inblock = 1 }
    inblock && /^\s*\]|^\s*\};/ { inblock = 0 }
    inblock { print }
  ' "$file" \
  | grep -v '^\s*//' \
  | grep -oE '\("[A-Z_]+"' \
  | tr -d '("' \
  | sort -u
}

BACK_CODES="$(extract "$BACK" 'MenuList *=')"
FRONT_CODES="$(extract "$FRONT" 'ErpMenus *=')"

[ -n "$BACK_CODES" ]  || fail "백엔드 MenuList 파싱 실패 — 코드를 하나도 못 찾았다"
[ -n "$FRONT_CODES" ] || fail "프론트 ErpMenus 파싱 실패 — 코드를 하나도 못 찾았다"

ONLY_BACK="$(comm -23 <(echo "$BACK_CODES") <(echo "$FRONT_CODES"))"
ONLY_FRONT="$(comm -13 <(echo "$BACK_CODES") <(echo "$FRONT_CODES"))"

if [ -z "$ONLY_BACK" ] && [ -z "$ONLY_FRONT" ]; then
  echo "[permission-sync] ✅ 일치 ($(echo "$BACK_CODES" | wc -l | tr -d ' ')개)"
  exit 0
fi

echo "[permission-sync] 🔴 권한 메뉴 코드가 어긋났다 — 권한이 안 먹는 사고로 이어진다" >&2
echo "" >&2

if [ -n "$ONLY_BACK" ]; then
  echo "  백엔드에만 있음 (화면에서 부여할 방법이 없다):" >&2
  echo "$ONLY_BACK" | sed 's/^/    - /' >&2
fi

if [ -n "$ONLY_FRONT" ]; then
  echo "  프론트에만 있음 (체크해도 권한이 안 먹는다):" >&2
  echo "$ONLY_FRONT" | sed 's/^/    - /' >&2
fi

echo "" >&2
echo "  고칠 곳 — 두 파일을 함께 고친다:" >&2
echo "    백엔드: src/HitPan.Application/Services/PermissionService.cs  (MenuList)" >&2
echo "    프론트: src/HitPan.Web/Pages/Settings/PermissionPage.razor.cs (ErpMenus)" >&2
exit 1
