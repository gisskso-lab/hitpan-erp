#!/usr/bin/env bash
# =============================================================================
# 업데이트 manifest 서명 스크립트 (NCP 전용, 사장님 실행)
# 작업지시서 20260715작1 · 검증팀 SoD 반증 P0 봉합 (2026-07-16)
#
# 이 스크립트가 존재하는 이유:
#   업데이트 manifest 는 개인키(NCP /var/hitpan/update-keys/)로만 서명할 수 있고,
#   고객 PC 워치독은 EXE 내장 공개키로 그 서명을 검증한다.
#   서명을 만드는 쪽(여기)과 검증하는 쪽(UpdateSignatureVerifier.cs)이
#   '똑같은 문자열'에 서명·검증해야 한다 — 한 바이트라도 다르면 정상 manifest 가
#   전 고객 PC 에서 거부돼 업데이트가 전면 중단된다(검증팀이 실측으로 적발).
#
#   그래서 서명 대상 규격을 사람이 손으로 만들지 않고 이 스크립트 한 곳에 못박는다.
#   규격 원본 = src/HitPan.Watchdog/AutoUpdate/UpdateManifestSigning.cs BuildSigningPayload.
#   두 규격이 일치하는지는 UpdateSignatureTests(왕복 테스트)로 자동 고정돼 있다.
#
# 헌법 정합: #22(개인키 NCP 격리) · #29(서명=사람 결재 1회) · #23(5중 검증)
# =============================================================================
set -euo pipefail

PRIVATE=""
VERSION=""
CHANNEL=""
URL=""
SHA256=""
SIZE=""
MIGRATION=""
KID="upd-v1"   # 시리얼 키('v1')와 분리 — 업데이트 개인키 유출 = 전 고객 코드 실행이라 위력이 다르다

usage() {
  cat >&2 <<'EOF'
사용법:
  sudo bash sign-manifest.sh \
    --private /var/hitpan/update-keys/update_private.pem \
    --version 1.2.35 \
    --channel normal \
    --url https://updates.hitpan.kr/packages/hitpan-1.2.35.zip \
    --sha256 <zip sha256 소문자> \
    --size <zip 바이트수> \
    --migration false

출력: manifest.json 에 넣을 signature(Base64) 와 kid.
EOF
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --private)   PRIVATE="$2"; shift 2 ;;
    --version)   VERSION="$2"; shift 2 ;;
    --channel)   CHANNEL="$2"; shift 2 ;;
    --url)       URL="$2"; shift 2 ;;
    --sha256)    SHA256="$2"; shift 2 ;;
    --size)      SIZE="$2"; shift 2 ;;
    --migration) MIGRATION="$2"; shift 2 ;;
    --kid)       KID="$2"; shift 2 ;;
    *) echo "알 수 없는 인자: $1" >&2; usage ;;
  esac
done

[[ -z "$PRIVATE" || -z "$VERSION" || -z "$CHANNEL" || -z "$URL" || -z "$SHA256" || -z "$SIZE" || -z "$MIGRATION" ]] && usage
[[ -f "$PRIVATE" ]] || { echo "개인키 파일이 없습니다: $PRIVATE" >&2; exit 2; }

# --- 규격 정규화 (검증기와 동일해야 함) -------------------------------------
#   channel: 소문자 / sha256: 소문자 / migration: true|false 만 허용
CHANNEL_LC="$(printf '%s' "$CHANNEL" | tr '[:upper:]' '[:lower:]')"
SHA256_LC="$(printf '%s' "$SHA256"  | tr '[:upper:]' '[:lower:]')"
case "$MIGRATION" in
  true|false) : ;;
  *) echo "--migration 은 true 또는 false 여야 합니다 (받은 값: $MIGRATION)" >&2; exit 3 ;;
esac

# --- 서명 대상 문자열 (BuildSigningPayload 와 바이트 동일) ---------------------
#   개행은 반드시 LF('\n') 하나. 마지막 줄(migration) 뒤에는 개행 없음. BOM 없음.
#   printf 는 셸·로케일에 무관하게 정확한 바이트를 낸다(echo 와 달리 이스케이프가 예측 가능).
PAYLOAD="$(printf 'hitpan-update-v1\nversion=%s\nchannel=%s\nurl=%s\nsha256=%s\nsize=%s\nmigration=%s' \
  "$VERSION" "$CHANNEL_LC" "$URL" "$SHA256_LC" "$SIZE" "$MIGRATION")"

# --- 서명 (ECDSA P-256 / SHA-256) --------------------------------------------
#   openssl dgst -sign 은 DER(Rfc3279DerSequence) 서명을 낸다.
#   검증기(UpdateSignatureVerifier)가 DER·P1363 둘 다 받도록 봉합돼 있어 이대로 통과한다.
#   -A: 한 줄 Base64(줄바꿈 없음). 표준 Base64('+/=')이며 검증기가 이 형식을 받는다.
SIGNATURE="$(printf '%s' "$PAYLOAD" | openssl dgst -sha256 -sign "$PRIVATE" | openssl base64 -A)"

# --- 출력 --------------------------------------------------------------------
echo "──────────────────────────────────────────────────────────"
echo "서명 대상(확인용, 이 문자열에 서명했습니다):"
printf '%s\n' "$PAYLOAD" | sed 's/^/  /'
echo "──────────────────────────────────────────────────────────"
echo "manifest.json 에 아래 두 필드를 넣으십시오:"
echo ""
echo "  \"signature\": \"$SIGNATURE\","
echo "  \"kid\": \"$KID\""
echo "──────────────────────────────────────────────────────────"
