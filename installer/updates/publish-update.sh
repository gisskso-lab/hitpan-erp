#!/usr/bin/env bash
# =============================================================================
# 업데이트 배포 통합 스크립트 (NCP 전용, 사장님 sudo 1회 실행) — 20260720작3
#   대표 결재 2026-07-20 / CTO 부분수정후승인(B-1~3·N-1~3)
#
# ■ 이 스크립트가 존재하는 이유
#   워치독은 오직 https://updates.hitpan.kr/manifest.json 한 파일만 '최신'으로 본다
#   (UpdateClient.cs). 그 파일 교체가 수작업 4단(zip 배치·서명·서명값 손붙여넣기·복사)이라
#   한 단계만 빠뜨려도 전 고객이 옛 버전에 고정된다(실측: manifest 가 1.2.30 에 고정됨).
#   이 스크립트가 그 4단을 원자적으로 묶어 '항상 최신을 가리키게' 한다.
#
# ■ 하는 일 (사장님은 인자 2개만 준다)
#   sudo bash publish-update.sh --zip hitpan-1.2.34.zip --manifest manifest-1.2.34-unsigned.json
#     1) 입력 검증  — zip 실측 sha256 == manifest.sha256, zip 파일명 == downloadUrl 파일명
#     2) 다운그레이드 게이트 — 웹루트 현 manifest 보다 높은 버전만 허용(IsNewerVersion 동일 규칙, B-3)
#     3) zip 배치   — packages/{zip}
#     4) 서명       — sign-manifest.sh 재사용 함수로 서명(B-1 단일 출처), signature·kid 자동 삽입
#     5) 원자 교체  — tmp 에 완성본 쓰고 mv 로 manifest.json 교체(반쪽 서빙 0), 옛것은 .bak 백업
#     6) 자기검증   — curl 로 서빙본 재취득 → 버전·서명(공개키 verify, B-2)·zip 200 확인
#                     하나라도 실패 → .bak 로 자동 롤백(옛 manifest 유지 = 정지 아님)
#
# ■ PM 은 이 스크립트를 실행하지 않는다 (헌법 #29)
#   PM 산출 = 이 파일 + 절차문서. 실행은 사장님 NCP sudo 1회. 개인키는 NCP 밖으로 안 나간다(#22).
#   이 스크립트가 만지는 것: packages/{zip} 생성, {webroot}/manifest.json 교체, .bak 생성. 그뿐.
#
# 헌법 정합: #15(침묵 금지) #19 #22(개인키 NCP) #23 #29(사람 결재 1회) #34
# =============================================================================
set -euo pipefail

log()  { printf '[publish] %s\n' "$*"; }
ok()   { printf '[publish] ✅ %s\n' "$*"; }
warn() { printf '[publish] ⚠️  %s\n' "$*" >&2; }
die()  { printf '[publish] 🔴 %s\n' "$*" >&2; exit 1; }

# ── 인자 파싱 ────────────────────────────────────────────────────────────────
ZIP=""
MANIFEST=""
PRIVATE="/var/hitpan/update-keys/update_private.pem"
PUBLIC="/var/hitpan/update-keys/update_public.pem"   # 자기검증용 공개키(개인키 아님 = 유출돼도 무해, #22 무관)
KID="upd-v1"
WEBROOT="/var/www/updates"                            # manifest.json 이 서빙되는 웹루트 (실서빙 경로 = www-data 소유, 2026-07-24 A-3 NCP 실측 정정 / SSOT 상주본 일치)
PACKAGES_SUBDIR="packages"
FEED_URL="https://updates.hitpan.kr/manifest.json"    # 자기검증이 실제로 받아볼 서빙 주소
ALLOW_REPUBLISH=0                                      # 동일 버전 재배포(서명 갱신 등) 명시 허용
BACKUP_KEEP=10                                         # .bak 최근 N개만 유지(N-3)

usage() {
  cat >&2 <<'EOF'
사용법 (NCP, 사장님 sudo 1회):
  sudo bash publish-update.sh \
    --zip      hitpan-1.2.34.zip \
    --manifest manifest-1.2.34-unsigned.json

옵션(기본값 있음, 보통 안 줘도 됨):
  --private  개인키 경로     (기본 /var/hitpan/update-keys/update_private.pem)
  --public   공개키 경로     (기본 /var/hitpan/update-keys/update_public.pem)
  --kid      서명 키 id      (기본 upd-v1)
  --webroot  manifest 웹루트 (기본 /var/www/updates — 실서빙 경로, 2026-07-24 SSOT 정정)
  --feed-url 자기검증 주소   (기본 https://updates.hitpan.kr/manifest.json)
  --allow-republish  동일 버전 재배포 허용(다운그레이드 게이트 우회 — 서명 갱신 시에만)
EOF
  exit 1
}

OVERRIDE_SEEN=""    # B-2: sudo 컨텍스트에서 넘어오면 안 되는 오버라이드 인자 추적

while [[ $# -gt 0 ]]; do
  case "$1" in
    --zip)             ZIP="$2"; shift 2 ;;
    --manifest)        MANIFEST="$2"; shift 2 ;;
    --private)         PRIVATE="$2"; shift 2 ; OVERRIDE_SEEN="$OVERRIDE_SEEN --private" ;;
    --public)          PUBLIC="$2"; shift 2 ; OVERRIDE_SEEN="$OVERRIDE_SEEN --public" ;;
    --kid)             KID="$2"; shift 2 ; OVERRIDE_SEEN="$OVERRIDE_SEEN --kid" ;;
    --webroot)         WEBROOT="$2"; shift 2 ; OVERRIDE_SEEN="$OVERRIDE_SEEN --webroot" ;;
    --feed-url)        FEED_URL="$2"; shift 2 ; OVERRIDE_SEEN="$OVERRIDE_SEEN --feed-url" ;;
    --allow-republish) ALLOW_REPUBLISH=1; shift ; OVERRIDE_SEEN="$OVERRIDE_SEEN --allow-republish" ;;
    -h|--help)         usage ;;
    *) echo "알 수 없는 인자: $1" >&2; usage ;;
  esac
done

# ── B-2 다층방어: sudo 컨텍스트 오버라이드 차단 (in-script 가드) ─────────────────
#   근본: sudoers 화이트리스트가 --private /tmp/공격자키.pem 을 추가인자로 흘려도(와일드카드
#   탐욕매칭), 이 스크립트가 sudo 로 실행될 때는 개인키·웹루트·재배포 오버라이드를 절대 수용하지
#   않는다. sudoers(외부) + 이 가드(내부) = 서명오라클(V-1) 다층 봉인. (근본틀 설계 B-2)
#   SUDO_UID/SUDO_USER 존재 = sudo 로 승격 실행된 상태.
if [[ -n "${SUDO_UID:-}${SUDO_USER:-}" && -n "$OVERRIDE_SEEN" ]]; then
  die "sudo 실행 시 오버라이드 인자 금지(개인키·웹루트·재배포 대체 차단, 서명오라클 방어 B-2): 넘어온 인자 =$OVERRIDE_SEEN. --zip/--manifest 2인자만 허용."
fi

[[ -z "$ZIP" || -z "$MANIFEST" ]] && usage

# ── 필수 도구 확인 (없으면 조용히 실패하지 않게, 헌법 #15) ─────────────────────
for tool in jq openssl curl sha256sum; do
  command -v "$tool" >/dev/null 2>&1 || die "$tool 이(가) 없습니다. NCP 에 설치 후 다시 실행하세요."
done
[[ -f "$ZIP" ]]       || die "zip 파일이 없습니다: $ZIP"
[[ -f "$MANIFEST" ]]  || die "manifest 파일이 없습니다: $MANIFEST"
[[ -f "$PRIVATE" ]]   || die "개인키가 없습니다: $PRIVATE (헌법 #22 — NCP 600 root)"
[[ -f "$PUBLIC" ]]    || die "공개키가 없습니다: $PUBLIC (자기검증용 — 없으면 openssl 로 개인키에서 생성: openssl ec -in $PRIVATE -pubout -out $PUBLIC)"

PACKAGES_DIR="$WEBROOT/$PACKAGES_SUBDIR"
LIVE_MANIFEST="$WEBROOT/manifest.json"

# ── B-1 단일 출처: 서명 규격 함수를 sign-manifest.sh 에서 source ─────────────────
#   payload 조립·서명·검증을 여기서 다시 짜지 않는다(규격 3벌 갈라짐 차단).
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export HITPAN_SIGN_MANIFEST_SOURCED=1
# shellcheck source=sign-manifest.sh
source "$SCRIPT_DIR/sign-manifest.sh"

# ── N-2: 동시실행 직렬화(flock) — 두 번 돌면 mv·백업 경합 ───────────────────────
LOCK="/var/hitpan/update-keys/publish.lock"
exec 9>"$LOCK" || die "락 파일을 열 수 없습니다: $LOCK"
flock -n 9 || die "다른 배포가 진행 중입니다(락 $LOCK). 끝난 뒤 다시 실행하세요."

# ── 1) 입력 검증 ─────────────────────────────────────────────────────────────
log "manifest 읽는 중: $MANIFEST"
VERSION="$(jq -r '.version'           "$MANIFEST")"
CHANNEL="$(jq -r '.channel'           "$MANIFEST")"          # "Normal" 등 대문자 — 서명 함수가 소문자화(B-1)
URL="$(jq -r '.downloadUrl'           "$MANIFEST")"
SHA_MANIFEST="$(jq -r '.sha256'       "$MANIFEST")"
# sizeBytes 는 큰 정수 — jq 숫자(double) 손상 방지: 원문에서 문자열로 뽑는다(B-1).
SIZE="$(jq -r '.sizeBytes | tostring' "$MANIFEST")"
# requiresMigration 은 JSON boolean → payload 는 문자열 "true"/"false"(B-1).
MIGRATION="$(jq -r 'if .requiresMigration then "true" else "false" end' "$MANIFEST")"

for v in VERSION CHANNEL URL SHA_MANIFEST SIZE MIGRATION; do
  [[ -n "${!v}" && "${!v}" != "null" ]] || die "manifest 에 $v 값이 없습니다($MANIFEST) — build-manifest.ps1 산출물인지 확인."
done

# channel 정규화 가드 (검증팀 SoD P1 봉합 2026-07-20):
#   워치독은 manifest.channel 을 enum(UpdateManifest.Channel)으로 읽어 .ToString().ToLowerInvariant() 로
#   서명 규격을 만든다(UpdateManifestSigning.BuildSigningPayload). 즉 워치독이 보는 값은 항상
#   Emergency|Normal|Major 셋 중 하나의 '이름'이다. 만약 manifest 에 숫자("channel":1)나 오타가 오면,
#   여기서(sign·self-check) raw 문자열 "1" 로 서명해 self-check 는 통과하지만 워치독은 "normal" 로 재조립해
#   전 고객이 서명 불일치로 거부한다(정상 릴리스 전면중단). self-check 가 워치독 정규화를 안 밟는 사각지대라
#   여기서 입력을 워치독과 같은 이름집합으로 못박아 그 창구를 원천 차단한다.
case "$CHANNEL" in
  Emergency|Normal|Major) : ;;
  emergency|normal|major)
    warn "channel 이 소문자('$CHANNEL')입니다 — build-manifest.ps1 은 대문자로 방출합니다. 값은 유효하나 산출 경로를 확인하세요." ;;
  *) die "channel 값이 유효하지 않습니다('$CHANNEL'). Emergency|Normal|Major 중 하나여야 합니다(숫자·오타 불가 — 워치독 서명 규격 불일치로 전 고객 거부됨)." ;;
esac

log "버전=$VERSION 채널=$CHANNEL 마이그=$MIGRATION"

# zip 파일명 == downloadUrl 의 파일명 (잘못된 짝 배포 차단)
ZIP_BASENAME="$(basename "$ZIP")"
URL_BASENAME="$(basename "$URL")"
[[ "$ZIP_BASENAME" == "$URL_BASENAME" ]] \
  || die "zip 파일명($ZIP_BASENAME)과 manifest downloadUrl 파일명($URL_BASENAME)이 다릅니다."

# zip 실측 sha256 == manifest.sha256 (한 바이트라도 다르면 중단)
SHA_ACTUAL="$(sha256sum "$ZIP" | awk '{print tolower($1)}')"
SHA_EXPECT="$(printf '%s' "$SHA_MANIFEST" | tr '[:upper:]' '[:lower:]')"
[[ "$SHA_ACTUAL" == "$SHA_EXPECT" ]] \
  || die "zip sha256 불일치 — manifest($SHA_EXPECT) vs 실측($SHA_ACTUAL). 잘못된 짝입니다."
# 실측 크기도 manifest 와 일치하는지(부수 확인)
SIZE_ACTUAL="$(stat -c '%s' "$ZIP")"
[[ "$SIZE_ACTUAL" == "$SIZE" ]] \
  || warn "zip 크기($SIZE_ACTUAL) 가 manifest sizeBytes($SIZE)와 다릅니다 — 서명은 manifest 값 기준으로 진행."
ok "입력 검증 통과 (sha256·파일명 일치)"

# ── 2) 다운그레이드 게이트 — IsNewerVersion 동일 규칙(B-3) ───────────────────────
#   각 버전을 major.minor.build 3정수로 파싱(Revision 무시, 결측 0 보정), 정수 튜플 비교.
#   파싱 불능 시 중단(fail-closed). "1.2.34" vs "1.2.34.0" 은 같은 버전으로 판정(F-4 함정 차단).
parse_three() {   # echo "maj min build" 또는 실패 시 return 1
  local s="$1"
  [[ "$s" =~ ^([0-9]+)\.([0-9]+)(\.([0-9]+))?(\.[0-9]+)?$ ]] || return 1
  local maj="${BASH_REMATCH[1]}" min="${BASH_REMATCH[2]}" bld="${BASH_REMATCH[4]:-0}"
  printf '%s %s %s' "$maj" "$min" "$bld"
}
# 반환: 0 = a > b / 1 = a == b / 2 = a < b / 3 = 파싱불능
cmp_three() {
  local pa pb; pa="$(parse_three "$1")" || return 3; pb="$(parse_three "$2")" || return 3
  read -r a1 a2 a3 <<<"$pa"; read -r b1 b2 b3 <<<"$pb"
  for pair in "$a1 $b1" "$a2 $b2" "$a3 $b3"; do
    read -r x y <<<"$pair"
    (( x > y )) && return 0
    (( x < y )) && return 2
  done
  return 1
}

if [[ -f "$LIVE_MANIFEST" ]]; then
  LIVE_VERSION="$(jq -r '.version // empty' "$LIVE_MANIFEST" 2>/dev/null || true)"
  if [[ -n "$LIVE_VERSION" ]]; then
    set +e; cmp_three "$VERSION" "$LIVE_VERSION"; rc=$?; set -e
    case "$rc" in
      0) log "다운그레이드 게이트 통과: $LIVE_VERSION → $VERSION (상향)" ;;
      1) if [[ "$ALLOW_REPUBLISH" == "1" ]]; then
           warn "동일 버전 재배포($VERSION) — --allow-republish 로 허용됨(서명 갱신 등)."
         else
           die "이미 최신이 $LIVE_VERSION 입니다 — 같은 버전 재배포는 --allow-republish 를 명시하세요."
         fi ;;
      2) die "다운그레이드 차단 — 현 서빙($LIVE_VERSION) 보다 낮은 버전($VERSION)을 올리려 합니다." ;;
      3) die "버전 형식 해석 불가 — 현 '$LIVE_VERSION' vs 새 '$VERSION'. 안전측(중단)." ;;
    esac
  else
    warn "현 manifest.json 버전을 읽지 못했습니다 — 최초 배포로 간주하고 진행."
  fi
else
  log "현 manifest.json 없음 — 최초 배포로 진행."
fi

# ── 3) zip 배치 ──────────────────────────────────────────────────────────────
mkdir -p "$PACKAGES_DIR"
TARGET_ZIP="$PACKAGES_DIR/$ZIP_BASENAME"
if [[ -f "$TARGET_ZIP" ]]; then
  EXIST_SHA="$(sha256sum "$TARGET_ZIP" | awk '{print tolower($1)}')"
  if [[ "$EXIST_SHA" == "$SHA_ACTUAL" ]]; then
    log "zip 이 이미 배치돼 있고 sha256 동일 — 재복사 생략."
  else
    die "packages 에 같은 이름의 다른 zip 이 있습니다($TARGET_ZIP, sha 불일치). 손으로 확인 후 진행하세요."
  fi
else
  cp -f "$ZIP" "$TARGET_ZIP"
  ok "zip 배치 완료: $TARGET_ZIP"
fi

# ── 3-b) 설치 EXE 배치 (20260730작3 ①안 — 랜딩 다운로드 404 봉합) ────────────────
#   ■ 왜 여기인가
#     랜딩 DownloadPage 는 manifest 버전으로 {packages}/HitPan-ERP-Setup-{V}.exe 를 만든다.
#     그런데 EXE 를 그 경로에 올리는 일이 어떤 워크플로에도 없었다 →
#     1.2.29~1.2.41 전 버전이 404. 랜딩 다운로드가 한 번도 작동한 적이 없다(2026-07-30 실측).
#     zip 과 '같은 게시 단위'로 묶어야 다음 릴리스에서 또 빠지지 않는다(사장님 ①안).
#
#   ■ 경로는 인자가 아니라 publish-run.sh 가 export 한 HITPAN_INSTALLER_EXE 로 받는다.
#     래퍼의 --zip/--manifest 2인자 계약을 깨지 않기 위해서다(B-2 오버라이드 차단 유지).
#     단독 실행(사장님 수동)일 때는 이 변수가 없을 수 있으므로 그 경우만 건너뛴다.
#   ■ 🔴 미지정 시 '조용한 skip' 금지 (검증팀 [3-V] D2 BLOCKER 봉합 2026-07-30)
#     최초 구현은 변수 미설정 시 warn 만 찍고 넘어갔다. PM 가정 = "래퍼 경유면 항상 설정된다".
#     **그 가정이 틀렸다.** emergency-recovery.sh:132 가 래퍼를 우회해 이 스크립트를 직접 부른다
#     (비상경로 = GitHub 미가용 전제). 그 경로에서 manifest 는 새 버전으로 교체되는데 EXE 는 빠져
#     → **랜딩이 없는 EXE 링크를 만든다 = 오늘 봉합하려는 그 404 가 비상경로에 그대로 재현**된다.
#     게다가 비상 상황이라 아무도 즉시 알아채지 못한다.
#     ⇒ 미지정이면 게시를 **중단**한다. EXE 없이 manifest 만 올리는 경로를 남기지 않는다.
#     ⇒ EXE 없이 manifest 만 갱신해야 하는 정당한 사유가 있으면 HITPAN_ALLOW_NO_INSTALLER=1 을
#       명시해야 한다(의도를 손으로 적게 만든다 — 사고는 '기본값'에서 난다).
INSTALLER_EXE="${HITPAN_INSTALLER_EXE:-}"
if [[ -z "$INSTALLER_EXE" && "${HITPAN_ALLOW_NO_INSTALLER:-0}" != "1" ]]; then
  die "설치 EXE 가 지정되지 않았습니다(HITPAN_INSTALLER_EXE 미설정) — 게시 중단.
  zip 만 올리면 랜딩 다운로드가 404 가 된다(2026-07-30 P0 재현).
  · 정상 경로: publish-run.sh 래퍼가 자동 설정한다.
  · 비상 경로(emergency-recovery.sh): EXE 경로를 export 하고 부를 것.
  · manifest 만 갱신이 정말 의도라면: HITPAN_ALLOW_NO_INSTALLER=1 을 명시할 것."
fi

if [[ -n "$INSTALLER_EXE" ]]; then
  [[ -f "$INSTALLER_EXE" ]] || die "설치 EXE 가 없습니다: $INSTALLER_EXE"
  EXE_BASENAME="$(basename "$INSTALLER_EXE")"
  # 파일명 규칙 고정 — 랜딩이 만드는 이름과 정확히 같아야 404 가 안 난다.
  EXPECT_EXE_NAME="HitPan-ERP-Setup-$VERSION.exe"
  [[ "$EXE_BASENAME" == "$EXPECT_EXE_NAME" ]] \
    || die "EXE 파일명이 manifest 버전과 어긋납니다: $EXE_BASENAME (기대=$EXPECT_EXE_NAME). 랜딩이 404 를 받게 된다."

  TARGET_EXE="$PACKAGES_DIR/$EXE_BASENAME"
  EXE_SHA_NEW="$(sha256sum "$INSTALLER_EXE" | awk '{print tolower($1)}')"
  if [[ -f "$TARGET_EXE" ]]; then
    EXE_SHA_OLD="$(sha256sum "$TARGET_EXE" | awk '{print tolower($1)}')"
    if [[ "$EXE_SHA_OLD" == "$EXE_SHA_NEW" ]]; then
      log "EXE 가 이미 배치돼 있고 sha256 동일 — 재복사 생략."
    else
      die "packages 에 같은 이름의 다른 EXE 가 있습니다($TARGET_EXE, sha 불일치). 손으로 확인 후 진행하세요."
    fi
  else
    # 원자 배치: 임시명으로 복사 후 mv — 260MB 복사 도중 고객이 반쪽 파일을 받는 것 차단.
    TMP_EXE="$PACKAGES_DIR/.${EXE_BASENAME}.tmp.$$"
    cp -f "$INSTALLER_EXE" "$TMP_EXE" || die "EXE 복사 실패: $INSTALLER_EXE"
    mv -f "$TMP_EXE" "$TARGET_EXE"    || die "EXE 원자 교체 실패: $TARGET_EXE"
    ok "설치 EXE 배치 완료: $TARGET_EXE ($(stat -c%s "$TARGET_EXE") bytes)"
  fi
  chmod 0644 "$TARGET_EXE" 2>/dev/null || true
else
  # 여기 오는 유일한 경우 = HITPAN_ALLOW_NO_INSTALLER=1 을 사람이 명시했을 때.
  warn "⚠️ EXE 배치를 의도적으로 건너뜁니다(HITPAN_ALLOW_NO_INSTALLER=1)."
  warn "   → 랜딩 다운로드(HitPan-ERP-Setup-$VERSION.exe)는 404 가 된다. 신규 고객 설치 불가."
  warn "   → manifest 만 갱신되므로 '기존 설치 고객의 자동업데이트'만 동작한다."
fi

# ── 4) 서명 + signature·kid 자동 삽입 (B-1 단일 출처) ────────────────────────────
log "서명 중(kid=$KID) — 규격 문자열은 sign-manifest.sh 함수가 만든다(B-1)"
SIGNATURE="$(hitpan_sign_payload "$PRIVATE" "$VERSION" "$CHANNEL" "$URL" "$SHA_EXPECT" "$SIZE" "$MIGRATION")" \
  || die "서명 실패 — 개인키·openssl 확인."
[[ -n "$SIGNATURE" ]] || die "서명 결과가 비었습니다."

# manifest 에 signature·kid 삽입(jq). JSON 필드 순서·BOM 은 서명 유효성과 무관(B-2 — 서명 대상은 파일이 아님).
TMP_MANIFEST="$WEBROOT/.manifest.json.tmp.$$"   # ⭐ 웹루트와 동일 파일시스템(N-2 — mv 원자성 보장)
trap 'rm -f "$TMP_MANIFEST"' EXIT
jq --arg sig "$SIGNATURE" --arg kid "$KID" '. + {signature:$sig, kid:$kid}' "$MANIFEST" > "$TMP_MANIFEST" \
  || die "manifest 에 서명 삽입 실패(jq)."
# 유효 JSON UTF-8 인지 확인(워치독이 파싱 가능해야 함). BOM 이 없어야 함.
jq -e . "$TMP_MANIFEST" >/dev/null || die "삽입 후 manifest 가 유효한 JSON 이 아닙니다."
ok "서명 삽입 완료(임시파일): $TMP_MANIFEST"

# ── 5) 원자 교체 + 백업 ──────────────────────────────────────────────────────
RELEASED_AT_SAFE="$(jq -r '.releasedAt // "unknown"' "$MANIFEST" | tr -c 'A-Za-z0-9._-' '_')"
if [[ -f "$LIVE_MANIFEST" ]]; then
  OLD_VER="$(jq -r '.version // "unknown"' "$LIVE_MANIFEST" 2>/dev/null || echo unknown)"
  BAK="$WEBROOT/manifest.json.bak-${OLD_VER}-${RELEASED_AT_SAFE}"
  cp -f "$LIVE_MANIFEST" "$BAK"
  log "옛 manifest 백업: $BAK (버전 $OLD_VER)"
fi
# ── B-15 불변식 (2파일 경계 — 수정 시 절대 위반 금지) ─────────────────────────
#   zip(packages/{zip})은 위 §3 에서 manifest 교체보다 '먼저' 배치된다. manifest 는 항상 '마지막'에
#   rename 한다. 이 순서라 워치독이 manifest 를 본 순간 zip 은 이미 자리에 있다(반쪽 게시 0).
#   워치독은 manifest.sha256 으로 zip 을 다시 해시검증하므로, 설령 zip 이 늦더라도 서명불일치가 아니라
#   '해시 대기'로 안전하게 떨어진다. → manifest rename 을 zip 배치보다 앞당기지 말 것.
mv -f "$TMP_MANIFEST" "$LIVE_MANIFEST"   # 원자 교체(같은 파일시스템) — 반쪽 서빙 0. 반드시 zip 배치 뒤.
trap - EXIT
ok "manifest.json 원자 교체 완료 → 버전 $VERSION"

# N-3: 백업 회전 — 최근 BACKUP_KEEP 개만 유지
#   ⚠️ 이건 manifest .bak(수 KB)만 회전한다. 패키지 본체(EXE 259MB·ZIP 98MB)는 아래에서 따로.
mapfile -t BAKS < <(ls -1t "$WEBROOT"/manifest.json.bak-* 2>/dev/null || true)
if (( ${#BAKS[@]} > BACKUP_KEEP )); then
  for old in "${BAKS[@]:$BACKUP_KEEP}"; do rm -f "$old"; log "오래된 백업 삭제: $(basename "$old")"; done
fi

# ── N-3b: 패키지 본체 회전 (2026-08-02 사장님 오더 — 레포 정본화) ───────────
#
#   *"설치실패로 불필요한 설치파일 빌드 업로드건은 삭제하고 수정본을 올리는 게 맞는데
#     왜 무지성으로 계속 업로드만 하는거야?"*  — 사장님, 2026-08-02
#
#   실측 이력:
#     · 레포본엔 패키지 회전이 0줄이었다. 게시할 때마다 357MB(EXE+ZIP)가 무조건 쌓였다.
#     · 7/29 디스크 100% 로 DB 가 죽자 서버에서 급히 손으로 회전 코드를 넣었는데
#       레포엔 반영하지 않았다 ⇒ 다음 배포가 레포본으로 덮으면 회전이 사라진다.
#     · 그 손수정 회전에는 '현행 버전 보호'가 없었다. 그래서 승인 메일이 가리키던
#       1.2.33 이 mtime 기준으로 지워졌고, 대리점이 다운로드 404 를 맞았다.
#       (docs/운영기록/20260802_사고기록_대리점_설치실패_승인메일_404.md)
#
#   ⇒ 여기서 두 가지를 동시에 정본화한다:
#      (1) 패키지 본체도 회전한다 — 무한 누적 차단
#      (2) 방금 게시한 버전($VERSION)은 KEEP 계산에서 아예 제외 = 절대 안 지워진다
#          "몇 개 남기냐"보다 "현역을 지우지 않는다"가 먼저다. 그게 404 의 교훈이다.
#
#   mtime 정렬을 쓰는 이유: 버전 문자열 정렬은 1.2.9 > 1.2.10 으로 오판한다.
PKG_KEEP="${HITPAN_PKG_KEEP:-3}"
rotate_packages() {
  local pattern="$1" label="$2"
  local -a files=()
  # 현행 버전이 이름에 들어간 파일은 목록에서 먼저 뺀다(보호).
  while IFS= read -r f; do
    [[ -n "$f" ]] || continue
    if [[ "$(basename "$f")" == *"$VERSION"* ]]; then
      log "$label 보호: $(basename "$f") (방금 게시한 현행 버전)"
      continue
    fi
    files+=("$f")
  done < <(ls -1t "$PACKAGES_DIR"/$pattern 2>/dev/null || true)

  (( ${#files[@]} > PKG_KEEP )) || return 0
  for old in "${files[@]:$PKG_KEEP}"; do
    rm -f "$old" && log "$label 회전 삭제: $(basename "$old")"
  done
}
if [[ -d "$PACKAGES_DIR" ]]; then
  rotate_packages 'HitPan-ERP-Setup-*.exe' 'EXE'
  rotate_packages 'hitpan-*.zip'           'ZIP'
  log "패키지 회전 완료(KEEP=$PKG_KEEP, 현행 $VERSION 보호) — 사용량: $(du -sh "$PACKAGES_DIR" 2>/dev/null | cut -f1)"
else
  warn "packages 경로 없음($PACKAGES_DIR) — 회전 건너뜀"
fi

# ── 6) 자기검증 (서빙본 재취득 → 롤백까지) ────────────────────────────────────
#   "돌려봤더니 됐다" 금지 — 실제 서빙되는 파일을 다시 받아 확인한다.
rollback() {
  warn "자기검증 실패 → 롤백 시작"
  if [[ -n "${BAK:-}" && -f "$BAK" ]]; then
    # B-13: 되돌릴 옛 manifest 의 kid 가 현 공개키(자기검증용)로 검증 가능한 서명인지 확인.
    #   옛 kid 가 이미 폐기(도달불가)됐다면, 되돌려도 워치독이 그 서명을 거부해 '깨진 채 방치'가 된다.
    #   그 경우 옛 버전으로 되돌리지 말고 manifest 를 제거(최신 없음 + 수동개입 신호)해 fail-closed.
    local _bak_sig _bak_ver _bak_ch _bak_url _bak_sha _bak_size _bak_mig
    _bak_sig="$(jq -r '.signature // empty' "$BAK" 2>/dev/null || true)"
    _bak_ver="$(jq -r '.version // empty'   "$BAK" 2>/dev/null || true)"
    _bak_ch="$(jq -r '.channel // empty'    "$BAK" 2>/dev/null || true)"
    _bak_url="$(jq -r '.downloadUrl // empty' "$BAK" 2>/dev/null || true)"
    _bak_sha="$(jq -r '.sha256 // empty'    "$BAK" 2>/dev/null || true)"
    _bak_size="$(jq -r '.sizeBytes | tostring' "$BAK" 2>/dev/null || true)"
    _bak_mig="$(jq -r 'if .requiresMigration then "true" else "false" end' "$BAK" 2>/dev/null || echo false)"
    if [[ -n "$_bak_sig" ]] && hitpan_verify_payload "$PUBLIC" "$_bak_sig" \
         "$_bak_ver" "$_bak_ch" "$_bak_url" "$_bak_sha" "$_bak_size" "$_bak_mig"; then
      cp -f "$BAK" "$LIVE_MANIFEST"
      warn "롤백 완료 — manifest.json 을 옛 버전($OLD_VER)으로 되돌렸습니다(서명 검증 통과, 정지 아님)."
    else
      warn "옛 manifest($OLD_VER) 서명이 현 공개키로 검증 안 됨(kid 도달불가·B-13) — 되돌려도 워치독 거부."
      rm -f "$LIVE_MANIFEST"
      die "롤백 대상 서명 도달불가(B-13). manifest 제거 = '최신 없음' + 수동개입 필요. 개인/공개키·kid 정합 확인 후 재게시."
    fi
  else
    warn "백업본이 없어 롤백 불가(최초 배포) — manifest.json 을 제거해 '최신 없음' 상태로 둡니다."
    rm -f "$LIVE_MANIFEST"
  fi
  die "배포 실패(자기검증 불통과). 원인 확인 후 재실행하세요."
}

log "자기검증 — 서빙되는 manifest 재취득(N-1 캐시 무력화)"
SELF="/tmp/hitpan-selfcheck-manifest.$$.json"
# N-1: 캐시 무력화 — no-cache 헤더 + 캐시버스터 쿼리. -fsS: 실패 시 비영점 종료.
if ! curl -fsS -H 'Cache-Control: no-cache' -H 'Pragma: no-cache' \
       "${FEED_URL}?_=${RELEASED_AT_SAFE}${RANDOM}" -o "$SELF" 2>/dev/null; then
  rm -f "$SELF"; rollback
fi

SELF_VER="$(jq -r '.version // empty'   "$SELF" 2>/dev/null || true)"
SELF_SIG="$(jq -r '.signature // empty' "$SELF" 2>/dev/null || true)"
SELF_KID="$(jq -r '.kid // empty'       "$SELF" 2>/dev/null || true)"
SELF_URL="$(jq -r '.downloadUrl // empty' "$SELF" 2>/dev/null || true)"

[[ "$SELF_VER" == "$VERSION" ]] || { warn "서빙 버전($SELF_VER)이 방금 올린 버전($VERSION)과 다릅니다(캐시·경로 확인)."; rm -f "$SELF"; rollback; }
[[ -n "$SELF_SIG" && -n "$SELF_KID" ]] || { warn "서빙 manifest 에 서명이 비었습니다."; rm -f "$SELF"; rollback; }

# B-2: 서명 검증 대상은 '규격 문자열'이지 파일 전체가 아니다 — 서빙본에서 규격 재조립 후 공개키 verify.
SELF_CH="$(jq -r '.channel' "$SELF")"
SELF_SHA="$(jq -r '.sha256' "$SELF")"
SELF_SIZE="$(jq -r '.sizeBytes | tostring' "$SELF")"
SELF_MIG="$(jq -r 'if .requiresMigration then "true" else "false" end' "$SELF")"
if ! hitpan_verify_payload "$PUBLIC" "$SELF_SIG" "$SELF_VER" "$SELF_CH" "$SELF_URL" "$SELF_SHA" "$SELF_SIZE" "$SELF_MIG"; then
  warn "서빙 manifest 서명이 공개키로 검증되지 않습니다 — 워치독도 거부할 상태."
  rm -f "$SELF"; rollback
fi

# zip 이 실제 200 으로 받아지는가 + 내용까지 sha 재확인(B-6 반쪽 게시 검출).
#   헤더만(-I) 확인하면 CF 엣지가 옛 zip 을 200 으로 돌려줘 위양성 성공이 날 수 있다.
#   캐시버스터+no-cache 로 실제 바이트를 받아 sha256 이 manifest 값과 같은지 재확인한다.
SELF_ZIP="/tmp/hitpan-selfcheck-zip.$$.bin"
if ! curl -fsS -H 'Cache-Control: no-cache' -H 'Pragma: no-cache' \
       "${SELF_URL}?_=${RELEASED_AT_SAFE}${RANDOM}" -o "$SELF_ZIP" 2>/dev/null; then
  warn "downloadUrl($SELF_URL) 이 200 으로 응답하지 않습니다(zip 접근 불가)."
  rm -f "$SELF" "$SELF_ZIP"; rollback
fi
SELF_ZIP_SHA="$(sha256sum "$SELF_ZIP" | awk '{print tolower($1)}')"
SELF_SHA_LC="$(printf '%s' "$SELF_SHA" | tr '[:upper:]' '[:lower:]')"
if [[ "$SELF_ZIP_SHA" != "$SELF_SHA_LC" ]]; then
  warn "서빙 zip 실측 sha($SELF_ZIP_SHA) ≠ manifest.sha256($SELF_SHA_LC) — 반쪽 게시·옛 zip 캐시 의심."
  rm -f "$SELF" "$SELF_ZIP"; rollback
fi
rm -f "$SELF" "$SELF_ZIP"

# 설치 EXE 도 실제로 받아지는가 (20260730작3 — 랜딩 다운로드 404 재발 차단)
#   zip 200 만 확인하고 끝냈던 것이 이번 P0 의 구조적 원인이다.
#   랜딩이 고객에게 주는 링크가 바로 이 URL 이므로, 여기서 200 이 아니면 게시를 성공이라 할 수 없다.
#   ⚠️ EXE 는 260MB 라 zip 처럼 전량 받아 sha 대조하지 않는다(게시 시간·디스크 낭비).
#      배치 직전 이미 sha 를 대조했고(3-b), 여기서는 '고객이 받을 수 있는가'만 본다.
if [[ -n "${INSTALLER_EXE:-}" ]]; then
  SELF_EXE_URL="${SELF_URL%/*}/HitPan-ERP-Setup-$VERSION.exe"
  SELF_EXE_CODE="$(curl -fsS -o /dev/null -w '%{http_code}' -r 0-0 \
                     -H 'Cache-Control: no-cache' -H 'Pragma: no-cache' \
                     "${SELF_EXE_URL}?_=${RELEASED_AT_SAFE}${RANDOM}" 2>/dev/null || echo 000)"
  case "$SELF_EXE_CODE" in
    200|206)
      ok "설치 EXE 도달 확인: $SELF_EXE_URL (HTTP $SELF_EXE_CODE)" ;;
    *)
      warn "설치 EXE 가 서빙되지 않습니다: $SELF_EXE_URL (HTTP $SELF_EXE_CODE) — 랜딩 다운로드가 404 가 된다."
      rollback ;;
  esac
fi

# ── 성공 ─────────────────────────────────────────────────────────────────────
ok  "배포 완료 — updates.hitpan.kr 이 이제 $VERSION 을 가리킵니다."
log "  manifest : $LIVE_MANIFEST (서명 유효, 공개키 검증 통과)"
log "  package  : $TARGET_ZIP (sha256 일치, 200 접근 가능)"
[[ -n "${INSTALLER_EXE:-}" ]] && log "  installer: $PACKAGES_DIR/HitPan-ERP-Setup-$VERSION.exe (랜딩 다운로드 대상, 200 확인)"
[[ -n "${BAK:-}" ]] && log "  backup   : $BAK"
log "워치독이 다음 점검 주기에 이 버전을 감지합니다(고객 손 0)."
