#!/usr/bin/env bash
# =============================================================================
# 자동업데이트 게시 래퍼 (NCP 전용) — publish-update.sh 를 좁힌 인자로만 호출
#   작업지시서 20260724작3 B-4 / 근본틀 설계 §0 관통결함·§1.3 비특권화
#
# ■ 이 스크립트가 존재하는 이유 (B-4 관통결함 급소 봉합)
#   deploy-update.yml 은 러너에서 SSH 로 publish-update.sh 를 바로 불렀다. 문제는
#   NCP 가 "러너가 보낸 값(commit·run_id·승인여부·버전)을 그대로 신뢰"한다는 것.
#   러너가 침해되면 그 값을 위조해 서명 게이트를 통과시킬 수 있다(서명 오라클).
#   → 서명 직전, NCP 안에서(러너가 아니라) 판정근거를 GitHub API·git 으로 독립 재조회한다.
#     "메타로 받은 값 ≠ 독립조회 값"이면 서명 전에 abort. 침해 러너가 위조해도 NCP 가 직접 본다.
#
# ■ 러너가 넘기는 것 (인자 2개 + 검증용 메타 env)
#   sudo /opt/hitpan/deploy/publish-run.sh --zip /tmp/hitpan-<VER>.zip --manifest /tmp/manifest-<VER>-unsigned.json
#   env(검증 대조용, 신뢰 안 함): HITPAN_EXPECT_SHA / HITPAN_GH_RUN_ID / HITPAN_GH_SHA / HITPAN_GH_REPO
#
# ■ 절대 원칙 (수정 시 위반 금지)
#   1. publish-update.sh 오버라이드 인자(--private/--webroot/--allow-republish 등) 절대 전달 안 함(B-2 짝).
#   2. publish-update.sh 는 절대경로 호출(PATH 미의존).
#   3. B-4 독립재조회 통과 후에만 publish 호출. 토큰 부재 시 = 조회불가 = 서명 거부(fail-closed).
#   4. staging 경로 접두 고정(/tmp 러너 전송본만).
#
# ■ 잔여(검증팀 SoD 명시 — 트랙2/정책에서 닫음)
#   H-2: (b) 조상 확인은 "main 히스토리에 merge된 적 있는 임의 커밋"을 다 통과시킨다(is-ancestor).
#        오래된 취약 커밋 dispatch 방어는 다운그레이드 게이트(publish-update.sh 버전비교)에 의존.
#        정확한 "승인된 릴리스 커밋만" 강제는 태그 서명·릴리스 정책(정식) 사안 → 사장님 결재 후.
#   B-14: staging 이 아직 /tmp (심링크·TOCTOU 소지). 설계 권고 = /var/hitpan/staging/<run_id>/(700)+chattr.
#        트랙2 NCP 경로 배치 사안. 현재 zip 은 읽기(sha·cp 소스)로만 쓰여 임의쓰기로 안 이어짐(저위험).
#
# 헌법 정합: #15 #22(개인키 격리) #27(전파지연≠실패) #29(사람 결재 1회) #34
# =============================================================================
set -euo pipefail

log()  { printf '[publish-run] %s\n' "$*"; }
ok()   { printf '[publish-run] ✅ %s\n' "$*"; }
warn() { printf '[publish-run] ⚠️  %s\n' "$*" >&2; }
die()  { printf '[publish-run] 🔴 %s\n' "$*" >&2; exit 1; }

# ── 상수 (전부 하드코딩 — 러너가 못 바꿈) ─────────────────────────────────────
PUBLISH="/opt/hitpan/installer/updates/publish-update.sh"   # 절대경로(PATH 미의존)
REPO_DIR="/opt/hitpan/repo"                                 # main 조상 확인용 로컬 미러(git)
MAIN_REF="origin/main"                                      # commit 조상 판정 기준
GH_TOKEN_FILE="/var/hitpan/update-keys/github-b4-token"     # B-4 GitHub API 토큰(600 root, 러너 미경유)
ALLOWED_ZIP_PREFIX="/tmp/hitpan-"                           # 러너 전송본 접두 고정
ALLOWED_MAN_PREFIX="/tmp/manifest-"
ALLOWED_EXE_PREFIX="/tmp/HitPan-ERP-Setup-"                 # 설치 EXE 접두 고정 (20260730작3 ①안)

# ── 인자 파싱 — --zip/--manifest 2개만 수용. 그 외 전부 거부(B-2 짝) ────────────
ZIP=""
MANIFEST=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --zip)      ZIP="$2"; shift 2 ;;
    --manifest) MANIFEST="$2"; shift 2 ;;
    *) die "허용되지 않은 인자: $1 (이 래퍼는 --zip/--manifest 만 받습니다. 오버라이드 차단 B-2)." ;;
  esac
done
[[ -n "$ZIP" && -n "$MANIFEST" ]] || die "사용법: publish-run.sh --zip <zip> --manifest <unsigned manifest>"

# 경로 접두 고정 — 러너 전송본(/tmp/hitpan-*, /tmp/manifest-*)만. 임의 경로 주입 차단.
[[ "$ZIP" == "$ALLOWED_ZIP_PREFIX"* ]]      || die "zip 경로가 허용 접두($ALLOWED_ZIP_PREFIX)로 시작하지 않습니다: $ZIP"
[[ "$MANIFEST" == "$ALLOWED_MAN_PREFIX"* ]] || die "manifest 경로가 허용 접두($ALLOWED_MAN_PREFIX)로 시작하지 않습니다: $MANIFEST"
[[ -f "$ZIP" ]]      || die "zip 파일이 없습니다: $ZIP"
[[ -f "$MANIFEST" ]] || die "manifest 파일이 없습니다: $MANIFEST"

for tool in jq sha256sum openssl; do
  command -v "$tool" >/dev/null 2>&1 || die "$tool 이(가) 없습니다."
done

# ── 설치 EXE 동반 검증 (20260730작3 ①안 — 랜딩 다운로드 404 봉합) ─────────────────
#   ■ 왜 인자로 안 받나 (급소)
#     래퍼가 --zip/--manifest 2인자만 받는 것은 sudoers 정확매칭 + 오버라이드 주입 차단(B-2)이다.
#     --exe 를 추가하면 그 신뢰경계가 넓어진다(사장님 ①안 선택 = 경계 무변경).
#     대신 zip 파일명에 이미 버전이 있으므로 EXE 경로를 '유도'한다 — 새 인자 0개.
#     경로가 /tmp/HitPan-ERP-Setup-<3자SemVer>.exe 로 고정이라 임의 경로 주입도 불가능하다.
#
#   ■ 왜 fail-closed 인가
#     EXE 가 없으면 게시를 중단한다. "zip 만 올라가고 EXE 는 빠지는" 상태가 바로 이번 P0
#     (랜딩이 1.2.29~1.2.41 전부 404 — 한 번도 작동한 적 없음)의 원인이다.
#     한쪽만 올라갈 수 있으면 같은 사고가 반드시 재발한다. 둘은 한 단위로 묶여야 한다.
ZIP_BASE="$(basename "$ZIP")"
EXE_VER="${ZIP_BASE#hitpan-}"; EXE_VER="${EXE_VER%.zip}"
[[ "$EXE_VER" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] \
  || die "zip 파일명에서 3자 SemVer 를 얻지 못했습니다(파일명=$ZIP_BASE). EXE 유도 불가."

EXE="${ALLOWED_EXE_PREFIX}${EXE_VER}.exe"
EXE_SHA_FILE="${EXE}.sha256"
[[ "$EXE" == "$ALLOWED_EXE_PREFIX"* ]] || die "EXE 경로 접두 위반: $EXE"   # 유도값 재확인(방어적)
[[ -f "$EXE" ]] \
  || die "설치 EXE 가 없습니다: $EXE — 게시 중단(fail-closed). zip 만 올리면 랜딩 다운로드가 404 가 된다."
[[ -f "$EXE_SHA_FILE" ]] \
  || die "EXE sha 파일이 없습니다: $EXE_SHA_FILE — 무결성 확인 불가로 게시 중단."

EXE_SHA_EXPECT="$(tr -d ' \t\r\n' < "$EXE_SHA_FILE" | tr 'A-F' 'a-f')"
EXE_SHA_ACTUAL="$(sha256sum "$EXE" | awk '{print tolower($1)}')"
[[ "$EXE_SHA_EXPECT" =~ ^[0-9a-f]{64}$ ]] || die "EXE sha 파일 형식이 sha256 이 아닙니다: $EXE_SHA_FILE"
[[ "$EXE_SHA_ACTUAL" == "$EXE_SHA_EXPECT" ]] \
  || die "EXE 실측 sha ≠ 러너 계산 sha (전송 손상 의심). 실측=$EXE_SHA_ACTUAL 계산=$EXE_SHA_EXPECT"
#   ⚠️ 한계 명시(정직): 이 대조는 '전송 손상'을 잡지, 훼손된 러너를 잡지 못한다(양쪽 다 러너발).
#      그 역할은 위 B-4 독립 재조회가 한다. EXE 는 같은 게시 단위 안에 있어 그 통과에 함께 묶이므로
#      EXE 단독 위조 경로는 열리지 않는다.
ok "설치 EXE 동반 확인: $EXE ($(stat -c%s "$EXE" 2>/dev/null || echo '?') bytes, sha 일치)"
export HITPAN_INSTALLER_EXE="$EXE"   # publish-update.sh 가 packages/ 에 함께 배치한다

# ── B-4 독립 재조회 (급소) — fail-closed 구조 (검증팀 SoD 봉합 2026-07-24) ─────────
#   ★ 검증팀 CRITICAL/HIGH 반증 반영: 이전 구조는 "메타가 있으면 검사, 없으면 통과"라
#     침해 러너가 HITPAN_GH_RUN_ID 를 unset 하면 (c) 를 통째로 건너뛰어 fail-OPEN 이었다.
#     또 (a) sha 대조는 러너 통제 3값(zip실측·manifest.sha·EXPECT_SHA)끼리라 순환논리 —
#     침해 러너의 위조를 원리적으로 못 잡는다(전송 변조만 잡음). 실제 방어는 (b)(c) 독립조회뿐.
#   → 원칙 뒤집음: 메타는 '반드시 있어야' 하고, 하나라도 없거나 조회 불가면 즉시 거부(fail-CLOSED).
#     (a) 는 전송무결성 보조로만 두고, 판정근거는 NCP 가 직접 조회한 (b) git + (c) GitHub API.
#
#   이행기(트랙2 git미러·토큰 미배치): HITPAN_B4_ALLOW_UNVERIFIED=1 를 '러너 밖'에서 명시할 때만
#   우회. deploy-update.yml 은 이 env 를 절대 안 넘긴다(러너 통제 차단). sudoers 도 이 env 를
#   전달 목록에서 제외해야 러너가 명령줄로 못 켠다(G-B SETENV 화이트리스트에서 배제).

# (a) sha 전송무결성 — 러너발 값끼리라 '보조'. 이걸로 침해 러너를 잡는다고 신뢰하지 않는다.
ACTUAL_SHA="$(sha256sum "$ZIP" | awk '{print tolower($1)}')"
MANIFEST_SHA="$(jq -r '.sha256 // ""' "$MANIFEST" | tr '[:upper:]' '[:lower:]')"
[[ -n "$MANIFEST_SHA" ]] || die "manifest 에 sha256 이 없습니다: $MANIFEST"
[[ "$ACTUAL_SHA" == "$MANIFEST_SHA" ]] \
  || die "zip 실측 sha ≠ manifest.sha256 (전송 변조 의심). 실측=$ACTUAL_SHA manifest=$MANIFEST_SHA"
# ★ 검증팀 2차: HITPAN_EXPECT_SHA 를 실제로 대조(죽은 변수 제거). 러너가 계산한 sha 와 NCP 실측이
#   어긋나면 전송 중 손상·불일치 신호. 침해 러너 방어는 (b)(c) 가 하고, 이건 전송무결성 보조.
if [[ -n "${HITPAN_EXPECT_SHA:-}" ]]; then
  EXPECT_SHA="$(printf '%s' "$HITPAN_EXPECT_SHA" | tr '[:upper:]' '[:lower:]')"
  [[ "$ACTUAL_SHA" == "$EXPECT_SHA" ]] \
    || die "zip 실측 sha ≠ 러너 계산 sha(HITPAN_EXPECT_SHA). 전송 손상 의심. 실측=$ACTUAL_SHA 계산=$EXPECT_SHA"
fi
ok "sha 전송무결성 대조 통과(보조): $ACTUAL_SHA"

# 이행기 우회 여부 먼저 판정(러너 밖에서만 켜짐). 켜졌으면 (b)(c) 필수강제를 완화하되 경고.
B4_BYPASS=0
if [[ "${HITPAN_B4_ALLOW_UNVERIFIED:-0}" == "1" ]]; then
  warn "⚠️ B-4 이행기 우회(HITPAN_B4_ALLOW_UNVERIFIED=1) — git미러·토큰 미배치 상한선. 트랙2 완료 시 제거. 이 경로는 침해 러너 방어가 축소됨."
  B4_BYPASS=1
fi

# (b) commit 이 main 조상인가 — 로컬 git 미러로 NCP 가 직접 확인 (필수, 우회 아니면)
#   ★ fetch 실패는 warn 아니라 die(H-3): 조회 실패 = 판정 불가 = 거부. stale 미러 판정 금지.
if (( ! B4_BYPASS )); then
  [[ -n "${HITPAN_GH_SHA:-}" ]] || die "B-4: HITPAN_GH_SHA 미제공 — commit 독립검증 불가. 서명 거부(fail-closed)."
  [[ -d "$REPO_DIR/.git" ]]    || die "B-4: git 미러($REPO_DIR) 미배치 — commit 조상 독립확인 불가. 서명 거부(트랙2 G-F 배치 후 재시도)."
  git -C "$REPO_DIR" fetch --quiet origin \
    || die "B-4: git fetch 실패 — 미러 최신화 불가로 조상 판정 신뢰 불가. 서명 거부(stale 판정 금지, H-3)."
  git -C "$REPO_DIR" merge-base --is-ancestor "$HITPAN_GH_SHA" "$MAIN_REF" 2>/dev/null \
    || die "B-4: commit $HITPAN_GH_SHA 이 $MAIN_REF 조상이 아님(독립 git 확인). 미승인 브랜치 게시 차단."
  ok "B-4(b): commit $HITPAN_GH_SHA 이 $MAIN_REF 조상임을 로컬 git 으로 독립확인"
fi

# (c) run_id·approval·head_sha 를 GitHub API 로 NCP 가 직접 조회 (필수, 우회 아니면)
#   ★ 이전엔 conclusion 을 읽고 버렸음(죽은 변수). 여기서 실제 비교로 게이트한다(H-1).
if (( ! B4_BYPASS )); then
  [[ -n "${HITPAN_GH_RUN_ID:-}" && -n "${HITPAN_GH_REPO:-}" ]] \
    || die "B-4: HITPAN_GH_RUN_ID/REPO 미제공 — run·approval 독립조회 불가. 서명 거부(fail-closed)."
  # ★ B-4 독립성 핵심: 토큰은 러너가 넘기지 않는다(env_keep 화이트리스트에도 없음).
  #   NCP 로컬 파일(600 root)에서만 읽는다 — 러너가 침해돼도 토큰을 못 위조·못 탈취한다.
  #   러너가 HITPAN_GH_TOKEN 을 억지로 넣어도, 여기서 로컬 파일 값으로 덮어써(신뢰경계 A) 무력화한다.
  if [[ -r "$GH_TOKEN_FILE" ]]; then
    HITPAN_GH_TOKEN="$(tr -d '\r\n' < "$GH_TOKEN_FILE")"
  fi
  [[ -n "${HITPAN_GH_TOKEN:-}" ]] \
    || die "B-4: GitHub 토큰($GH_TOKEN_FILE) 미배치·비어있음 — run·approval 독립조회 불가. 서명 거부(트랙2 G-F 배치 후)."
  API="https://api.github.com/repos/$HITPAN_GH_REPO/actions/runs/$HITPAN_GH_RUN_ID"
  RUN_JSON="$(curl -fsS -H "Authorization: Bearer $HITPAN_GH_TOKEN" \
               -H "Accept: application/vnd.github+json" "$API" 2>/dev/null)" \
    || die "B-4: GitHub API run 조회 실패($HITPAN_GH_RUN_ID). 서명 거부(fail-closed)."
  RUN_SHA="$(printf '%s' "$RUN_JSON" | jq -r '.head_sha // ""')"
  RUN_CONCLUSION="$(printf '%s' "$RUN_JSON" | jq -r '.conclusion // ""')"
  RUN_STATUS="$(printf '%s' "$RUN_JSON" | jq -r '.status // ""')"
  # (c-1) 신원 위조: 러너 주장 commit == GitHub 이 기록한 run head_sha
  [[ "$RUN_SHA" == "${HITPAN_GH_SHA:-}" ]] \
    || die "B-4: run head_sha($RUN_SHA) ≠ 러너 주장 commit(${HITPAN_GH_SHA:-}). 신원 위조 의심. 서명 거부."
  # (c-2) approval/결론 실제 비교(죽은 변수 아님): 성공한 run 만 게시 허용.
  #   이 deploy 잡이 자기 run 을 조회하면 아직 conclusion 이 빈값(in_progress)인 정상 케이스가 있다.
  #   ★ 검증팀 2차 LOW 봉합: 빈 conclusion 을 무조건 통과시키지 않고 status 로 뒷받침한다 —
  #     빈값이면 status 가 in_progress/queued 여야 통과(정상 진행중), 아니면 거부(비정상).
  case "$RUN_CONCLUSION" in
    success) : ;;                      # 성공 확정 — 통과
    "")                                # 빈값 = 아직 결론 안 남 → status 로 진행중인지 확인
      case "$RUN_STATUS" in
        in_progress|queued|waiting|pending|requested) : ;;   # 정상 진행중
        *) die "B-4: run conclusion 이 비었는데 status='$RUN_STATUS'(진행중 아님). 비정상 run 게시 차단." ;;
      esac ;;
    *) die "B-4: run conclusion='$RUN_CONCLUSION'(성공 아님). 실패·취소된 run 아티팩트 게시 차단." ;;
  esac
  ok "B-4(c): GitHub API 독립조회 통과 run=$HITPAN_GH_RUN_ID head_sha=$RUN_SHA status=$RUN_STATUS conclusion=${RUN_CONCLUSION:-(진행중)}"
fi

# ── 게시 호출 — 좁힌 2인자만. 오버라이드 절대 안 붙임(B-2 짝) ────────────────────
[[ -x "$PUBLISH" || -f "$PUBLISH" ]] || die "게시 스크립트가 없습니다: $PUBLISH"
log "게시 호출: $PUBLISH --zip $ZIP --manifest $MANIFEST (EXE 동반=$HITPAN_INSTALLER_EXE)"
# EXE 는 인자가 아니라 export 된 HITPAN_INSTALLER_EXE 로 넘긴다 — 인자 2개 계약 불변(B-2).
exec bash "$PUBLISH" --zip "$ZIP" --manifest "$MANIFEST"
