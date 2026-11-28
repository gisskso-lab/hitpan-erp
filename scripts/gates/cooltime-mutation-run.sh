#!/usr/bin/env bash
# =============================================================================
# 쿨타임 게이트 음성 대조군 — 변이 런 (설계 docs/설계/erp/쿨타임_자동로그아웃_설계_20260928.md §7 · §D · §E)
#
#   제품 문장 하나를 빼거나 되돌린 뒤(변이) 해당 게이트가 **FAIL 하는지** 본다. FAIL 이면 그 게이트가
#   그 문장을 실제로 재고 있다는 증거다. 변이마다 끝나면 git 으로 원복한다(작업 나무만 · 커밋 0).
#
#   🔴 DB 게이트 변이는 시험 DB 가 있어야 한다 — CI db-gate 잡과 같은 환경 변수
#      (HITPAN_REQUIRE_DB · HITPAN_MYSQL · HITPAN_DB_HOST/PORT/USER/PASS)를 준 곳에서만 돌린다.
#      개발 PC(3306 = 실물 DB)에서 돌리지 않는다. HITPAN_REQUIRE_DB 가 없으면 DB 변이는 건너뛴다.
#
#   쓰는 법:  bash scripts/gates/cooltime-mutation-run.sh            # 전부
#             bash scripts/gates/cooltime-mutation-run.sh a c        # 고른 것만(ⓐ ⓒ)
#   근거·표: docs/개발/erp/20261127_쿨타임1b_갈래3_게이트_개발명세서.md §3
# =============================================================================
set -u
cd "$(git rev-parse --show-toplevel)"

ENGINE=src/HitPan.API/Services/Cooltime/CooltimeEngine.cs
HR=src/HitPan.Application/Services/HrService.cs
SETTINGS=src/HitPan.Application/Services/SettingsService.cs
SESSION_MW=src/HitPan.API/Middleware/SessionValidityMiddleware.cs
NOTIFY=src/HitPan.Web/Services/NotificationClient.cs
SCREEN=src/HitPan.Web/Services/CooltimeScreenRules.cs   # 🆕 11/27 고침 차례
PROMPT=src/HitPan.Web/Components/Common/CooltimePrompt.razor   # 🆕 11/28 고침 1
API_PROGRAM=src/HitPan.API/Program.cs                                           # 🆕 11/28 다음 묶음(§288) ⓨ
MIG132=src/HitPan.API/Migrations/SQL/DB-132_tenant_settings_cooltime.sql        # 🆕 11/28 다음 묶음(§288) ⓩ
DB="CooltimeDbGateTests"
RULE="CooltimeRuleGateTests"
MIG="ErpMigrationApplyGateTests"                                                # 🆕 11/28 ⓩ

# 이름 | 파일 | 바꿀 글자(그대로) | 바꿀 값 | 시험(FullyQualifiedName~) | DB 필요(1/0)
MUTATIONS=(
  "a|$ENGINE|AND tenant_id = @TenantId AND device_kind = 'pc'\" + guard|AND tenant_id = @TenantId\" + guard|$DB.G_C7_|1"
  "b|$ENGINE|await WriteCooltimeCheckOutAsync(tenantId, row.UserId, anchorLocal, tx, ct).ConfigureAwait(false);|(CooltimeAttendanceOutcome.NotAttempted, null, null, null);|$DB.G_C8_|1"
  "c|$ENGINE|                 AND cooltime_asked_at > UTC_TIMESTAMP(6) - INTERVAL 65 SECOND\",|                 \",|$DB.G_C6_|1"
  "d|$HR|check_out_source = CASE WHEN check_out_source = 'cooltime_blank' THEN NULL ELSE check_out_source END|check_out_source = check_out_source|$DB.G_C16_|1"
  "e|$NOTIFY|options.AccessTokenProvider = GetHubTokenAsync;|options.AccessTokenProvider = () => Task.FromResult<string?>(token);|$RULE.G_C18a_배선|0"
  # ⬛ [낡은 줄 · 11/28 나누기 이전] f = 「…t.ChangedAtUtc)) continue;」 → 「…null)) continue;」 · G_C19_
  #   🆕 11/28 HQA 반려 P1 — 회사 한 곳 함수(RunTenantAsync)로 옮겨 continue 가 return Tally() 가 되었다(같은 뜻)
  "f|$ENGINE|if (IsBeforeSettingChange(cUtc, t.ChangedAtUtc)) return Tally();|if (IsBeforeSettingChange(cUtc, null)) return Tally();|$DB.G_C19_|1"
  "g|$SETTINGS|if (wasEnabled != cooltime.Enabled)|if (!wasEnabled && cooltime.Enabled)|$DB.G_C20_|1"
  "h|$SETTINGS|if (wasEnabled != cooltime.Enabled)|if (wasEnabled && !cooltime.Enabled)|$DB.G_C20_|1"
  "i|$ENGINE|? UtcToServerLocal(missedCUtc)|? nowLocal|$DB.G_C21_|1"
  "k|$SESSION_MW|if (context.User?.Identity?.IsAuthenticated != true) { await _next(context); return; }|if (context.User?.Identity?.IsAuthenticated != true) { await _next(context); return; } if (context.Request.Path.StartsWithSegments(\"/api/auth/cooltime\")) { await _next(context); return; }|$DB.G_C14_|1"
  # ── 🆕 11/27 고침 차례(§273 · §275) — 갈래 ② 명세서 §8 ──
  "l|$ENGINE| AND cooltime_asked_at > UTC_TIMESTAMP(6) - INTERVAL 65 SECOND AND user_id = @JwtUserId\"| AND user_id = @JwtUserId\"|$DB.G_C14_|1"
  "m|$ENGINE|if (n == 1) await _pusher.ContinuedAsync(tenantId, sessionId, ct).ConfigureAwait(false);|if (n == 1) await Task.CompletedTask;|$DB.G_C14_|1"
  "n|$HR|AND check_out_source IN ('cooltime', 'cooltime_blank', 'edited')|AND check_out_source IN ('cooltime', 'cooltime_blank')|$DB.G_C12_|1"
  "o|$HR|if (newOut > DateTime.Now)|if (newOut > DateTime.MaxValue)|$DB.G_C12_|1"
  "p|$SCREEN|409 => CallOutcome.Recheck,   // [계속]|409 => CallOutcome.Ended,   // [계속]|$RULE.G_C14_화면_계속_응답|0"
  "q|$SCREEN|>= 200 and < 300 => ended == true ? CallOutcome.Ended : CallOutcome.Recheck,|>= 200 and < 300 => CallOutcome.Ended,|$RULE.V1_|0"
  "r|$SCREEN|foreach (var k in hit) body[k] = null;|foreach (var k in hit) { }|$RULE.V4_|0"
  "s|$SCREEN|or SourceCooltimeBlank or SourceEdited;|or SourceCooltimeBlank;|$RULE.C5_|0"
  "t|$SCREEN|if (active == false) return RecheckDecision.EndLocally;|if (active == false) { }|$RULE.V1_|0"
  # ── 🆕 11/28 고침 1(§277 P3 (b) · §285) — [지금 로그아웃] + 서버 안 끝냄 = 손 로그아웃 ──
  "u|$SCREEN|=> string.Equals(trigger, TriggerButton, StringComparison.Ordinal) && decision != RecheckDecision.EndLocally;|=> false;|$RULE.고침1_|0"
  "v|$PROMPT|await RecheckAsync(trigger);|await RecheckAsync();|$RULE.고침1_배선|0"
  "w|$PROMPT|await FinishWithLogoutAsync(CooltimeScreenRules.TriggerButton);|await FinishWithLogoutAsync(CooltimeScreenRules.TriggerCountdown);|$RULE.고침1_배선|0"
  # ── 🆕 11/28 다음 묶음(§288 · 대표 결재 §287 E-6 W=4) — docs/개발/erp/20261128_쿨타임1b_다음묶음_규칙4_마이그게이트_개발명세서.md §3 ──
  "j|$ENGINE|AND login_at < @CUtc + INTERVAL @W HOUR||$DB.G_C22_|1"
  "x|$ENGINE|return (CooltimeAttendanceOutcome.NoEmployee, null, null, null);|{ }|$DB.G_C23_|1"
  "y|$API_PROGRAM|builder.Services.AddHostedService<HitPan.API.Services.Cooltime.CooltimeService>();|// (변이 y) 쿨타임 서버 작업 배선 뺌|$RULE.G_C24_|0"
  "z|$MIG132|cooltime_changed_at DATETIME(6) NULL|cooltime_changed_at DATETIME NULL|$MIG.G_W13_7_|1"
  # ── 🆕 11/28 [3-V] S-3 · [4] P3 — 규칙 ④ 폭 경계 정확값(G-C22 「경계 login = C 정확」 · 「경계 login = C + 4h 정확」) · a~z 다 써서 두 글자 ──
  "aa|$ENGINE|login_at >= @CUtc|login_at > @CUtc|$DB.G_C22_|1"
  "ab|$ENGINE|login_at < @CUtc + INTERVAL @W HOUR|login_at <= @CUtc + INTERVAL @W HOUR|$DB.G_C22_|1"
  # ── 🆕 11/28 PM §290 S-4 — ④ 로그인도 E-2(저장 직후 C 건너뜀)를 따르는지: ⓕ 와 같은 바늘 · 대상 = G-C22 「④+저장 C 뒤」 줄 ──
  # ⬛ [낡은 줄 · 11/28 나누기 이전] ac = f 와 같은 바늘(continue;) · G_C22_ — 🆕 11/28 위 f 와 같은 까닭으로 return Tally()
  "ac|$ENGINE|if (IsBeforeSettingChange(cUtc, t.ChangedAtUtc)) return Tally();|if (IsBeforeSettingChange(cUtc, null)) return Tally();|$DB.G_C22_|1"
  # ── 🆕 11/28 HQA 반려 P1 설정 끄기 경주(설계 docs/설계/erp/쿨타임_설정끄기경주_고침_설계_20261128.md §6 변이 표 · 고침 커밋 뒤에만) ──
  #   M1 끝내기 거래 재확인 뺌 → G-C25 ⓐ · ⓒ · G-C26 ⓑ / M2 묻기 거래 재확인 뺌 → G-C25 ⓑ · ⓓ / M3 비교 칸을 켬만으로 → G-C25 ⓒ · ⓓ
  #   M4 공유 잠금 → 일반 SELECT → G-C26 ⓑ / M5 재확인을 세션 행 FOR UPDATE 뒤로(반대 순서) → G-C26 ⓐ
  #   한 줄 = 시험 하나(이 표는 필터 하나만 받는다) ⇒ 같은 변이를 대상 시험마다 한 줄씩 둔다.
  "M1a|$ENGINE|if (expected is not null && !await StillSameSettingAsync(expected, tx, ct).ConfigureAwait(false))|if (expected is not null && !await Task.FromResult(true).ConfigureAwait(false))|$DB.G_C25a_|1"
  "M1c|$ENGINE|if (expected is not null && !await StillSameSettingAsync(expected, tx, ct).ConfigureAwait(false))|if (expected is not null && !await Task.FromResult(true).ConfigureAwait(false))|$DB.G_C25c_|1"
  "M1-26b|$ENGINE|if (expected is not null && !await StillSameSettingAsync(expected, tx, ct).ConfigureAwait(false))|if (expected is not null && !await Task.FromResult(true).ConfigureAwait(false))|$DB.G_C26b_|1"
  "M2b|$ENGINE|if (!await StillSameSettingAsync(t, askTx, ct).ConfigureAwait(false))|if (!await Task.FromResult(true).ConfigureAwait(false))|$DB.G_C25b_|1"
  "M2d|$ENGINE|if (!await StillSameSettingAsync(t, askTx, ct).ConfigureAwait(false))|if (!await Task.FromResult(true).ConfigureAwait(false))|$DB.G_C25d_|1"
  "M3c|$ENGINE| AND cooltime_at <=> @At AND cooltime_changed_at <=> @Changed LOCK IN SHARE MODE\"| LOCK IN SHARE MODE\"|$DB.G_C25c_|1"
  "M3d|$ENGINE| AND cooltime_at <=> @At AND cooltime_changed_at <=> @Changed LOCK IN SHARE MODE\"| LOCK IN SHARE MODE\"|$DB.G_C25d_|1"
  "M4-26b|$ENGINE|<=> @Changed LOCK IN SHARE MODE\"|<=> @Changed\"|$DB.G_C26b_|1"
  "M5-26a|$ENGINE|if (expected is not null && !await StillSameSettingAsync(expected, tx, ct).ConfigureAwait(false))|if (expected is not null && await _db.ExecuteAsync(new CommandDefinition(\"SELECT session_id FROM user_sessions WHERE session_id = @Sid FOR UPDATE\", new { Sid = sessionId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false) > int.MinValue && !await StillSameSettingAsync(expected, tx, ct).ConfigureAwait(false))|$DB.G_C26a_|1"
)

want=" $* "
fails=0
for m in "${MUTATIONS[@]}"; do
  IFS='|' read -r name file from to test needdb <<< "$m"
  if [ "$#" -gt 0 ] && [[ "$want" != *" $name "* ]]; then continue; fi
  if [ "$needdb" = "1" ] && [ -z "${HITPAN_REQUIRE_DB:-}" ]; then
    echo "▶ [변이 $name] 건너뜀 — 시험 DB 없음(HITPAN_REQUIRE_DB). 이 변이는 확인 안 됐다."
    continue
  fi
  count=$(FROM="$from" perl -0ne 'my $n = () = /\Q$ENV{FROM}\E/g; print $n' "$file")
  if [ "$count" != "1" ]; then
    echo "::error::[변이 $name] 바꿀 글자가 $file 에 ${count}번 — 정확히 1번이어야 한다(제품이 바뀌었으면 이 표를 같이 고쳐라)"
    fails=$((fails + 1)); continue
  fi
  FROM="$from" TO="$to" perl -0pi -e 's/\Q$ENV{FROM}\E/$ENV{TO}/' "$file"
  echo "▶ [변이 $name] $file 변이 → 시험 $test (기대 = FAIL)"
  if ! dotnet build src/HitPan.Tests/HitPan.Tests.csproj --configuration Release --nologo -v q > "/tmp/ctm_mut_$name.build.log" 2>&1; then
    echo "::error::[변이 $name] 변이 빌드 실패 — 대조군이 아니라 컴파일 오류다"; tail -20 "/tmp/ctm_mut_$name.build.log"
    fails=$((fails + 1))
  elif dotnet test src/HitPan.Tests/HitPan.Tests.csproj --configuration Release --no-build --nologo \
         --filter "FullyQualifiedName~HitPan.Tests.Integrity.$test" > "/tmp/ctm_mut_$name.test.log" 2>&1; then
    echo "::error::[변이 $name] $test 가 변이 뒤에도 통과했다 — 이 게이트는 그 문장을 재지 못한다"
    grep -E "Passed|Failed|Total" "/tmp/ctm_mut_$name.test.log" | tail -3
    fails=$((fails + 1))
  elif grep -q "\[SKIP\]" "/tmp/ctm_mut_$name.test.log"; then
    echo "::error::[변이 $name] SKIP — 안 돈 것을 FAIL 로 셀 수 없다"; fails=$((fails + 1))
  else
    echo "✅ [변이 $name] $test FAIL 확인(기대대로)"
    grep -E "^\s+Failed |Assert|기대|대조군" "/tmp/ctm_mut_$name.test.log" | head -3
  fi
  git checkout -- "$file"
done

# 원복 확인 — 변이가 남아 있으면 안 된다
if ! git diff --quiet -- "$ENGINE" "$HR" "$SETTINGS" "$SESSION_MW" "$NOTIFY" "$SCREEN" "$PROMPT" "$API_PROGRAM" "$MIG132"; then
  echo "::error::변이 원복 실패 — 제품 파일에 변경이 남았다"; git diff --stat; exit 2
fi
if [ "$fails" -gt 0 ]; then echo "::error::변이 런 실패 $fails 건"; exit 1; fi
echo "변이 런 끝 — 실패 0"
