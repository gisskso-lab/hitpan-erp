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
#
# ■ publish-update.sh 가 이 파일을 source 한다 (20260720작3 B-1 — 서명 규격 단일 출처)
#   서명 대상 문자열(payload)이 세 군데(BuildSigningPayload.cs / 여기 / publish)로 갈라지면
#   정상 릴리스가 전 고객 PC 에서 거부된다. 그래서 payload 조립·정규화·서명·검증을 아래
#   '재사용 함수'로 못박고, publish-update.sh 는 이 함수만 호출한다(자기 손으로 payload 를 다시 짜지 않음).
#   HITPAN_SIGN_MANIFEST_SOURCED=1 로 source 하면 아래 main 실행부는 건너뛴다(함수만 로드).
# =============================================================================
set -euo pipefail

# ── 재사용 함수 (publish-update.sh 가 source 로 가져다 쓴다 — B-1 단일 출처) ───────
# 서명 대상 문자열을 만든다. BuildSigningPayload(UpdateManifestSigning.cs)와 바이트 동일해야 한다.
#   개행은 LF('\n') 하나. 마지막 줄(migration) 뒤 개행 없음. BOM 없음.
#   인자: version channel url sha256 size migration  (channel·sha256 은 이 함수가 소문자화, migration 은 true|false 검증)
hitpan_build_signing_payload() {
  local _ver="$1" _ch="$2" _url="$3" _sha="$4" _size="$5" _mig="$6"
  local _ch_lc _sha_lc
  _ch_lc="$(printf '%s' "$_ch"  | tr '[:upper:]' '[:lower:]')"
  _sha_lc="$(printf '%s' "$_sha" | tr '[:upper:]' '[:lower:]')"
  case "$_mig" in
    true|false) : ;;
    *) echo "[sign] --migration 은 true 또는 false 여야 합니다 (받은 값: $_mig)" >&2; return 3 ;;
  esac
  printf 'hitpan-update-v1\nversion=%s\nchannel=%s\nurl=%s\nsha256=%s\nsize=%s\nmigration=%s' \
    "$_ver" "$_ch_lc" "$_url" "$_sha_lc" "$_size" "$_mig"
}

# payload 를 개인키로 서명해 한 줄 Base64(표준 '+/=')로 낸다. ECDSA P-256 / SHA-256 / DER.
#   인자: private_pem  version channel url sha256 size migration
hitpan_sign_payload() {
  local _priv="$1"; shift
  local _payload
  _payload="$(hitpan_build_signing_payload "$@")" || return $?
  printf '%s' "$_payload" | openssl dgst -sha256 -sign "$_priv" | openssl base64 -A
}

# 서빙되는 서명이 공개키로 검증되는지 확인한다(20260720작3 B-2 — 대상은 '규격 문자열'이지 파일 전체가 아님).
#   인자: public_pem signature_base64  version channel url sha256 size migration
#   반환: 0=검증통과 / 1=불일치·형식오류
hitpan_verify_payload() {
  local _pub="$1" _sig_b64="$2"; shift 2
  local _payload _tmpdir _rc
  _payload="$(hitpan_build_signing_payload "$@")" || return $?
  _tmpdir="$(mktemp -d)"
  # openssl base64 는 표준 Base64('+/=')만 받는다. sign-manifest 서명은 표준 Base64라 그대로 디코드된다.
  printf '%s' "$_sig_b64" | openssl base64 -d -A > "$_tmpdir/sig.bin" 2>/dev/null || { rm -rf "$_tmpdir"; return 1; }
  if printf '%s' "$_payload" | openssl dgst -sha256 -verify "$_pub" -signature "$_tmpdir/sig.bin" >/dev/null 2>&1; then
    _rc=0
  else
    _rc=1
  fi
  rm -rf "$_tmpdir"
  return $_rc
}

# source 로 로드만 하는 경우(publish-update.sh) 여기서 멈춘다 — 아래 CLI main 은 실행하지 않는다.
if [[ "${HITPAN_SIGN_MANIFEST_SOURCED:-0}" == "1" ]]; then
  return 0
fi
# ── 여기부터는 직접 실행(CLI) 시에만 도는 main ────────────────────────────────

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

# ── B-2 다층방어 (CTO 지적 봉합 2026-07-24): sudo 컨텍스트 개인키 경로 고정 ─────────
#   publish-update.sh 는 이 파일을 source(함수만)라 위 CLI 를 안 밟지만, 공격자가
#   `sudo bash sign-manifest.sh --private /tmp/공격키.pem ...` 로 이 CLI 를 직접 부르면
#   임의 개인키로 서명(서명 오라클)이 가능하다. 실봉인 1차 = sudoers 가 이 파일을 화이트리스트에서
#   제외(publish-update.sh 만 허용)하는 것이고, 이 가드는 그 위의 2차 방어다.
#   sudo 로 승격 실행(SUDO_UID/SUDO_USER 존재)될 때는 개인키가 반드시 NCP 표준 경로여야 한다.
STD_PRIVATE="/var/hitpan/update-keys/update_private.pem"
if [[ -n "${SUDO_UID:-}${SUDO_USER:-}" && "$PRIVATE" != "$STD_PRIVATE" ]]; then
  echo "[sign] 🔴 sudo 실행 시 개인키는 표준 경로($STD_PRIVATE)여야 합니다. 받은 값: $PRIVATE (서명 오라클 방어 B-2). 임의 개인키 서명 차단." >&2
  exit 4
fi

[[ -f "$PRIVATE" ]] || { echo "개인키 파일이 없습니다: $PRIVATE" >&2; exit 2; }

# --- 서명 대상 문자열 + 서명 (재사용 함수 = 단일 출처, B-1) --------------------
#   payload 조립·정규화(channel/sha256 소문자, migration 검증)는 위 hitpan_build_signing_payload 가 담당.
#   확인용으로 payload 도 함께 만들어 화면에 보여준다(서명한 문자열을 사장님이 눈으로 확인).
PAYLOAD="$(hitpan_build_signing_payload "$VERSION" "$CHANNEL" "$URL" "$SHA256" "$SIZE" "$MIGRATION")" \
  || { echo "payload 조립 실패" >&2; exit 3; }
SIGNATURE="$(hitpan_sign_payload "$PRIVATE" "$VERSION" "$CHANNEL" "$URL" "$SHA256" "$SIZE" "$MIGRATION")" \
  || { echo "서명 실패" >&2; exit 3; }

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
