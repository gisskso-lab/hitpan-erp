#!/usr/bin/env bash
# =============================================================================
# 웹 CD 래퍼 (NCP 전용) — 백오피스 API·Web·랜딩 배포를 이 스크립트 하나에 가둔다
#   작업지시서 20260724작3 B-1 / 근본틀 설계 §1.3 비특권화
#
# ■ 이 스크립트가 존재하는 이유 (B-1 관통결함 봉합)
#   기존 deploy-ncp.yml 은 GitHub 러너에서 SSH 로 광역 sudo(sudo rsync·sudo systemctl·
#   sudo cp·sudo tee·sudo mkdir)를 직접 실행했다. 그 결과 웹 CD 의 SSH 키만 있으면
#   NCP 에서 무엇이든 root 로 할 수 있어, 같은 키를 공유하는 UPDATE CD 가 아무리 권한을
#   좁혀도 웹 CD 통로로 개인키(/var/hitpan/update-keys/)를 읽어낼 수 있었다(헌법 #22 위반).
#   → 그 광역 sudo 를 전부 이 래퍼 안으로 흡수하고, sudoers 는 이 파일 1개(인자 0개)만
#     NOPASSWD 로 허용한다. "배포 계정은 무엇을 실행할지 못 고른다"를 실제로 성립시킨다.
#
# ■ 러너가 넘기는 것 = 데이터(publish 산출물)뿐. 실행 로직은 전부 여기.
#   러너: rsync 로 publish 산출물을 /var/hitpan/staging/web/{api,web,landing}/ 에 올림(데이터).
#   러너: RELEASE_VERSION 을 env 로 전달(문자열, 아래 정규식 검증).
#   러너: sudo /opt/hitpan/deploy/deploy-web.sh (인자 0개).
#   이 스크립트: 백업 → /opt 이동(exclude 시크릿) → drop-in 버전 → 재기동 → 헬스체크 → 실패 시 롤백.
#
# ■ 절대 원칙 (수정 시 위반 금지)
#   1. 인자 0개 강제. 경로·서비스명·EXCLUDES 전부 상수(주입 차단, B-8).
#   2. RELEASE_VERSION 은 env 이며 ^[A-Za-z0-9._-]+$ 만 허용(drop-in 명령주입 차단).
#   3. 롤백 rsync 에도 EXCLUDES 적용(appsettings 시크릿 회귀 차단, B-9).
#   4. 배포 전 free-space 프리플라이트(디스크 full 사고 차단, B-16).
#   5. flock 직렬화(동시 배포 경합 차단).
#
# 헌법 정합: #15(침묵 금지) #19 #22(개인키 격리·시크릿 보존) #29(사람 결재 1회) #34 #39(free-space)
# =============================================================================
set -euo pipefail

log()  { printf '[deploy-web] %s\n' "$*"; }
ok()   { printf '[deploy-web] ✅ %s\n' "$*"; }
warn() { printf '[deploy-web] ⚠️  %s\n' "$*" >&2; }
die()  { printf '[deploy-web] 🔴 %s\n' "$*" >&2; exit 1; }

# ── 0) 인자 0개 강제 (B-8 주입 차단) ──────────────────────────────────────────
#   러너가 임의 인자를 붙여 동작을 바꾸지 못하게 한다. 값은 전부 아래 상수/ENV 로만.
[[ $# -eq 0 ]] || die "인자를 받지 않습니다(받은 개수: $#). 값 전달은 RELEASE_VERSION env 로만. (B-8 주입 차단)"

# ── 상수 (경로·서비스명·EXCLUDES — 전부 하드코딩, 러너가 못 바꿈) ───────────────
STAGING="/var/hitpan/staging/web"          # 러너가 데이터만 올리는 스테이징(경계 A)
OPT="/opt/hitpan"                          # 서비스 WorkingDirectory 정합 경로
BACKUP_KEEP=5                              # -bak 세대 N개만 유지(B-16 디스크)
MIN_FREE_MB=2048                           # 배포 전 최소 여유공간(B-16 프리플라이트)
LOCK="/var/hitpan/deploy/deploy-web.lock"

# 배포 3종: 스테이징 하위 디렉토리 → /opt 대상 → 서비스명
#   (bash 3.x 호환 위해 배열 3개 인덱스 정렬)
COMPONENTS=(api web landing)
OPT_DIRS=(backoffice-api backoffice-web landing)
SERVICES=(hitpan-backoffice-api hitpan-backoffice-web hitpan-landing)

# 운영 시크릿 — 절대 덮어쓰지 않는다(rsync·롤백 양쪽 exclude, B-9)
EXCLUDES=(--exclude='appsettings.Production.json' --exclude='appsettings.Development.json')

# ── 롤백 함수 (헬스체크/정합 실패 시 — 최신 -bak 으로 복원, exclude 유지 B-9) ────
#   함수는 호출보다 먼저 정의돼야 한다(§7 정합게이트·§8 헬스체크 양쪽에서 부름).
rollback() {
  warn "실패 감지 — 자동 롤백"
  # ★ 검증팀 HIGH(롤백 백스톱 소실): 롤백은 '최선 노력'으로 끝까지 돌아야 한다.
  #   set -euo pipefail 하에서 rsync·systemctl 하나가 비영점이면 롤백이 중간에 죽어
  #   '망가진 채 방치'(헌법 #39 위반)가 된다. 각 단계 실패를 삼키되 반드시 로깅하고,
  #   복원 실패 컴포넌트를 집계해 끝에 경보한다(조용한 실패 금지, #15).
  local i d latest svc rb_fail=0 restored=0 no_bak=0
  for i in "${!OPT_DIRS[@]}"; do
    d="${OPT_DIRS[$i]}"
    latest=$(ls -td "$OPT/$d"-bak-* 2>/dev/null | head -1 || true)
    if [[ -n "$latest" ]]; then
      if rsync -a --delete "${EXCLUDES[@]}" "$latest/" "$OPT/$d/"; then
        log "롤백: $latest → $OPT/$d/"; restored=$((restored+1))
      else
        warn "🔴 롤백 rsync 실패: $d (수동 복원 필요: $latest)"; rb_fail=$((rb_fail+1))
      fi
    else
      warn "⚠️ $d 백업본 없음 — 롤백 대상 없음(최초 배포였을 수 있음)."; no_bak=$((no_bak+1))
    fi
  done
  for svc in "${SERVICES[@]}"; do
    systemctl restart "$svc" || { warn "🔴 롤백 후 재기동 실패: $svc (수동 확인 필요)"; rb_fail=$((rb_fail+1)); }
  done
  if (( rb_fail )); then
    warn "🔴🔴 롤백 부분실패($rb_fail 건) — 일부 컴포넌트가 불완전 상태다. 즉시 수동 개입 필요(헌법 #39 방치 금지)."
  else
    warn "롤백 완료(복원 $restored·백업없음 $no_bak) — 진범 진단 후 재배포 필요."
  fi
}

# ── 1) 전제 확인 ──────────────────────────────────────────────────────────────
[[ $EUID -eq 0 ]] || die "root 로 실행돼야 합니다(sudo). 현재 EUID=$EUID."
for tool in rsync systemctl curl df; do
  command -v "$tool" >/dev/null 2>&1 || die "$tool 이(가) 없습니다."
done
[[ -d "$STAGING" ]] || die "스테이징이 없습니다: $STAGING (러너가 데이터를 올리지 않았습니다)."

mkdir -p "$(dirname "$LOCK")"
exec 9>"$LOCK" || die "락 파일을 열 수 없습니다: $LOCK"
flock -n 9 || die "다른 웹 배포가 진행 중입니다(락 $LOCK)."

# ── 2) RELEASE_VERSION 검증 (있으면 drop-in 반영, 없으면 경고만 — 기존 동작 보존) ──
#   기존 deploy-ncp.yml 은 미설정 시 배포 계속 + 경고였다(서버 기존 값 유지). 그 동작을 보존한다.
RELEASE_VERSION="${RELEASE_VERSION:-}"
if [[ -n "$RELEASE_VERSION" ]]; then
  [[ "$RELEASE_VERSION" =~ ^[A-Za-z0-9._-]+$ ]] \
    || die "RELEASE_VERSION 형식 오류('$RELEASE_VERSION') — 영숫자·.·_·- 만 허용(drop-in 명령주입 차단, B-8)."
fi

# ── 3) free-space 프리플라이트 (B-16 — 백업이 디스크를 채워 배포가 반쯤 죽는 사고 차단) ──
FREE_MB=$(df -Pm "$OPT" | awk 'NR==2{print $4}')
[[ -n "$FREE_MB" ]] || die "여유공간을 읽지 못했습니다($OPT)."
if (( FREE_MB < MIN_FREE_MB )); then
  die "여유공간 부족: ${FREE_MB}MB < 최소 ${MIN_FREE_MB}MB. 오래된 -bak 정리 후 재시도(B-16 디스크 96.8% 실측)."
fi
log "free-space 확인: ${FREE_MB}MB (>= ${MIN_FREE_MB}MB)"

# ── 4) 백업 (기존 /opt/hitpan/* → *-bak-<TS>) + 세대 정리 ──────────────────────
TS="$(date +%Y%m%d-%H%M%S)"
for i in "${!OPT_DIRS[@]}"; do
  d="${OPT_DIRS[$i]}"
  if [[ -d "$OPT/$d" ]]; then
    cp -r "$OPT/$d" "$OPT/$d-bak-$TS"
    log "백업: $OPT/$d → $OPT/$d-bak-$TS"
    # 세대 정리: 최신 BACKUP_KEEP 개만 남기고 삭제
    ls -td "$OPT/$d"-bak-* 2>/dev/null | tail -n +$((BACKUP_KEEP + 1)) | xargs -r rm -rf
  fi
done

# ── 5) 스테이징 → /opt 이동 (시크릿 exclude) + 재기동 ─────────────────────────
for i in "${!COMPONENTS[@]}"; do
  c="${COMPONENTS[$i]}"; d="${OPT_DIRS[$i]}"; svc="${SERVICES[$i]}"
  src="$STAGING/$c/"
  [[ -d "$src" ]] || die "스테이징 컴포넌트 없음: $src"
  rsync -a --delete "${EXCLUDES[@]}" "$src" "$OPT/$d/"
  log "배치: $src → $OPT/$d/ (시크릿 보존)"
done

# ── 6) RELEASE_VERSION drop-in (있을 때만 — appsettings 미편집, 환경변수 우선) ────
if [[ -n "$RELEASE_VERSION" ]]; then
  log "출하 버전 → $RELEASE_VERSION (랜딩·백오피스 동시)"
  for svc in hitpan-landing hitpan-backoffice-api; do
    mkdir -p "/etc/systemd/system/$svc.service.d"
    printf '[Service]\nEnvironment="Release__InstallerVersion=%s"\n' "$RELEASE_VERSION" \
      > "/etc/systemd/system/$svc.service.d/release-version.conf"
  done
  systemctl daemon-reload
fi

for svc in "${SERVICES[@]}"; do
  systemctl restart "$svc"
  log "재기동: $svc"
done

# ── 7) 출하 버전 정합 게이트 (drop-in 적용 시 — 두 경로 같은 값인지) ─────────────
if [[ -n "$RELEASE_VERSION" ]]; then
  land_v=$(systemctl show hitpan-landing -p Environment --value | tr ' ' '\n' | sed -n 's/^Release__InstallerVersion=//p')
  back_v=$(systemctl show hitpan-backoffice-api -p Environment --value | tr ' ' '\n' | sed -n 's/^Release__InstallerVersion=//p')
  log "랜딩=$land_v / 백오피스=$back_v / 기대=$RELEASE_VERSION"
  if [[ "$land_v" != "$RELEASE_VERSION" || "$back_v" != "$RELEASE_VERSION" ]]; then
    warn "출하 버전 불일치 — 롤백 진행"
    rollback
    die "출하 버전 불일치(랜딩=$land_v, 백오피스=$back_v). 두 경로가 다른 EXE 를 안내하게 되어 배포 중단·롤백."
  fi
fi

# ── 8) 헬스체크 (기존 deploy-ncp.yml 검사 항목 흡수) ──────────────────────────
sleep 8
hc_fail=0
check() {  # $1=설명 $2=url $3=허용정규식
  local code
  # 🔴 P0 봉합 (2026-08-02): 종전엔 `curl -fsS ... || code="000"` 였다.
  #   -f 는 HTTP 4xx/5xx 를 '오류'로 보고 종료코드 22 를 낸다.
  #   그러면 || 가 발동해 실제 응답코드(401·405)가 000 으로 덮어씌워진다.
  #   그런데 아래 기대값에는 401·403·405 가 '통과'로 들어 있다.
  #   ⇒ "401 을 통과로 인정한다"고 써놓고, 401 이 오면 000 으로 바꿔 실패시켰다.
  #      이 헬스체크는 구조적으로 절대 통과할 수 없었다.
  #
  #   실측 (2026-08-02, 서버에서 직접 재현):
  #     healthz  실제 401 → -f 때문에 000
  #     status   실제 401 → 000
  #     biz-no   실제 405 → 000
  #   7/23·8/02 배포가 전부 이 이유로 롤백됐다. 배포물 결함이 아니라 검사 결함이었다.
  #   (그 8초 전 로그에 "Now listening on: http://127.0.0.1:5258" 이 이미 찍혀 있었다)
  #
  #   봉합: -f 를 뺀다. 우리는 '응답 코드'를 알고 싶은 것이지 2xx 여부를 묻는 게 아니다.
  #   연결 자체가 안 되면 curl 이 실패하고 %{http_code} 도 000 이므로,
  #   "서버가 죽었다"는 여전히 000 으로 정확히 잡힌다 — 안전망은 그대로다.
  code=$(curl -sS -o /dev/null -w "%{http_code}" --max-time 15 "$2" 2>/dev/null) || code="000"
  [[ -n "$code" ]] || code="000"
  log "$1 → $code"
  echo "$code" | grep -qE "$3" || { warn "$1 실패(코드 $code)"; hc_fail=1; }
}
check "back /"                 "https://back.hitpan.kr/"                                   '^(200|301|302)$'
check "landing /"              "https://landing.hitpan.kr/"                                '^(200|301|302)$'
check "API healthz(nginx)"     "https://back.hitpan.kr/api/backoffice/credentials/healthz" '^(200|401|403)$'
check "API direct(local)"      "http://127.0.0.1:5258/api/backoffice/credentials/status"   '^(200|401|403)$'
check "biz-no route"           "https://back.hitpan.kr/api/landing/biz-no/verify"          '^(405|400|401|415)$'

if (( hc_fail )); then
  rollback
  die "헬스체크 실패 — 롤백 완료. 진범 진단 후 재배포."
fi

ok "웹 배포 완료 (백오피스 API·Web·랜딩 정상)."
