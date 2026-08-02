#!/usr/bin/env bash
# ══════════════════════════════════════════════════════════════════════════
# NCP 빌드 산출물 보관·회전 — 20260802 사장님 오더
#
#   *"NCP에 백오피스, 랜딩페이지 빌드설치파일 폴더 만들고,
#     특별히 빌드 설치파일 폴더를 효율적으로 관리해."*
#
# ── 왜 만드나 (2026-08-02 실측) ────────────────────────────────────────────
#   산출물이 5곳에 흩어져 있었고, 정리하는 곳은 1곳뿐이었다:
#     /var/www/updates/packages   ← 회전 있음(서버본에만, 레포엔 없음)
#     /var/hitpan/staging/web     119M  아무도 안 지움
#     /tmp/backoffice-*           111M  아무도 안 지움
#     /root/hitpan-1.2.3*.zip     358M  아무도 안 지움 (8/02 수동 삭제)
#     /opt/hitpan/*-bak-*         238M  deploy-web.sh 가 5세대 유지
#   ⇒ 게시할 때마다 여기저기 사본이 남아 10GB 디스크가 찼다.
#      백오피스 DB 는 정작 198MB(빈 데이터)인데 여유가 1.5GB까지 떨어졌다.
#
# ── 설계 원칙 ──────────────────────────────────────────────────────────────
#   1. 고객이 받는 경로(/var/www/updates/packages)는 이 스크립트가 '지우지 않는다'.
#      2026-07-29 에 그 폴더를 정리했다가 승인 메일이 가리키던 1.2.33 이 사라져
#      대리점이 404 를 맞았다(20260802 사고기록). 같은 실수를 구조적으로 막는다.
#      ⇒ 여기서는 '보고만' 한다. 회전은 publish-update.sh 소관.
#   2. manifest 가 가리키는 버전은 무슨 일이 있어도 보호한다(KEEP 계산에서 제외).
#   3. 지우기 전에 무엇을 지우는지 먼저 출력한다. --dry-run 이 기본.
#   4. 실행 계정이 root 여도 경로를 변수로 조립하지 않는다(오삭제 방지).
#
# ── 사용 ───────────────────────────────────────────────────────────────────
#   artifacts-manage.sh report              현황만 (기본, 아무것도 안 지움)
#   artifacts-manage.sh init                보관 폴더 생성
#   artifacts-manage.sh clean --dry-run     지울 대상 미리보기
#   artifacts-manage.sh clean --apply       실제 정리
#
# 헌법: #29(인프라 조작 사전승인) — clean --apply 는 사장님 결재 후에만.
#       #15(침묵 금지) — 건너뛴 것도 이유와 함께 출력한다.
# ══════════════════════════════════════════════════════════════════════════
set -euo pipefail

# ── 경로 (하드코딩. 변수 조립으로 인한 오삭제를 원천 차단) ──────────────────
LIVE_PACKAGES="/var/www/updates/packages"   # 🔴 고객 다운로드 경로 — 읽기만
LIVE_MANIFEST="/var/www/updates/manifest.json"
ARCHIVE_ROOT="/var/hitpan/artifacts"        # 새로 만드는 보관소
ARC_ERP="$ARCHIVE_ROOT/erp"                 #   ERP 설치본(EXE·ZIP) 보관
ARC_BO="$ARCHIVE_ROOT/backoffice"           #   백오피스 빌드 보관
ARC_LANDING="$ARCHIVE_ROOT/landing"         #   랜딩 빌드 보관
STAGING="/var/hitpan/staging"               # 배포 임시 — 회수 대상
OPT="/opt/hitpan"

KEEP_ARCHIVE="${HITPAN_KEEP_ARCHIVE:-3}"    # 보관소 세대 수
KEEP_BAK="${HITPAN_KEEP_BAK:-2}"            # /opt/hitpan/*-bak-* 세대 수
STALE_TMP_DAYS="${HITPAN_STALE_TMP_DAYS:-2}"

APPLY=0
MODE="${1:-report}"
for a in "$@"; do [[ "$a" == "--apply" ]] && APPLY=1; done

log()  { echo "[artifacts] $*"; }
warn() { echo "[artifacts] ⚠️  $*" >&2; }
die()  { echo "[artifacts] 🔴 $*" >&2; exit 1; }

hr() { echo "────────────────────────────────────────────────────────────"; }

# manifest 가 현재 가리키는 버전 — 절대 보호 대상
live_version() {
  [[ -f "$LIVE_MANIFEST" ]] || { echo ""; return; }
  sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$LIVE_MANIFEST" | head -1
}

# ── init: 보관 폴더 생성 ───────────────────────────────────────────────────
do_init() {
  hr; log "보관 폴더 생성"
  for d in "$ARC_ERP" "$ARC_BO" "$ARC_LANDING"; do
    if [[ -d "$d" ]]; then
      log "  이미 있음: $d"
    else
      mkdir -p "$d"
      log "  생성: $d"
    fi
  done
  cat > "$ARCHIVE_ROOT/README.txt" <<'TXT'
히트판 빌드 산출물 보관소
─────────────────────────────────────────────
  erp/         ERP 설치본(EXE·ZIP) 세대 보관
  backoffice/  백오피스 빌드 세대 보관
  landing/     랜딩 빌드 세대 보관

⚠️ 고객이 실제로 내려받는 곳은 여기가 아니라
   /var/www/updates/packages 입니다.
   그 폴더는 artifacts-manage.sh 가 절대 지우지 않습니다.
   (2026-07-29 그 폴더 정리로 대리점 404 사고 발생)

관리: installer/deploy/artifacts-manage.sh
TXT
  log "  안내문: $ARCHIVE_ROOT/README.txt"
}

# ── report: 현황 ───────────────────────────────────────────────────────────
do_report() {
  local lv; lv="$(live_version)"
  hr; log "디스크"
  df -Pm / | awk 'NR==2{printf "  전체 %sMB / 사용 %sMB / 여유 %sMB (%s)\n", $2,$3,$4,$5}'

  hr; log "🔴 고객 다운로드 경로 (이 스크립트는 지우지 않음)"
  log "  $LIVE_PACKAGES"
  if [[ -d "$LIVE_PACKAGES" ]]; then
    ls -1sh "$LIVE_PACKAGES" 2>/dev/null | sed 's/^/    /'
    log "  합계: $(du -sh "$LIVE_PACKAGES" 2>/dev/null | cut -f1)"
  else
    warn "  경로 없음"
  fi
  log "  manifest 현행 버전: ${lv:-(읽기 실패)}  ← 회전 시 절대 보호"

  hr; log "보관소"
  for d in "$ARC_ERP" "$ARC_BO" "$ARC_LANDING"; do
    if [[ -d "$d" ]]; then
      log "  $(du -sh "$d" 2>/dev/null | cut -f1)	$d  ($(ls -1 "$d" 2>/dev/null | wc -l)개)"
    else
      log "  (없음)	$d   → init 필요"
    fi
  done

  hr; log "회수 대상 (clean 이 정리)"
  [[ -d "$STAGING" ]] && log "  $(du -sh "$STAGING" 2>/dev/null | cut -f1)	$STAGING"
  local baks; baks=$(ls -1d "$OPT"/*-bak-* 2>/dev/null | wc -l)
  log "  $(du -csh "$OPT"/*-bak-* 2>/dev/null | tail -1 | cut -f1)	$OPT/*-bak-*  (${baks}개, KEEP=$KEEP_BAK)"
  local tmpsz; tmpsz=$(du -csm /tmp/backoffice-* /tmp/landing* 2>/dev/null | tail -1 | cut -f1 || echo 0)
  log "  ${tmpsz}M	/tmp/backoffice-* · /tmp/landing*"
}

# ── clean ─────────────────────────────────────────────────────────────────
rm_path() {
  local p="$1" why="$2"
  [[ -e "$p" ]] || return 0
  local sz; sz=$(du -sh "$p" 2>/dev/null | cut -f1)
  if (( APPLY )); then
    rm -rf "$p"
    log "  삭제 $sz	$p	($why)"
  else
    log "  [예정] $sz	$p	($why)"
  fi
}

do_clean() {
  local lv; lv="$(live_version)"
  hr
  if (( APPLY )); then
    log "정리 실행 (--apply)"
  else
    log "정리 미리보기 — 실제 삭제 없음. 실행하려면 --apply"
  fi

  # 1) 배포 스테이징 — 배포 때마다 다시 만들어진다
  hr; log "[1] 배포 스테이징"
  rm_path "$STAGING/web" "배포 시 재생성됨"

  # 2) /tmp 빌드 찌꺼기 — N일 지난 것만
  hr; log "[2] /tmp 빌드 찌꺼기 (${STALE_TMP_DAYS}일 경과분만)"
  local found=0
  while IFS= read -r p; do
    [[ -n "$p" ]] || continue
    rm_path "$p" "${STALE_TMP_DAYS}일 이상 방치"
    found=1
  done < <(find /tmp -maxdepth 1 \( -name 'backoffice-*' -o -name 'landing*' \) \
             -mtime "+$STALE_TMP_DAYS" 2>/dev/null || true)
  (( found )) || log "  대상 없음"

  # 3) /opt 배포 백업 — 롤백용이라 KEEP 세대는 남긴다
  hr; log "[3] 배포 백업 (/opt/hitpan/*-bak-*, 최신 ${KEEP_BAK}세대 유지)"
  for base in backoffice-api backoffice-web landing; do
    local n=0
    while IFS= read -r p; do
      [[ -n "$p" ]] || continue
      n=$((n+1))
      (( n > KEEP_BAK )) && rm_path "$p" "$base ${n}번째 세대(오래됨)"
    done < <(ls -1dt "$OPT/$base"-bak-* 2>/dev/null || true)
  done

  # 4) 보관소 세대 회전
  hr; log "[4] 보관소 회전 (각 ${KEEP_ARCHIVE}세대 유지)"
  for d in "$ARC_ERP" "$ARC_BO" "$ARC_LANDING"; do
    [[ -d "$d" ]] || continue
    local n=0
    while IFS= read -r p; do
      [[ -n "$p" ]] || continue
      # manifest 현행 버전이 이름에 들어있으면 절대 안 지운다
      if [[ -n "$lv" && "$(basename "$p")" == *"$lv"* ]]; then
        log "  보호  $(basename "$p")	(manifest 현행 $lv)"
        continue
      fi
      n=$((n+1))
      (( n > KEEP_ARCHIVE )) && rm_path "$p" "$(basename "$d") ${n}번째 세대"
    done < <(ls -1dt "$d"/* 2>/dev/null || true)
  done

  # 5) 고객 경로는 보고만
  hr; log "[5] 🔴 고객 다운로드 경로 — 건드리지 않음"
  log "  $LIVE_PACKAGES 는 이 스크립트의 삭제 대상이 아니다."
  log "  회전은 publish-update.sh(PKG_KEEP) 소관이며, manifest 현행 버전($lv)은"
  log "  거기서도 보호돼야 한다 — 2026-07-29 그 보호가 없어 대리점 404 가 났다."

  hr
  df -Pm / | awk 'NR==2{printf "[artifacts] 결과 여유: %sMB\n", $4}'
  (( APPLY )) || log "미리보기였습니다. 실제 정리는 --apply"
}

case "$MODE" in
  init)   do_init; do_report ;;
  report) do_report ;;
  clean)  do_clean ;;
  *)      die "사용법: $(basename "$0") {report|init|clean [--apply]}" ;;
esac
