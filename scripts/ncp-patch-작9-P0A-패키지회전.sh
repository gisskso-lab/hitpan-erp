#!/usr/bin/env bash
# ══════════════════════════════════════════════════════════════════════════════
# 20260730작9 P0-A — publish-update.sh 패키지 회전 추가 (NCP 상주본 패치)
#
# ■ 무엇을 고치나
#   게시 스크립트에 BACKUP_KEEP(manifest .bak 회전)은 있는데
#   **패키지 본체(ZIP 102MB·EXE 270MB) 회전이 없어** 게시 1회당 372MB 가 영구 누적됐다.
#   → 2026-07-30 실측: 디스크 9.8G 100% → MySQL 기동 불가 → 랜딩 가입 500 (사업 중단)
#
# ■ 왜 사장님이 실행하나 (헌법 #29 — 인프라 사전 승인제)
#   /opt/hitpan/installer/updates/ 는 sudoers 화이트리스트 밖의 상주 실행 스크립트다.
#   PM 이 SSH 로 임의 수술하지 않는다. 이 파일을 사장님이 검토하고 실행한다.
#
# ■ 실행 방법 (사장님)
#   1) NCP 접속:
#      ssh -i ~/.ssh/id_ed25519 root@211.188.58.140
#   2) 아래 내용을 서버에 붙여넣어 실행하거나, 이 파일을 scp 후 bash 로 실행
#
# ■ 안전장치
#   · 실행 전 원본을 .bak-작9-<타임스탬프> 로 백업 (되돌리기 가능)
#   · 이미 적용돼 있으면 아무것도 하지 않고 종료 (멱등)
#   · 패치 후 bash -n 문법 검사 → 실패 시 자동 원복
#   · 게시 로직 자체는 한 줄도 바꾸지 않는다 (회전 블록 추가만 — 헌법 #1)
# ══════════════════════════════════════════════════════════════════════════════
set -euo pipefail

TARGET="/opt/hitpan/installer/updates/publish-update.sh"
STAMP="$(date +%Y%m%d-%H%M%S)"
BAK="${TARGET}.bak-작9-${STAMP}"
MARKER="20260730작9 P0-A"

echo "══ 작9 P0-A 패키지 회전 패치 ══"

[[ -f "$TARGET" ]] || { echo "❌ 대상 없음: $TARGET"; exit 1; }

# 멱등 — 이미 적용됐으면 종료
if grep -q "$MARKER" "$TARGET"; then
  echo "✅ 이미 적용돼 있습니다($MARKER) — 아무것도 하지 않습니다."
  exit 0
fi

# 삽입 기준선 확인 — 'ok  "배포 완료' 줄 앞에 넣는다(모든 검증 통과 후, 성공 선언 전)
ANCHOR='^ok  "배포 완료'
if ! grep -qE "$ANCHOR" "$TARGET"; then
  echo "❌ 삽입 기준선을 찾지 못했습니다(스크립트 구조 변경됨). 수동 확인 필요."
  echo "   기대: ok  \"배포 완료 — updates.hitpan.kr ...\""
  exit 1
fi

cp -p "$TARGET" "$BAK"
echo "· 백업 생성: $BAK"

# ── 회전 블록 작성 ───────────────────────────────────────────────────────────
ROTATE_BLOCK="$(cat <<'ROTEOF'
# ── 6) 패키지 회전 (20260730작9 P0-A — 디스크 100% 사망 재발 방지) ──────────────
#   ■ 왜 필요한가
#     BACKUP_KEEP 은 manifest .bak 만 회전한다. 패키지 본체(ZIP 102MB·EXE 270MB)는
#     한 번도 회수되지 않아 게시 1회당 372MB 가 영구 누적됐다.
#     실측 2026-07-30: 디스크 9.8G 100%(여유 0) → mysql.service failed →
#       LandingSignupController.Signup:69 에서 DB 연결 실패 → 랜딩 가입 500.
#     즉 게시 파이프라인이 자기 DB 를 죽였다.
#   ■ 왜 여기인가
#     위의 서빙 검증·EXE 도달 확인을 모두 통과한 뒤다. 검증 전에 지우면 롤백 대상이 사라진다.
#   ■ 왜 2세대인가
#     현행(N) + 직전(N-1). 직전은 롤백 대상이라 반드시 남긴다.
#   ■ 실패해도 게시는 성공으로 둔다
#     회전 실패가 정상 게시를 되돌리면 본말전도다. 경고만 남긴다(헌법 #15 — 침묵 아님).
PKG_KEEP="${HITPAN_PKG_KEEP:-2}"

hitpan_rotate_pkg() {
  local pattern="$1" label="$2"
  local files=() f old base
  # mtime 최신순. 버전 문자열 정렬은 쓰지 않는다 — '1.2.9' > '1.2.10' 오판으로
  #   1.2.100 도달 시 현행본을 지울 수 있다.
  while IFS= read -r f; do [[ -n "$f" ]] && files+=("$f"); done < <(
    find "$PACKAGES_DIR" -maxdepth 1 -type f -name "$pattern" -printf '%T@\t%p\n' 2>/dev/null \
      | sort -rn | cut -f2-
  )
  (( ${#files[@]} > PKG_KEEP )) || return 0
  for old in "${files[@]:$PKG_KEEP}"; do
    base="$(basename "$old")"
    # 이중 안전망 — mtime 이 꼬여도(수동 touch 등) 방금 올린 것은 절대 지우지 않는다
    if [[ "$base" == "${ZIP_BASENAME:-}" || "$base" == "${EXE_BASENAME:-}" ]]; then
      warn "회전 대상에 현행 게시본이 포함됐다 — 건너뜀: $base"
      continue
    fi
    if rm -f "$old"; then
      log "$label 회전 삭제: $base"
    else
      warn "$label 회전 삭제 실패(무시하고 진행): $base"
    fi
  done
}

hitpan_rotate_pkg 'hitpan-*.zip'           'ZIP'
hitpan_rotate_pkg 'HitPan-ERP-Setup-*.exe' 'EXE'
log "패키지 회전 완료(KEEP=$PKG_KEEP) — packages 사용량: $(du -sh "$PACKAGES_DIR" 2>/dev/null | cut -f1)"
log "디스크 현황: $(df -h / | awk 'NR==2{print $4" 여유 ("$5" 사용)"}')"

ROTEOF
)"

# ── 삽입 (기준선 앞) ─────────────────────────────────────────────────────────
TMP="$(mktemp)"
awk -v block="$ROTATE_BLOCK" '
  /^ok  "배포 완료/ && !done { print block; done=1 }
  { print }
' "$TARGET" > "$TMP"

# 삽입 확인
if ! grep -q "$MARKER" "$TMP"; then
  echo "❌ 삽입 실패 — 원본 유지."
  rm -f "$TMP"
  exit 1
fi

# 문법 검사 — 실패 시 원복
if ! bash -n "$TMP" 2>/tmp/작9-syntax-err.txt; then
  echo "❌ 패치 결과가 bash 문법 오류 — 원본 유지."
  cat /tmp/작9-syntax-err.txt
  rm -f "$TMP"
  exit 1
fi

cat "$TMP" > "$TARGET"      # 권한·소유 유지(리다이렉트로 내용만 덮음)
rm -f "$TMP"

echo "· 문법 검사 통과 (bash -n)"
echo "· 적용 완료: $TARGET"
echo
echo "══ 확인 ══"
grep -n "$MARKER" "$TARGET" | head -3
echo
echo "현재 packages 상태:"
ls -la /var/www/updates/packages/ 2>/dev/null || true
echo
df -h / | tail -1
echo
echo "✅ 완료. 다음 게시부터 ZIP·EXE 가 각 2세대만 유지됩니다."
echo "   되돌리려면: cp -p '$BAK' '$TARGET'"
