#!/usr/bin/env bash
# 마이그레이션 번호 충돌 검사 — DB-NN_*.sql 의 번호는 유일해야 한다
#
# 왜 이 검사가 있나 (2026-08-24 봉합 · 1.3.5 동시검증이 적발한 P0):
#   `DB-100_payroll.sql`(8/14 적용분)이 있는데 `DB-100_resignation_letters.sql`(8/24)을
#   같은 번호로 새로 만들었다. MigrationRunner 는 파일명이 아니라 **번호로 식별자를 만들고**
#   (MigrationRunner:70-76), schema_migrations 에 success=1 인 ID 는 건너뛴다(:103-107).
#
#   ⇒ 급여 마이그가 이미 "DB-100 완료" 를 기록해 둔 탓에 퇴직서 SQL 은
#     **한 줄도 실행되지 않고 skip** 됐다. 표가 없으니 그 화면만 500.
#
#   🔴 이 사고는 기존 관문을 전부 통과했다:
#     · 빌드 errors 0 + warnings 0 — 파일 이름 문제라 컴파일러가 볼 수 없다
#     · 게시 게이트 — "새 SQL 인데 requiresMigration=false" 만 보지 번호는 안 본다
#     · 로컬 실측 — 빈 DB 에는 DB-100 기록이 없어 그냥 실행된다.
#       "빈 DB 에서 된다" 는 "고객 DB 에서 된다" 가 아니다.
#
#   사람이 매번 최대 번호를 세어 보는 것에 맡기지 않는다. CI 가 대신 기억한다.
#
# 판정 규칙: MigrationRunner 와 **같은 규칙**으로 ID 를 만든다.
#   패턴 ^DB-(\d+)([a-zA-Z]*)_  →  ID = "DB-" + 번호 + 접미사
#   접미사(DB-08b 등)가 다르면 다른 마이그다 — 번호만 같은 건 충돌이 아니다.
#
# 종료 코드: 0 = 충돌 없음, 1 = 충돌(빌드 실패시킬 것)

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SQL_DIR="$ROOT/src/HitPan.API/Migrations/SQL"

fail() { echo "[migration-id] 🔴 $*" >&2; exit 1; }

[ -d "$SQL_DIR" ] || fail "마이그 폴더 없음: $SQL_DIR"

# MigrationRunner 의 MigrationId 생성 규칙을 그대로 재현한다.
#   C# : $"DB-{num.PadLeft(2,'0')}{suffix}"
#   PadLeft(2,'0') 는 1자리만 채운다 — 2자리 이상은 그대로다(100 → "100").
#   접미사는 대소문자 무시로 비교한다(러너가 OrdinalIgnoreCase 로 정렬·비교).
ids=""
while IFS= read -r path; do
  name="$(basename "$path")"
  num="$(printf '%s' "$name" | sed -n 's/^DB-\([0-9]\{1,\}\)\([a-zA-Z]*\)_.*/\1/p')"
  suf="$(printf '%s' "$name" | sed -n 's/^DB-\([0-9]\{1,\}\)\([a-zA-Z]*\)_.*/\2/p')"
  # 패턴에 안 맞는 파일은 러너도 무시한다(Where(f => f.Match.Success)) — 여기서도 건너뛴다.
  [ -n "$num" ] || continue
  if [ "${#num}" -lt 2 ]; then
    padded="0$num"
  else
    padded="$num"
  fi
  # 접미사는 소문자로 접어서 비교 (러너의 OrdinalIgnoreCase 와 맞춘다)
  suf_lower="$(printf '%s' "$suf" | tr '[:upper:]' '[:lower:]')"
  ids="${ids}DB-${padded}${suf_lower}|${name}"$'\n'
done < <(find "$SQL_DIR" -maxdepth 1 -name 'DB-*.sql' | sort)

[ -n "$ids" ] || fail "DB-*.sql 파일을 하나도 못 찾음 — 경로가 바뀌었는지 확인할 것"

total="$(printf '%s' "$ids" | grep -c . || true)"

# 같은 ID 를 두 번 이상 쓰는 것만 뽑는다.
dupes="$(printf '%s' "$ids" | awk -F'|' 'NF{print $1}' | sort | uniq -d)"

if [ -n "$dupes" ]; then
  echo "[migration-id] 🔴 마이그레이션 번호가 겹칩니다 — 뒤 파일은 영원히 실행되지 않습니다." >&2
  echo "" >&2
  while IFS= read -r dup; do
    [ -n "$dup" ] || continue
    echo "  $dup 를 쓰는 파일:" >&2
    printf '%s' "$ids" | awk -F'|' -v d="$dup" '$1==d{print "    · " $2}' >&2
  done <<< "$dupes"
  echo "" >&2
  # 다음에 쓸 수 있는 번호를 알려준다 — 사람이 다시 세지 않게.
  maxnum="$(printf '%s' "$ids" | sed -n 's/^DB-0*\([0-9]\{1,\}\).*/\1/p' | sort -n | tail -1)"
  echo "  ⇒ 고치는 법: 새로 만든 파일의 번호를 DB-$((maxnum + 1)) 로 바꾸세요." >&2
  echo "     (현재 쓰인 가장 큰 번호 = $maxnum)" >&2
  echo "" >&2
  echo "  왜 위험한가: MigrationRunner 는 파일명이 아니라 번호로 적용 여부를 기록합니다." >&2
  echo "  앞 번호가 이미 적용된 고객 DB 에서는 뒤 파일이 skip 되어 표가 생기지 않고," >&2
  echo "  그 화면만 500 이 납니다. 빌드와 로컬 빈 DB 실측은 이걸 못 잡습니다." >&2
  exit 1
fi

echo "[migration-id] ✅ 마이그레이션 번호 충돌 없음 ($total 건)"
exit 0
