#!/usr/bin/env bash
# =============================================================================
# 복원 교체(W-G1-52 갈래 A-2③) DB 게이트 음성 대조군 — 변이 런 (기본 실행 아님 · 측정 전용)
#
#   근거: 작업지시서 docs/운영기록/20261128작_W-G1-52_갈래A2_작업지시서.md §13-3 P2(고침 1-3 = DB 변이 런 장치)
#         리뷰서 docs/검증/erp/20261128_W-G1-52_A2-3_PR1030_머리f99dd103_짧은재확인_보안상무_검증팀장.md §4-1 M1 ~ M14 · §4-2
#         개발명세서 docs/개발/erp/20261128_W-G1-52_갈래A2-3_대조교체_개발명세서.md §5 · §10-4 · §10-1-3(이 표의 바늘 셈 · 빌드 확인 · 넘김)
#         꼴 = scripts/gates/cooltime-mutation-run.sh(쿨타임 변이 런 · 바늘 정확히 1번 · 변이마다 git 원복)
#
#   제품 문장 하나를 빼거나 되돌린 뒤(변이) 기대 시험이 **FAIL 하는지** 본다. FAIL 이면 그 시험이 그 문장을 실제로 잰다는 증거다.
#   변이마다 끝나면 git 으로 원복한다(작업 나무만 · 커밋 0). 한 줄 = 시험 하나(같은 변이를 대상 시험마다 한 줄씩).
#
#   🔴 시험 DB 가 있어야 한다 — CI db-gate 잡과 같은 환경 변수(HITPAN_REQUIRE_DB · HITPAN_MYSQL · HITPAN_DB_HOST/PORT/USER/PASS ·
#      HITPAN_TEST_DB)를 준 곳에서만 돌린다. 개발 PC(3306 · 3307 = 실물 DB)에서 돌리지 않는다(#39).
#      HITPAN_REQUIRE_DB 가 없으면 이 런은 **무효**(변이마다 「건너뜀」 · 끝에 ::error:: · 끝 코드 3).
#   🔴 SafeRestoreSwapGateDbTests 는 CI 에서 TZ=Asia/Seoul 따로 단계로 돈다(D9 · D9b) — 이 스크립트도 시험을 TZ=Asia/Seoul 로 부른다.
#   🔴 변이는 고침 커밋 뒤에만 — 대상 제품 파일에 미커밋 변경이 있으면 시작하지 않는다(git checkout 원복이 미커밋 고침을 지우는 사고 방지).
#
#   쓰는 법:  bash scripts/gates/restore-swap-mutation-run.sh                 # 전부(시험 DB 필요)
#             bash scripts/gates/restore-swap-mutation-run.sh M1 S5          # 고른 것만
#             bash scripts/gates/restore-swap-mutation-run.sh --count-only   # 바늘 셈만(파일 손 0 · DB 0)
#             bash scripts/gates/restore-swap-mutation-run.sh --build-only   # 변이 → Release 빌드 → 원복만(시험 0 · DB 0 · 로컬 확인용)
# =============================================================================
set -u
cd "$(git rev-parse --show-toplevel)"

MODE="run"
case "${1:-}" in
  --count-only) MODE="count"; shift ;;
  --build-only) MODE="build"; shift ;;
esac

declare -A FILES=(
  [STEP]=src/HitPan.Application/Services/RestoreSwapStep.cs
  [SQL]=src/HitPan.Application/Services/RestoreSwapSql.cs
  [PLAN]=src/HitPan.Application/Services/RestoreSwapPlan.cs
  [RULES]=src/HitPan.Application/Services/RestoreOverlayRules.cs
  [SAFE]=src/HitPan.Application/Services/RestoreSafePath.cs
  [CLASS]=src/HitPan.Application/Services/RestoreTableClassification.cs
)
CLASS_FQN="HitPan.Tests.Integrity.SafeRestoreSwapGateDbTests"
TESTS_PROJ=src/HitPan.Tests/HitPan.Tests.csproj
LOGDIR="${RUNNER_TEMP:-${TMPDIR:-/tmp}}"
SEP=' ║ '
NL='⏎'   # 바늘 · 바꿀 값 안의 줄바꿈 표지(바늘 쪽은 CRLF · LF 둘 다 맞음)

# 줄 꼴:  이름 ║ 파일 열쇠 ║ 바늘(그대로 · 정확히 1번) ║ 바꿀 값 ║ 기대 FAIL 시험(SafeRestoreSwapGateDbTests.<이 글자> 로 시작)
#   시험 글자 = 메서드 이름 앞 ASCII 부분(밑줄까지) — 다른 메서드 이름에 들어가지 않게 골랐다(명세서 §10-1-3 표).
read -r -d '' TABLE <<'TABLE'
# ── 리뷰서 §4-1 · 명세서 §10-4 (M1 ~ M14) ──
M1 ║ STEP ║ if (others > 0) ║ if (others < 0) ║ P2_1_
M2 ║ PLAN ║  WHERE (tenant_id = @t OR tenant_id IS NULL)" ║ " ║ P2_1_
M3 ║ STEP ║ RestoreSwapSql.CountOverlayMismatch, p, tx, ct).ConfigureAwait(false) > 0) ║ RestoreSwapSql.CountOverlayMismatch, p, tx, ct).ConfigureAwait(false) < 0) ║ G21_13b_
M4 ║ SQL ║ dl.approver_id = n.employee_id AND dl.is_active = 1) ║ dl.approver_id = n.employee_id) ║ G21_9b_
M5 ║ SQL ║  WHERE l2.tenant_id = n.tenant_id AND l2.user_id = s.user_id)" ║  WHERE 1 = 0)" ║ G21_7b_
M6 ║ STEP ║ if (marked != 1) ║ if (marked < 0) ║ G7b_
M7a ║ RULES ║ {u}.is_deleted = 0 AND {u}.is_active = 1 AND ║ {u}.is_deleted = 0 AND ║ G21_7c_
M7b ║ RULES ║ {u}.is_deleted = 0 AND {u}.is_active = 1 AND ║ {u}.is_deleted = 0 AND ║ G21_10neg_
M8 ║ STEP ║ _localNow = localNow ?? (() => DateTime.Now); ║ _localNow = localNow ?? (() => DateTime.Now.ToUniversalTime()); ║ D9b_
M9a ║ STEP ║ RestoreSwapSql.MarkHistorySuccess, new { Now = LocalNow(), ║ RestoreSwapSql.MarkHistorySuccess, new { Now = DateTime.UtcNow, ║ G4_G5_G7_
M9b ║ STEP ║ RestoreSwapSql.MarkHistorySuccess, new { Now = LocalNow(), ║ RestoreSwapSql.MarkHistorySuccess, new { Now = DateTime.UtcNow, ║ D9b_
M10 ║ SAFE ║ unique_checks = 0, autocommit = 1", ║ unique_checks = 0", ║ D15_
M11 ║ STEP ║ await using var tx = await ((DbConnection)_db).BeginTransactionAsync(ct).ConfigureAwait(false);⏎        var n = await _db.ExecuteAsync(new CommandDefinition(sql, p, tx, cancellationToken: ct)).ConfigureAwait(false);⏎        await tx.CommitAsync(CancellationToken.None).ConfigureAwait(false); ║ var n = await _db.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct)).ConfigureAwait(false); ║ D15b_
M12 ║ STEP ║ ct = CancellationToken.None; ║ // (변이 M12) ⑤ 도 부르는 쪽 취소 토큰 그대로 ║ P3_3_
M13 ║ SQL ║ error_message = @Code WHERE status = 'running' AND started_at <= @Now" ║ error_message = @Code WHERE started_at <= @Now" ║ P3_2_
M14 ║ CLASS ║ var keep = KeepSet.Contains(tableName);⏎        var backup = BackupSet.Contains(tableName); ║ var keep = KeepSet.Contains(tableName) && !(tableName is "refresh_tokens" or "tenant_devices");⏎        var backup = BackupSet.Contains(tableName) || tableName is "refresh_tokens" or "tenant_devices"; ║ G5_
# ── 리뷰서 §4-2 · 명세서 §5 첫 구현 DB 몫(§4-1 과 겹치는 것 = C3′ 단언 M3 · R 퇴사자 전부 M4 · 겹침 M5 · is_active 낱 M7 · G7 M6 · D15 M10 은 위 줄로 갈음) ──
S1 ║ RULES ║ ["role", "is_active", ║ ["is_active", ║ G21_1_2_
S2 ║ SQL ║ RestoreOverlayRules.ChatMemberOverlayColumns.Select(c => $"n.{c} = l.{c}")) ║ RestoreOverlayRules.ChatMemberOverlayColumns.Select(c => $"n.{c} = n.{c}")) ║ G21_8_11_14_17_
S3 ║ STEP ║ var overlay = await OverlayAsync(tenantId, LocalNow(), ct).ConfigureAwait(false); ║ var overlay = new RestoreSwapResult<RestoreOverlayCounts> { Ok = true, Step = SafeRestoreStep.Verify, DataUnchanged = true, Message = "", Value = new RestoreOverlayCounts() }; ║ G21_1_2_
S4 ║ RULES ║ nx.employee_id = {l}.employee_id) ║ nx.user_id = {l}.user_id) ║ G21_10_
S5 ║ STEP ║ if (aE != 0 || bE != counts.BothEmployees || cE != counts.LiveOnlyEmployees || dE != 0⏎                || aC != 0 || bC != counts.BothChat || cC != counts.LiveOnlyChat) ║ if (aE < 0 && bE < 0 && cE < 0 && dE < 0 && aC < 0 && bC < 0 && cC < 0) ║ G21_12_
S6a ║ STEP ║ cE != counts.LiveOnlyEmployees ║ cE != 0 ║ G21_3_4_5_6
S6b ║ STEP ║ cC != counts.LiveOnlyChat ║ cC != 0 ║ G21_8_11_14_17_
S7 ║ STEP ║ var targets = (await _db.QueryAsync<string>(new CommandDefinition(RestoreSwapSql.ResignTargets, p, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList(); ║ var targets = new List<string>(); ║ G21_9_
S8 ║ SQL ║ AND EXISTS (SELECT 1 FROM rs_new__link_snapshot s ║ AND 1 = 0 AND EXISTS (SELECT 1 FROM rs_new__link_snapshot s ║ G21_9_
S9a ║ STEP ║ var narrowed = await Exec(RestoreSwapSql.NarrowChatLeave, new { t = tenantId, RestoreAt = restoreAtLocal }, tx, ct).ConfigureAwait(false); ║ var narrowed = 0; ║ G21_8_11_14_17_
S9b ║ SQL ║ AND EXISTS (SELECT 1 FROM chat_room_members r WHERE r.tenant_id = n.tenant_id AND r.room_id = n.room_id) ║ AND EXISTS (SELECT 1 FROM DUAL) ║ G21_10_
S10 ║ STEP ║ await Exec(RestoreSwapSql.LinkBackupOnly, p, tx, ct).ConfigureAwait(false); ║ await Task.CompletedTask.ConfigureAwait(false); ║ G21_10_
S12 ║ RULES ║  AND n2.user_id = {l}.user_id)"; ║ )"; ║ G21_10neg_
S13 ║ RULES ║ LiveOnlyActiveLinkedWhere(l) + " AND " + BPrimeExemption(l); ║ LiveOnlyActiveLinkedWhere(l); ║ G21_10_
S14 ║ SQL ║ RestoreOverlayRules.LiveOnlyActiveLinkedAfterBPrime("l") + " FOR UPDATE" ║ RestoreOverlayRules.LiveOnlyActiveLinkedWhere("l") + " FOR UPDATE" ║ G21_10_
# ⬛ 고침 1-4 옛 줄(기대 시험 G4_G5_G7_ — 측정 런 46154225643 살아남음 · G4 의 세션 RC 는 ④-끝 되돌림에 지워져 ⑤ 에 못 감 · 명세서 §10-1-4) → 기대 시험만 D2_ 로 · 바늘 · 바꿀 값 그대로
# S15 ║ STEP ║ "SET SESSION TRANSACTION ISOLATION LEVEL REPEATABLE READ", cancellationToken: ct)).ConfigureAwait(false);⏎⏎            tx = await ((DbConnection)_db).BeginTransactionAsync(SwapIsolation, ct) ║ "SELECT 1", cancellationToken: ct)).ConfigureAwait(false);⏎⏎            tx = await ((DbConnection)_db).BeginTransactionAsync(ct) ║ G4_G5_G7_
S15 ║ STEP ║ "SET SESSION TRANSACTION ISOLATION LEVEL REPEATABLE READ", cancellationToken: ct)).ConfigureAwait(false);⏎⏎            tx = await ((DbConnection)_db).BeginTransactionAsync(SwapIsolation, ct) ║ "SELECT 1", cancellationToken: ct)).ConfigureAwait(false);⏎⏎            tx = await ((DbConnection)_db).BeginTransactionAsync(ct) ║ D2_
S16 ║ STEP ║ Now = LocalNow(), Src = sourceLabel ║ Now = DateTime.UtcNow, Src = sourceLabel ║ G4_G5_G7_
S17 ║ STEP ║ MigrationJobStore.AnyRunning(tenantId) ║ MigrationJobStore.AnyRunning(tenantId + "-변이") ║ D4_D13_
# ── 넘김(표에 줄 없음 · 까닭 = 명세서 §10-1-3) — S11 조각 셋 뺌(기대 시험 없음) · N1-a 표지 · 깃발 · 셈 순서(합류 뒤 · A-2① 코드) · G15(변이 없음 · 설계 §15-9) ──
TABLE

# 바늘 셈 · 바꿈 — 바늘을 ⏎ 로 쪼개 조각마다 quotemeta · 조각 사이 = \r?\n(CRLF 작업 나무 · LF CI 둘 다)
PERL_PAT='my @p = split(/\x{E2}\x{8F}\x{8E}/, $ENV{FROM}, -1); my $pat = join("\\r?\\n", map { quotemeta } @p);'
count_needle() {  # $1 파일 $2 바늘
  FROM="$2" perl -0777 -ne "$PERL_PAT"' my $n = () = /$pat/g; print $n' "$1"
}
apply_needle() {  # $1 파일 $2 바늘 $3 바꿀 값
  FROM="$2" TO="$3" perl -0777 -pi -e "$PERL_PAT"' my $t = $ENV{TO}; $t =~ s/\x{E2}\x{8F}\x{8E}/\n/g; s/$pat/$t/' "$1"
}

ALL_FILES=()
for k in "${!FILES[@]}"; do ALL_FILES+=("${FILES[$k]}"); done

if [ "$MODE" != "count" ] && ! git diff --quiet -- "${ALL_FILES[@]}"; then
  echo "::error::대상 제품 파일에 미커밋 변경이 있다 — 변이는 고침 커밋 뒤에만(git checkout 원복이 미커밋 고침을 지운다)"; git diff --stat -- "${ALL_FILES[@]}"; exit 2
fi
if [ "$MODE" = "run" ] && [ -z "${HITPAN_REQUIRE_DB:-}" ]; then
  NODB=1
else
  NODB=0
fi

want=" $* "
fails=0; ran=0; skipped=0
while IFS= read -r line; do
  case "$line" in ''|'#'*) continue ;; esac
  rest="$line"
  name="${rest%%"$SEP"*}"; rest="${rest#*"$SEP"}"
  key="${rest%%"$SEP"*}";  rest="${rest#*"$SEP"}"
  from="${rest%%"$SEP"*}"; rest="${rest#*"$SEP"}"
  to="${rest%%"$SEP"*}";   test="${rest#*"$SEP"}"
  if [ "$#" -gt 0 ] && [[ "$want" != *" $name "* ]]; then continue; fi
  file="${FILES[$key]:-}"
  if [ -z "$file" ] || [ ! -f "$file" ]; then
    echo "::error::[변이 $name] 파일 열쇠 $key 를 모른다(표 고쳐라)"; fails=$((fails + 1)); continue
  fi
  count=$(count_needle "$file" "$from")
  if [ "$count" != "1" ]; then
    echo "::error::[변이 $name] 바늘이 $file 에 ${count}번 — 정확히 1번이어야 한다(제품이 바뀌었으면 이 표를 같이 고쳐라)"
    fails=$((fails + 1)); continue
  fi
  if [ "$MODE" = "count" ]; then echo "  [변이 $name] $file 바늘 1번 · 기대 FAIL $test"; continue; fi
  if [ "$NODB" = "1" ]; then
    echo "▶ [변이 $name] 건너뜀 — 시험 DB 없음(HITPAN_REQUIRE_DB). 이 변이는 확인 안 됐다."; skipped=$((skipped + 1)); continue
  fi

  apply_needle "$file" "$from" "$to"
  echo "▶ [변이 $name] $file 변이 → 시험 $test (기대 = FAIL)"
  ran=$((ran + 1))
  if ! dotnet build "$TESTS_PROJ" --configuration Release --nologo -v q > "$LOGDIR/rsm_mut_$name.build.log" 2>&1; then
    echo "::error::[변이 $name] 변이 빌드 실패 — 대조군이 아니라 컴파일 오류다"; tail -20 "$LOGDIR/rsm_mut_$name.build.log"
    fails=$((fails + 1))
  elif [ "$MODE" = "build" ]; then
    w=$(grep -cE "warning CS[0-9]+" "$LOGDIR/rsm_mut_$name.build.log" || true)
    echo "✅ [변이 $name] 변이 건 Release 빌드 됨(오류 0 · 경고 줄 $w)"
  elif TZ=Asia/Seoul dotnet test "$TESTS_PROJ" --configuration Release --no-build --nologo \
         --filter "FullyQualifiedName~$CLASS_FQN.$test" > "$LOGDIR/rsm_mut_$name.test.log" 2>&1; then
    if grep -qE "No test matches|Total tests: 0" "$LOGDIR/rsm_mut_$name.test.log"; then
      echo "::error::[변이 $name] 시험 $test 가 0건 — 필터 글자가 메서드에 안 맞는다(표 고쳐라)"
    else
      echo "::error::[변이 $name] $test 가 변이 뒤에도 통과했다 — 이 시험은 그 문장을 재지 못한다"
      grep -E "Passed|Failed|Total" "$LOGDIR/rsm_mut_$name.test.log" | tail -3
    fi
    fails=$((fails + 1))
  elif grep -q "\[SKIP\]" "$LOGDIR/rsm_mut_$name.test.log"; then
    echo "::error::[변이 $name] SKIP — 안 돈 것을 FAIL 로 셀 수 없다"; fails=$((fails + 1))
  else
    echo "✅ [변이 $name] $test FAIL 확인(기대대로)"
    grep -E "^\s+Failed |Assert|기대" "$LOGDIR/rsm_mut_$name.test.log" | head -3
  fi
  git checkout -- "$file"
done <<< "$TABLE"

if [ "$MODE" = "count" ]; then
  if [ "$fails" -gt 0 ]; then echo "::error::바늘 셈 실패 $fails 건"; exit 1; fi
  echo "바늘 셈 끝 — 전부 정확히 1번"; exit 0
fi

# 원복 확인 — 변이가 남아 있으면 안 된다
if ! git diff --quiet -- "${ALL_FILES[@]}"; then
  echo "::error::변이 원복 실패 — 제품 파일에 변경이 남았다"; git diff --stat; exit 2
fi
# 원복 뒤 다시 빌드 — 뒤 단계가 마지막 변이 바이너리를 쓰지 않게(원복 뒤 깨끗 빌드 확인 겸)
if [ "$ran" -gt 0 ]; then
  if ! dotnet build "$TESTS_PROJ" --configuration Release --nologo -v q > "$LOGDIR/rsm_restore.build.log" 2>&1; then
    echo "::error::원복 뒤 빌드 실패"; tail -20 "$LOGDIR/rsm_restore.build.log"; exit 2
  fi
  echo "원복 뒤 Release 빌드 됨"
fi
if [ "$NODB" = "1" ]; then
  echo "::error::변이 런 무효 — 시험 DB 없음(HITPAN_REQUIRE_DB) · 건너뜀 $skipped 건 · 하나도 확인 안 됐다"; exit 3
fi
if [ "$fails" -gt 0 ]; then echo "::error::변이 런 실패 $fails 건"; exit 1; fi
echo "변이 런 끝 — 실패 0 (돈 변이 $ran 건 · 모드 $MODE)"
