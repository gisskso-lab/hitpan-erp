#!/usr/bin/env bash
# 비밀 탐지 — 사건별 검사 범위 계산
# 근거: docs/설계/보안/20261113_비밀탐지설정정상화_설계서.md §2-2 (안 B) · §2-3 반증
#       docs/운영기록/20261113작_비밀탐지설정정상화_작업지시서.md ㄱ-2
#       [3-V] 병렬이슈 F1(기준 참조 완전 이름) · F5(사건 JSON 을 그대로 읽는다)
#
#   사건            BASE                                              HEAD
#   pull_request    PR base.sha (현행)                                 PR head.sha
#   push(첫·강제)   merge-base(refs/remotes/origin/<기본 가지>, after)  after
#   push(이어)      before (현행)                                      after
#   push(삭제)      검사 안 함 (skip)
#   schedule·수동   전체 이력 (full)
#
# 원칙: 계산이 안 되면 **빨강**(exit 1). 조용히 「전체」 나 「0」 으로 떨어지지 않는다.
#
# 입력 — 러너가 주는 그대로 (자체시험도 같은 두 값을 가짜 사건 파일로 준다):
#   GITHUB_EVENT_NAME  사건 이름
#   GITHUB_EVENT_PATH  사건 JSON 파일
#   REPO_DIR           검사할 저장소 (비면 현재 폴더)
#   GITHUB_OUTPUT      결과 기록 파일 (비면 표준 출력)
# 출력(GITHUB_OUTPUT): mode=range|full|skip · base=<커밋> · head=<커밋> · reason=<사유>
set -euo pipefail

ZERO="0000000000000000000000000000000000000000"
REPO_DIR="${REPO_DIR:-.}"
EVENT_NAME="${GITHUB_EVENT_NAME:-}"
EVENT_PATH="${GITHUB_EVENT_PATH:-}"
OUT="${GITHUB_OUTPUT:-/dev/stdout}"

g() { git -C "$REPO_DIR" "$@"; }

fail() {
  echo "::error title=검사 범위 계산 실패::$*"
  exit 1
}

emit() {
  local mode="$1" base="$2" head="$3" reason="$4"
  {
    echo "mode=${mode}"
    echo "base=${base}"
    echo "head=${head}"
    echo "reason=${reason}"
  } >> "$OUT"
  echo "검사 범위: mode=${mode} base=${base:-(없음)} head=${head:-(없음)} — ${reason}"
}

# 사건 JSON 의 값 (없으면 빈 값)
ev() { jq -r "$1 // empty" "$EVENT_PATH"; }

# 사건이 주는 커밋 값은 16진 SHA 만 받는다 — 옵션·이름 해석이 끼어들 틈을 없앤다
is_sha() { [[ "$1" =~ ^[0-9a-f]{40}([0-9a-f]{24})?$ ]]; }

# 커밋으로 풀리면 전체 SHA (태그 개체는 커밋으로 벗긴다) · 아니면 실패
commit_of() {
  local rev="$1"
  is_sha "$rev" || return 1
  g rev-parse --verify --quiet "${rev}^{commit}"
}

g rev-parse --git-dir > /dev/null 2>&1 || fail "저장소가 아니다: ${REPO_DIR}"
case "$EVENT_NAME" in
  schedule|workflow_dispatch) ;;
  *) [ -n "$EVENT_PATH" ] && [ -f "$EVENT_PATH" ] || fail "사건 파일이 없다" ;;
esac

case "$EVENT_NAME" in
  pull_request)
    base="$(commit_of "$(ev '.pull_request.base.sha')")" || fail "PR base 커밋을 찾지 못했다"
    head="$(commit_of "$(ev '.pull_request.head.sha')")" || fail "PR head 커밋을 찾지 못했다"
    if [ "$base" = "$head" ]; then
      emit skip "$base" "$head" "PR 새 커밋 0"
    else
      emit range "$base" "$head" "PR base..head (현행)"
    fi
    ;;

  push)
    ref="$(ev '.ref')"
    before="$(ev '.before')"
    after="$(ev '.after')"
    deleted="$(ev '.deleted')"
    default_branch="$(ev '.repository.default_branch')"
    [ -n "$default_branch" ] || default_branch="main"
    git check-ref-format --branch "$default_branch" > /dev/null 2>&1 || fail "기본 가지 이름이 올바르지 않다"
    main_ref="refs/remotes/origin/${default_branch}"   # F1: 완전 이름 — 같은 이름 태그가 가로채지 못한다

    if [ "$deleted" = "true" ] || [ "$after" = "$ZERO" ]; then
      emit skip "" "" "삭제 사건 — 검사할 커밋 없음"
      exit 0
    fi
    head="$(commit_of "$after")" || fail "after 커밋을 찾지 못했다"

    use_merge_base=0
    reason=""
    before_c=""
    if [ -z "$before" ] || [ "$before" = "$ZERO" ]; then
      use_merge_base=1; reason="첫 푸시"
    elif ! is_sha "$before"; then
      fail "before 값이 SHA 가 아니다"
    elif ! before_c="$(g rev-parse --verify --quiet "${before}^{commit}")"; then
      # 실전 강제 푸시: 떨어진 before 커밋은 fetch 되지 않는다(개체 없음 · git 128 계열)
      use_merge_base=1; reason="강제 푸시(before 커밋이 저장소에 없음)"
    else
      set +e
      g merge-base --is-ancestor "$before_c" "$head"
      anc=$?
      set -e
      case "$anc" in
        0) ;;
        1) use_merge_base=1; reason="강제 푸시(before 가 after 의 조상 아님)" ;;
        *) fail "조상 판정 실패(git 종료 코드 ${anc})" ;;
      esac
    fi

    if [ "$use_merge_base" -eq 1 ]; then
      # 기본 가지 자체의 첫·강제 푸시는 합류점이 자기 자신이 되어 「새 커밋 0」 으로 조용해진다 → 빨강
      if [ "$ref" = "refs/heads/${default_branch}" ]; then
        fail "기본 가지(${default_branch}) 의 ${reason} — 합류점 기준을 쓸 수 없다. 전체 검사(수동 실행)로 확인할 것"
      fi
      # 기준 참조는 완전 이름으로 SHA 하나로 고정한 뒤 그 SHA 만 쓴다
      main_sha="$(g rev-parse --verify --quiet "${main_ref}^{commit}")" || fail "기준 가지 참조(${main_ref}) 가 없다 — 전체 이력 checkout 확인"
      base="$(g merge-base "$main_sha" "$head")" || fail "합류점이 없다(기준 가지와 이력이 이어지지 않음) — ${reason}"
      [ -n "$base" ] || fail "합류점이 비었다 — ${reason}"
      reason="${reason} → 합류점 기준"
    else
      base="$before_c"
      reason="이어 푸시 before..after (현행)"
    fi

    if [ "$base" = "$head" ]; then
      emit skip "$base" "$head" "새 커밋 0 (${reason})"
    else
      emit range "$base" "$head" "$reason"
    fi
    ;;

  schedule|workflow_dispatch)
    emit full "" "" "전체 이력 (${EVENT_NAME})"
    ;;

  *)
    fail "모르는 사건: '${EVENT_NAME}'"
    ;;
esac
