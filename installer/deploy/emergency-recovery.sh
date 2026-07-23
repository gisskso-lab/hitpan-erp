#!/usr/bin/env bash
# =============================================================================
# 비상경로 — 1회용 복구코드 게시 (NCP 전용) — 20260724 트랙2 5단계
#   사장님 결재(2026-07-24): "사장님 폰이 막혔을 때 쓸 1회용·감사로그·사후통지 봉인 복구코드"
#
# ■ 이 스크립트가 존재하는 이유
#   자동업데이트 게시는 GitHub environment reviewer(사장님 폰 1탭 승인)로 게이트된다.
#   사장님 폰이 막히면(분실·MFA 재발급·GitHub 장애) 게시가 영구 차단된다(부승인자 없는 1-of-1 SPOF).
#   이 스크립트는 그 상황에서만 쓰는 1회용 복구코드로 게시를 통과시킨다.
#
# ■ 뒷문이 되지 않게 하는 설계 (보안 상무 우려 흡수)
#   1. 코드 원본 저장 안 함 — 해시(SHA-512 crypt)만 저장. 유출돼도 원본 역산 불가(#22).
#   2. 1회용 — 성공 시 해시 파일 즉시 삭제(재사용 물리 차단).
#   3. 접근 게이트 — NCP root 만 실행 가능. 러너·토큰·자식계정 접근 0.
#   4. 서명·다운그레이드 게이트 유지 — 복구코드는 'GitHub 폰승인만' 생략. 서명(NCP 개인키)·
#      버전 단조증가·자기검증 롤백은 publish-update.sh 그대로 통과(뒷문/비상경로 가르는 핵심선).
#   5. 사유 강제 + 감사로그 + 사후통지 — 비상 사용에 반드시 이유·기록·통지.
#
# ■ 두 가지 모드
#   setup  : 복구코드 새로 생성 → 해시만 저장 → 원본을 화면에 1회 출력(사장님이 안전보관). 원본 미저장.
#   use    : 저장된 해시로 입력 코드 검증 → 통과 시 publish-update.sh 게시 → 해시 삭제(1회용).
#
# 헌법 정합: #22(비밀 최소·해시만) #29(root 만) #33(사장님 결재) #40(비밀공유 대신 게이트)
# =============================================================================
set -euo pipefail

log()  { printf '[emergency] %s\n' "$*"; }
ok()   { printf '[emergency] ✅ %s\n' "$*"; }
warn() { printf '[emergency] ⚠️  %s\n' "$*" >&2; }
die()  { printf '[emergency] 🔴 %s\n' "$*" >&2; exit 1; }

# ── 상수 ──────────────────────────────────────────────────────────────────────
HASH_FILE="/var/hitpan/update-keys/emergency-recovery.hash"   # 해시만(원본 아님). 600 root.
AUDIT_LOG="/var/log/hitpan/emergency-publish.log"             # append-only 감사로그
PUBLISH="/opt/hitpan/deploy/publish-run.sh"                   # 실제 게시(서명·다운그레이드 게이트 통과)
PUBLISH_FALLBACK="/opt/hitpan/installer/updates/publish-update.sh"  # 래퍼 없이 직접(비상 시)

[[ $EUID -eq 0 ]] || die "root 로만 실행됩니다(sudo). 비상경로는 NCP root 전용(#29)."
command -v openssl >/dev/null 2>&1 || die "openssl 이 없습니다."
mkdir -p "$(dirname "$AUDIT_LOG")" 2>/dev/null || true
mkdir -p "$(dirname "$HASH_FILE")" 2>/dev/null || true   # 해시 디렉토리 없으면 생성(가용성)

# 감사로그 append-only 강제(검증팀 지적): root 도 지우지 못하게 chattr +a.
#   파일 없으면 만들고 +a. 지원 안 되는 FS(tmpfs 등)면 조용히 넘어가되 >> 는 유지.
if [[ ! -f "$AUDIT_LOG" ]]; then : > "$AUDIT_LOG" 2>/dev/null || true; chmod 600 "$AUDIT_LOG" 2>/dev/null || true; fi
chattr +a "$AUDIT_LOG" 2>/dev/null || true   # append-only. rm·truncate 차단(root 포함). 실패해도 로깅은 진행.

MODE="${1:-}"

case "$MODE" in
  # ── setup: 복구코드 생성 → 해시 저장 → 원본 1회 출력 ──────────────────────────
  setup)
    if [[ -f "$HASH_FILE" ]]; then
      warn "이미 복구코드 해시가 있습니다($HASH_FILE)."
      warn "새로 만들면 기존 코드는 무효가 됩니다. 계속하려면: $0 setup --force"
      [[ "${2:-}" == "--force" ]] || die "중단(기존 코드 보존)."
    fi
    # 암호학적 랜덤 복구코드: 그룹5-5-5-5 (사람이 받아적기 쉬운 형태), 대문자+숫자
    RAW="$(openssl rand -base64 24 | tr -dc 'A-Z0-9' | head -c 20)"
    CODE="${RAW:0:5}-${RAW:5:5}-${RAW:10:5}-${RAW:15:5}"
    # 해시만 저장(SHA-512 crypt, salt 자동). 원본은 저장 안 함.
    HASH="$(openssl passwd -6 "$CODE")"
    umask 077
    printf '%s\n' "$HASH" > "$HASH_FILE"
    chmod 600 "$HASH_FILE"
    chown root:root "$HASH_FILE"
    echo ""
    echo "════════════════════════════════════════════════════════════"
    echo "  🔑 비상 복구코드 (이 화면에서만 보입니다 — 안전한 곳에 적으세요)"
    echo ""
    echo "        $CODE"
    echo ""
    echo "  · 폰 메모·금고 등 안전한 곳에 보관(사진 X, 스크린샷 X 권장)"
    echo "  · 이 코드는 사장님 폰 승인이 막혔을 때 단 1회 게시에만 사용"
    echo "  · 한 번 쓰면 자동 무효화(1회용). 쓴 뒤 재발급하려면 setup --force"
    echo "  · 원본은 서버에 저장되지 않음(해시만). 잃어버리면 재발급만 가능"
    echo "════════════════════════════════════════════════════════════"
    log "감사로그: 복구코드 생성됨(해시 저장, 원본 미저장)"
    # force 재발급(기존 코드 교체)과 최초 생성을 구분해 기록(검증팀 지적 — 오용 추적).
    SETUP_KIND="initial"; [[ "${2:-}" == "--force" ]] && SETUP_KIND="force-replaced"
    printf '%s\tSETUP\tby=%s\tcode-generated(hash-only)\tkind=%s\n' \
      "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "${SUDO_USER:-root}" "$SETUP_KIND" >> "$AUDIT_LOG"
    ;;

  # ── use: 코드 검증 → 게시 → 해시 삭제(1회용) ─────────────────────────────────
  use)
    [[ -f "$HASH_FILE" ]] || die "복구코드가 설정돼 있지 않습니다. 먼저 '$0 setup' 실행."
    ZIP="${2:-}"; MANIFEST="${3:-}"; REASON="${4:-}"
    [[ -n "$ZIP" && -n "$MANIFEST" ]] || die "사용법: $0 use <zip> <manifest> \"<사유>\""
    [[ -n "$REASON" ]] || die "비상 사용 사유를 반드시 입력하세요(봉인 조건). 예: \"폰 분실, MFA 재발급 중\""

    # 코드 입력(화면 미표시)
    read -r -s -p "비상 복구코드 입력: " ENTERED; echo ""
    [[ -n "$ENTERED" ]] || die "코드가 비었습니다."

    # 검증: 저장 해시 전체를 salt 로 넘겨 재해시 → 동일하면 일치.
    #   1차 python crypt(stored 에서 salt·라운드 자동추출). 2차 fallback openssl(python crypt 제거 대비).
    #   ★ crypt 모듈은 python 3.13 에서 제거 예정 → 실패 시 openssl 로 폴백(NCP 업글 내구성).
    STORED="$(cat "$HASH_FILE")"
    CHECK="$(ENTERED="$ENTERED" STORED="$STORED" python3 -W ignore -c '
import crypt, os
print(crypt.crypt(os.environ["ENTERED"], os.environ["STORED"]))
' 2>/dev/null || true)"
    # 폴백: python crypt 미가용 시 openssl. salt 는 $6$<salt>$ 에서 추출하되 -salt 대신 stdin 안전전달 불가라
    #   해시앞부분($6$salt$)을 그대로 재사용하는 openssl passwd 표준 호출.
    if [[ -z "$CHECK" ]]; then
      SALT_ONLY="$(printf '%s' "$STORED" | sed -n 's/^\$6\$\([^$]*\)\$.*/\1/p')"
      if [[ -n "$SALT_ONLY" ]]; then
        CHECK="$(printf '%s' "$ENTERED" | openssl passwd -6 -salt "$SALT_ONLY" -stdin 2>/dev/null || true)"
      fi
    fi
    if [[ -z "$CHECK" || "$CHECK" != "$STORED" ]]; then
      printf '%s\tUSE-FAIL\tby=%s\treason=%s\tbad-code\n' \
        "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "${SUDO_USER:-root}" "$REASON" >> "$AUDIT_LOG"
      die "복구코드 불일치. 게시 거부(감사로그 기록됨)."
    fi
    ok "복구코드 확인됨"

    # 감사로그(게시 전)
    printf '%s\tUSE-START\tby=%s\treason=%s\tzip=%s\n' \
      "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "${SUDO_USER:-root}" "$REASON" "$(basename "$ZIP")" >> "$AUDIT_LOG"

    # 게시 — 서명·다운그레이드·자기검증 게이트는 그대로 통과(GitHub 폰승인만 생략).
    #   ★ 이 경로는 wrapper(publish-run.sh)의 B-4 독립재조회를 통째로 우회하고(비상=GitHub 미가용 전제)
    #     publish-update.sh 를 직접 부른다. 그래도 서명(NCP 개인키)·다운그레이드·자기검증 롤백은 100% 유지.
    #     (HITPAN_B4_ALLOW_UNVERIFIED 는 wrapper 전용 env 라 여기선 무효 → 넘기지 않는다. 검증팀 지적)
    #   ★ 1회용 봉인 급소(검증팀 P0): set -e 하에서 publish 가 비영점이면 그 줄에서 스크립트가 즉사해
    #     RC 포착·소진·실패로그가 전부 스킵된다. → set +e 로 감싸 rc 를 반드시 포착한다.
    log "비상 게시 실행(서명·버전방어 유지)..."
    [[ -f "$PUBLISH_FALLBACK" ]] || die "게시 스크립트 없음: $PUBLISH_FALLBACK"
    set +e
    bash "$PUBLISH_FALLBACK" --zip "$ZIP" --manifest "$MANIFEST"
    RC=$?
    set -e

    if [[ $RC -eq 0 ]]; then
      # 1회용: 성공 시 해시 삭제(재사용 물리 차단). rm 실패도 fail-closed 처리.
      if rm -f "$HASH_FILE"; then :; else
        # 삭제 실패 = 코드가 살아남을 위험 → 파일을 비워서라도 무효화(재사용 물리 차단)
        : > "$HASH_FILE" 2>/dev/null || true
        warn "🔴 해시파일 삭제 실패 — 비워서 무효화 시도. 수동으로 $HASH_FILE 삭제 확인 요망."
      fi
      ok "게시 성공 → 복구코드 무효화(1회용 소진). 재사용하려면 setup 재실행."
      printf '%s\tUSE-OK\tby=%s\treason=%s\tzip=%s\tcode-consumed\n' \
        "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "${SUDO_USER:-root}" "$REASON" "$(basename "$ZIP")" >> "$AUDIT_LOG"
      # 사후통지(신규 채널 신설 금지 — 기존 알림 경로 있으면 여기에 훅. 없으면 로그가 통지 대체)
      warn "사후통지: 비상경로로 게시됨(사유: $REASON). 정상화 후 GitHub 승인경로 복구 확인 요망."
    else
      # ★ 검증팀 지적: publish 가 manifest 반영 후 자기검증서 실패(rc≠0)했을 수도 있다.
      #   그 경우 게시는 이미 나갔으나 소진 안 되면 재사용 위험 → 실패여도 코드를 소진(안전측)한다.
      #   비상경로는 "1회 쓰면 무효"가 봉인 원칙이므로, 시도 자체를 소진으로 본다.
      : > "$HASH_FILE" 2>/dev/null || rm -f "$HASH_FILE" 2>/dev/null || true
      printf '%s\tUSE-PUBLISH-FAIL\tby=%s\treason=%s\trc=%s\tcode-consumed(safety)\n' \
        "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "${SUDO_USER:-root}" "$REASON" "$RC" >> "$AUDIT_LOG"
      die "게시 실패(rc=$RC) — 안전측으로 복구코드 소진(재사용 차단). 게시 반영 여부는 서빙 manifest·publish 로그로 확인 후, 필요시 setup 재발급."
    fi
    ;;

  status)
    if [[ -f "$HASH_FILE" ]]; then
      ok "복구코드 설정됨(해시 존재). 미사용 상태."
    else
      warn "복구코드 미설정(또는 이미 소진됨). 필요 시 '$0 setup'."
    fi
    [[ -f "$AUDIT_LOG" ]] && { echo "--- 최근 감사로그 5행 ---"; tail -5 "$AUDIT_LOG"; }
    ;;

  *)
    cat >&2 <<EOF
비상경로 복구코드 (NCP root 전용):
  sudo $0 setup                              복구코드 새로 생성(해시만 저장, 원본 1회 출력)
  sudo $0 use <zip> <manifest> "<사유>"      폰 막힘 시 1회 게시(코드 입력 → 게시 → 소진)
  sudo $0 status                             설정 상태·감사로그 확인
EOF
    exit 1
    ;;
esac
