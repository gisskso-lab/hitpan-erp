#!/usr/bin/env bash
# [장치] 해시 목록(M10) · 원자료 커밋(publish 잡) — 설계 §5 · §2
#   사용: publish.sh sums <원자료 폴더>           (measure 잡 · SHA256SUMS.raw 만들기)
#         publish.sh commit                        (publish 잡 · 아래 환경 변수)
#   commit 환경: HQA_IN(운반 산출물 받은 폴더) · HQA_PUSH_TOKEN · HQA_MEASURE_RESULT · HQA_RAW_ID(measure 잡 출력 = <런>-<measure 시도> · 고침1 F11)
#               GITHUB_SHA · GITHUB_RUN_ID · GITHUB_RUN_ATTEMPT · GITHUB_REPOSITORY
#   원자료 가지: hqa/20261116-S4-gh실측-원자료-<GITHUB_SHA 앞 8자> · 부모 사슬 = 장치(장전 · 리허설) 커밋
#   지키는 것: 강제 push 0 · 태그 push 0 · git add -A / -f 0 · check-ignore 하나라도 걸리면 빨강(커밋 0)
#   종료 코드: 0 = 커밋 · push 끝 · 1 = 빨강
set -u -o pipefail

CMD="${1:?usage: publish.sh <sums|commit> ...}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SUMS=SHA256SUMS.raw
ALLOWED_RE='\.(json|jsonl|txt|png)$'

die() { echo "[publish] 빨강: $*" >&2; exit 1; }

# 목록 ↔ 실제 파일 집합 대조(해시 · 이름 둘 다) — sums 직후 · commit 앞 공용
verify_list() {
  local d="$1" listed actual
  ( cd "$d" && sha256sum -c --strict --quiet "$SUMS" ) || return 1
  listed=$(awk '{ sub(/^[0-9a-f]{64} [ *]/, ""); print }' "$d/$SUMS" | LC_ALL=C sort)
  actual=$(cd "$d" && find . -type f ! -name "$SUMS" -printf '%P\n' | LC_ALL=C sort)
  [ "$listed" = "$actual" ]
}

sums() {
  local raw="${1:?raw dir}" tmp
  [ -d "$raw" ] || die "원자료 폴더 없음"
  # 고침1 F2: 임시 목록은 원자료 폴더 밖(mktemp)에 쓴다 — 옛 꼴은 SHA256SUMS.raw.tmp 자신이 목록에 들어가 대조 실패
  tmp=$(mktemp) || die "임시 파일 실패"
  ( cd "$raw" && rm -f "$SUMS" && find . -type f ! -name "$SUMS" -printf '%P\0' | LC_ALL=C sort -z | xargs -0 -r sha256sum > "$tmp" ) || { rm -f "$tmp"; die "해시 목록 실패"; }
  mv "$tmp" "$raw/$SUMS" || { rm -f "$tmp"; die "해시 목록 옮기기 실패"; }
  chmod 644 "$raw/$SUMS"
  verify_list "$raw" || die "$SUMS 목록과 실제 파일이 다름(만든 직후)"
  echo "[M10] $SUMS 줄 수 $(wc -l < "$raw/$SUMS")"
}

commit() {
  : "${HQA_IN:?}" "${HQA_PUSH_TOKEN:?}" "${GITHUB_SHA:?}" "${GITHUB_RUN_ID:?}" "${GITHUB_RUN_ATTEMPT:?}" "${GITHUB_REPOSITORY:?}"
  # 고침1 F11: 실패한 잡만 다시 돌려도 measure 시도 번호를 따른다(publish 시도 번호 아님) — 같은 런 · 숫자 꼴만
  local id="${HQA_RAW_ID:-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}}"
  [[ "$id" =~ ^${GITHUB_RUN_ID}-[1-9][0-9]*$ ]] || die "원자료 ID 꼴 다름: $id"
  local src="$HQA_IN/$id"
  local branch="hqa/20261116-S4-gh실측-원자료-${GITHUB_SHA:0:8}"
  local repo; repo="$(git -C "$HERE" rev-parse --show-toplevel)"
  local red=0

  # 1) 운반 산출물 꼴: 이 런 · 이 시도 폴더 하나뿐
  [ -d "$src" ] || die "운반 산출물에 $id 폴더 없음"
  local others; others=$(find "$HQA_IN" -mindepth 1 -maxdepth 1 ! -name "$id" | wc -l)
  [ "$others" = "0" ] || die "운반 산출물에 다른 폴더 $others 개"

  # 2) 해시 목록 대조(운반 중 변형 0 · 목록 밖 파일 0)
  [ -f "$src/$SUMS" ] || die "$SUMS 없음"
  verify_list "$src" || die "$SUMS 대조 불일치 또는 목록과 실제 파일이 다름"

  # 3) 확장자 제한(.json · .jsonl · .txt · .png + SHA256SUMS.raw)
  local badext; badext=$(cd "$src" && find . -type f ! -name "$SUMS" -printf '%P\n' | grep -Ev "$ALLOWED_RE" | wc -l)
  [ "$badext" = "0" ] || die "허용 밖 확장자 $badext 개"

  # 4) 누출 재검 — 걸리면 그 파일 빼고 withheld.json 에 덧붙임 · 해시 목록 다시 · 잡 빨강
  node "$HERE/leak-scan.js" "$src" --stage publish --remove
  local lrc=$?
  if [ "$lrc" = "2" ]; then red=1; sums "$src"; echo "[publish] 누출 재검에 걸린 파일을 뺐다(잡 빨강)" >&2
  elif [ "$lrc" != "0" ]; then die "누출 재검 오류 rc=$lrc"; fi

  # 5) 원자료 가지: 있으면 첫 부모 사슬에 장치 커밋이 있는지 단언 뒤 덧붙임 · 없으면 장치 커밋에서 새로
  cd "$repo" || die "레포 폴더 없음"
  git config push.followTags false
  local remote_tip
  remote_tip=$(git ls-remote origin "refs/heads/$branch" | cut -f1) || die "ls-remote 실패"
  if [ -n "$remote_tip" ]; then
    git fetch --no-tags origin "+refs/heads/$branch:refs/remotes/origin/hqa-raw-target" || die "원자료 가지 받기 실패"
    git rev-list --first-parent refs/remotes/origin/hqa-raw-target | grep -qx "$GITHUB_SHA" || die "원자료 가지 첫 부모 사슬에 $GITHUB_SHA 없음(다른 회차)"
    if git cat-file -e "refs/remotes/origin/hqa-raw-target:hqa-raw/$id" 2>/dev/null; then die "원자료 가지에 hqa-raw/$id 가 이미 있음"; fi
    git checkout -q --detach refs/remotes/origin/hqa-raw-target || die "checkout 실패"
  else
    git checkout -q --detach "$GITHUB_SHA" || die "checkout 실패"
  fi
  [ -e "hqa-raw/$id" ] && die "작업 나무에 hqa-raw/$id 가 이미 있음"
  mkdir -p hqa-raw
  cp -R "$src" "hqa-raw/$id" || die "복사 실패"

  # 6) check-ignore — 하나라도 걸리면 빨강 · 커밋 0 (-f 강제 추가 금지)
  local files; files=$(cd "hqa-raw/$id" && find . -type f -printf "hqa-raw/$id/%P\n" | LC_ALL=C sort)
  local ign; ign=$(printf '%s\n' "$files" | git check-ignore --stdin -v 2>&1); local irc=$?
  if [ "$irc" = "0" ]; then echo "$ign" >&2; die "gitignore 에 걸린 파일 있음 — 커밋 0"; fi
  [ "$irc" = "1" ] || die "check-ignore 오류 rc=$irc"

  # 7) 파일 하나씩 add · 커밋 · push(강제 0 · 태그 0)
  while IFS= read -r f; do git add -- "$f" || die "add 실패"; done <<< "$files"
  local staged_other; staged_other=$(git diff --cached --name-only | grep -v "^hqa-raw/$id/" | wc -l)
  [ "$staged_other" = "0" ] || die "원자료 밖 변경이 올라감"
  local mode; mode=$(node -e 'const fs=require("fs");const f=process.argv[1];let m="unknown";for(const n of ["run.json","m0-assert.json"]){if(fs.existsSync(f+"/"+n)){m=JSON.parse(fs.readFileSync(f+"/"+n,"utf8")).mode||m;break;}}console.log(m)' "hqa-raw/$id")
  case "$mode" in rehearsal|armed|invalid) ;; *) mode=unknown ;; esac  # 커밋 메시지엔 3값만(원자료 유래 글자 거름)
  local msg; msg=$(mktemp)
  {
    printf 'hqa-raw: S4 관문1 GitHub 서버 실측 원자료 %s\n\n' "$id"
    printf 'run: %s\nmode: %s\ndevice-commit: %s\nfixture-tree: 0b31f8692367ae28a6fac255d8eae5c931066d7b\nmeasure-job: %s\npublish-withheld: %s\npublish-attempt: %s\n' \
      "$id" "$mode" "$GITHUB_SHA" "${HQA_MEASURE_RESULT:-unknown}" "$red" "$GITHUB_RUN_ATTEMPT"
  } > "$msg"
  git -c user.name="github-actions[bot]" -c user.email="41898282+github-actions[bot]@users.noreply.github.com" commit -q -F "$msg" || die "커밋 실패"
  local new; new=$(git rev-parse HEAD)
  # 고침1 F11: 인증 헤더의 base64 꼴도 로그 가림에 등록(러너는 원래 토큰 원형만 가린다)
  local b64; b64=$(printf 'x-access-token:%s' "$HQA_PUSH_TOKEN" | base64 -w0)
  echo "::add-mask::$b64"
  local hdr="AUTHORIZATION: basic $b64"
  git -c "http.https://github.com/.extraheader=$hdr" push --no-follow-tags origin "HEAD:refs/heads/$branch" || die "push 실패(강제 안 함)"
  unset hdr b64
  local after; after=$(git ls-remote origin "refs/heads/$branch" | cut -f1)
  [ "$after" = "$new" ] || die "push 뒤 원격 머리 불일치"

  local link="https://raw.githubusercontent.com/${GITHUB_REPOSITORY}/${new}/hqa-raw/${id}/"
  echo "[publish] 원자료 가지 $branch · 커밋 $new · 경로 hqa-raw/$id"
  if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    {
      echo "### 원자료 커밋"
      echo "- 가지: \`$branch\`"
      echo "- 커밋: \`$new\`"
      echo "- 경로: \`hqa-raw/$id/\` · 로그인 없이: ${link}run.json (리허설 M0 실패면 ${link}m0-assert.json)"
      echo "- measure 잡: ${HQA_MEASURE_RESULT:-unknown} · publish 누출 재검 보류: $red"
    } >> "$GITHUB_STEP_SUMMARY"
  fi
  [ "$red" = "0" ]
}

case "$CMD" in
  sums) shift; sums "$@" ;;
  commit) commit ;;
  *) die "unknown command: $CMD" ;;
esac
