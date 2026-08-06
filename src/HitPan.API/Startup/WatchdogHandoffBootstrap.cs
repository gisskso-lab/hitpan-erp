using System.Diagnostics;
using System.Globalization;

namespace HitPan.API.Startup;

/// <summary>
/// W4-3-H (핸드오프) — 구버전 워치독(자기교체 능력 없음)이 깔린 고객 PC 에서, 새 API 가 대신
/// 워치독 교체를 성사시킨다. **고객 손 0번.** 재설치도 수동 조작도 없다(사장님 오더 2026-08-06).
///
/// ── 왜 API 가 이 일을 하는가 ─────────────────────────────────────────────────────
/// 1.2.52 이하 워치독은 업데이트 시 신버전 워치독을 {app}\watchdog.new 에 **배치는 하지만**
/// (UpdateOrchestrator.cs:344-352), 성공 확정 직후 CleanupAfterSuccess(:882) 가 그걸 **지운다**:
///     "신버전 워치독 예비물(W4-3 미교체). 고리5 전까지는 쓰지 않으므로 성공 시 치운다."
/// 즉 재료는 매번 고객 PC 까지 도착하는데, 쓰이지 않고 버려진다. 그래서 워치독은 영원히 구버전이고,
/// **워치독 코드에 대한 모든 봉합이 자동업데이트로는 절대 고객에게 도달하지 못한다.**
/// 사장님 지적 — "머 하나 제대로 수정된거 없이 버젼만 계속 올라가네" — 의 구조적 원인이 이것이다.
///
/// 구버전 워치독은 이미 고객 PC 에 있어 우리가 고칠 수 없다. 그러나 구버전 워치독이 **원래 하는 일**
/// 중에 우리가 코드를 실어 보낼 수 있는 통로가 하나 있다:
///     · 업데이트는 {app}\api 폴더를 통째로 신버전으로 교체한다.
///     · 그 뒤 schtasks /Run "HitPan-ERP-API-tenant-{slot}" 으로 API 를 재기동한다
///       (UpdateOrchestrator.cs:952 → installer/HitPan-Universal.iss:2354 가 /RU SYSTEM /RL HIGHEST 로 등록).
/// ⇒ **신버전 API 가 SYSTEM 권한으로 고객 PC 에서 실행된다.** 여기서 인계받는다.
///
/// ── 타이밍 (여기가 핵심) ─────────────────────────────────────────────────────────
/// 구버전 워치독의 실제 순서(UpdateOrchestrator.cs:207~231):
///     api 교체 → **API 재기동(여기서 우리가 뜬다)** → web 재기동 → 검증(최대 60초) → watchdog.new 삭제
/// 즉 우리가 뜨는 시점엔 watchdog.new 가 **아직 있고**, 약 1분 뒤 **지워진다.**
/// 그래서 "2분 뒤 교체 예약"만 걸면 그때는 재료가 이미 없다 — 예약이 헛돈다.
/// **[3-V] 병렬검증 C5 적발.** 봉합: 예약을 걸기 전에 **재료를 먼저 우리 소유 폴더로 복사한다.**
/// 복사본은 구버전 워치독이 모르는 경로라 지워지지 않는다.
///
/// ── 안전 원칙 (헌법 #20·#30) ────────────────────────────────────────────────────
/// 최악은 "업데이트 실패"가 아니라 **워치독이 죽은 채 남아 그 PC 가 영구히 업데이트를 못 받는 것**이다.
/// 본사가 손쓸 방법이 0 이기 때문이다. 그래서:
///   · 이 코드의 어떤 실패도 **API 기동을 막지 않는다** (ERP 가 안 뜨면 그게 더 큰 사고 — 헌법 #20).
///     전 구간 try/catch + 백그라운드 실행. 예외는 삼키되 침묵하지 않는다(헌법 #15).
///   · 부팅 복구 안전망을 **교체보다 먼저** 등록하고, 실패하면 교체를 시작조차 않는다.
///   · 재료 검증은 폴더 존재가 아니라 **EXE 존재**로 한다.
///   · 이미 같은 버전이면 아무것도 하지 않는다(무한 재교체 차단).
/// </summary>
internal static class WatchdogHandoffBootstrap
{
    // ★ 봉합 [3-V] C3-1 (P0) — 워치독 본체의 자기교체 작업명과 **절대 겹치면 안 된다.**
    //   본체: UpdateOrchestrator.cs:1016 "HitPanWatchdogSelfReplace" / :1019 "…Recover"
    //   같은 이름을 쓰면 두 사고가 난다:
    //     ① schtasks /Create /F 가 서로를 덮어쓴다(먼저 건 예약이 소리 없이 사라진다).
    //     ② 교체로 올라온 신버전 워치독이 기동 시 CleanupStaleSelfReplaceArtifacts(Worker.cs:108)
    //        를 도는데, 그건 `watchdog.new` 존재만 본다(UpdateOrchestrator.cs:1268). 우리 재료는
    //        `watchdog.handoff` 라 그에게 안 보인다 → "잔재"로 오판해 **부팅 복구 안전망을 지운다**(:1273).
    //        아직 교체 완료 판정 전인데 안전망이 사라진다 = 그 순간 정전이면 영구 고립.
    //   그래서 이름 공간을 분리한다. 두 체계는 서로를 건드리지 않는다.
    private const string SwapTaskName = "HitPanWatchdogHandoffSwap";
    private const string RecoverTaskName = "HitPanWatchdogHandoffRecover";
    private const string ServiceName = "HitPanWatchdog";
    private const string GuardianTaskName = "HitPanWatchdogGuardian";

    /// <summary>구버전 워치독이 배치하는 재료 폴더(그가 곧 지운다).</summary>
    private const string SourceDirName = "watchdog.new";

    /// <summary>우리가 복사해 두는 폴더. 구버전 워치독은 이 이름을 모르므로 지우지 않는다.</summary>
    private const string StageDirName = "watchdog.handoff";

    /// <summary>
    /// 기동 시 1회 호출. **절대 블로킹하지 않는다** — 백그라운드로 던지고 즉시 반환한다.
    /// schtasks 호출이 수 초 걸릴 수 있는데, 그만큼 ERP 기동이 늦어지면 안 된다.
    /// </summary>
    public static void ScheduleIfPending(ILogger logger)
    {
        if (!OperatingSystem.IsWindows()) return;

        _ = Task.Run(() =>
        {
            try
            {
                Run(logger);
            }
            catch (Exception ex)
            {
                // 여기서 무엇이 터지든 ERP 는 계속 산다. 침묵은 금지(헌법 #15).
                logger.LogWarning(ex, "[W4-3-H] 워치독 핸드오프 처리 중 예외 — ERP 동작에는 영향 없음(구버전 워치독 유지)");
            }
        });
    }

    private static void Run(ILogger logger)
    {
        var appRoot = ResolveAppRoot();
        if (appRoot is null)
        {
            logger.LogDebug("[W4-3-H] 설치 루트를 판정할 수 없음 — 핸드오프 건너뜀(개발 환경으로 간주).");
            return;
        }

        // ★ 재봉합 [4] 검증팀 P1-2 — 한 PC 에 슬롯이 여러 개면 API 프로세스도 여러 개다.
        //   슬롯별로 갈라지는 건 포트·DB·데이터 폴더뿐이고 **{app}\api 바이너리와 {app}\watchdog 은 공유**한다.
        //   그래서 여러 API 가 동시에 같은 watchdog.handoff 를 복사·삭제하고 같은 카운터를 갱신한다
        //   (TryCopyDirectory 가 다른 프로세스의 복사 중인 폴더를 통째로 지우는 구간이 열린다).
        //   전역 뮤텍스로 한 번에 하나만 통과시킨다. 못 잡으면 다른 API 가 이미 하고 있다는 뜻이라 그냥 물러난다.
        using var gate = new Mutex(initiallyOwned: false, @"Global\HitPanWatchdogHandoff", out _);
        var held = false;
        try
        {
            try { held = gate.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { held = true; }   // 앞 프로세스가 죽으며 남긴 것 — 우리가 이어받는다.

            if (!held)
            {
                logger.LogDebug("[W4-3-H] 다른 인스턴스가 처리 중 — 이번 기동에서는 건너뜁니다(정상).");
                return;
            }
            RunCore(appRoot, logger);
        }
        finally
        {
            if (held) { try { gate.ReleaseMutex(); } catch (Exception ex) { logger.LogWarning(ex, "[W4-3-H] 뮤텍스 해제 실패"); } }
        }
    }

    private static void RunCore(string appRoot, ILogger logger)
    {

        var stageDir = Path.Combine(appRoot, StageDirName);
        var sourceDir = Path.Combine(appRoot, SourceDirName);

        // ── ⓪ 상대 체계 능력 판정 — 스스로 할 수 있으면 우리는 물러난다 ──────────────
        //   ★ 재봉합 [4] 재검증 P2-2 (P0) — 이 판정이 없으면 **같은 서비스를 두 번 교체**한다.
        //
        //   핸드오프는 구버전(자기교체 불가) 워치독을 위한 **과도기 코드**다. 그런데 W4-3 을 내장한
        //   워치독(1.2.54 이상)이 이미 도는 PC 에서도 그대로 동작하면:
        //     · 우리:   API 기동 시 watchdog.handoff 복사 → T+2분 HandoffSwap 예약
        //     · 워치독: CleanupAfterSuccess 에서 watchdog.new 로 → T+2분 SelfReplace 예약
        //   두 예약이 1분 안팎으로 각자 발화해 **같은 서비스를 stop/start 하고 같은 폴더를 Move** 한다.
        //   이름공간 분리는 schtasks 덮어쓰기만 막을 뿐, 서비스와 {app}\watchdog 은 보호되지 않는다.
        //   (핸드오프는 watchdog.new 를 지우지 않으므로 양쪽 재료가 모두 살아 있어 둘 다 발동한다.)
        //
        //   판정 근거: 지금 **돌고 있는** 워치독이 자기교체 능력을 가졌는가.
        //   W4-3 은 1.2.54 부터다. 그 이상이면 워치독이 스스로 하므로 우리는 손대지 않는다.
        //   = 이 코드의 유효기간·해제조건이기도 하다(직전 반려 사유).
        var runningWatchdogExe = Path.Combine(appRoot, "watchdog", "HitPan.Watchdog.exe");
        var runningVer = TryGetFileVersion(runningWatchdogExe);
        if (runningVer is not null && HasSelfReplaceCapability(runningVer))
        {
            logger.LogInformation(
                "[W4-3-H] 현재 워치독({Ver})은 스스로 교체할 수 있습니다 — 핸드오프는 물러납니다(이중 교체 방지).",
                runningVer);
            return;
        }

        // ── ① 재료 확보 ────────────────────────────────────────────────────────────
        //   구버전 워치독이 지우기 전에 우리 폴더로 복사한다. 이미 복사해 둔 게 있으면 그걸 쓴다
        //   (교체가 한 번 실패했거나, 예약만 되고 재부팅된 경우 — 재료가 남아 있어야 재시도된다).
        if (!HasWatchdogExe(stageDir))
        {
            if (!HasWatchdogExe(sourceDir))
            {
                logger.LogDebug("[W4-3-H] 교체할 신버전 워치독 없음 — 정상(이미 최신이거나 업데이트 직후가 아님).");
                return;
            }

            // 봉합 [3-V] C5-잔여 (P1): 구버전 워치독의 CleanupAfterSuccess 가 watchdog.new 를 지우는
            //   시점과 이 복사가 겹칠 수 있다(워치독 EXE 는 수십 MB 자기포함 배포라 복사가 수 초 걸린다).
            //   삭제가 먼저 닿으면 File.Copy 가 터진다. 한 번 실패했다고 포기하면 "이번에도 안 바뀜"이
            //   반복되므로 짧게 재시도한다. 그래도 안 되면 다음 업데이트 라운드에서 다시 시도된다.
            var copied = false;
            for (var i = 0; i < 3 && !copied; i++)
            {
                if (i > 0) Thread.Sleep(2000);
                copied = TryCopyDirectory(sourceDir, stageDir, logger);
                if (!copied && !Directory.Exists(sourceDir))
                {
                    logger.LogWarning("[W4-3-H] 재료 폴더가 사라졌습니다(구버전 워치독이 정리함) — 이번 라운드는 포기합니다.");
                    break;
                }
            }
            if (!copied)
            {
                logger.LogWarning("[W4-3-H] 신버전 워치독 확보 실패 — 구버전 워치독을 그대로 둡니다(다음 업데이트에 재시도).");
                return;
            }
            logger.LogInformation("[W4-3-H] 신버전 워치독을 {Dir} 로 확보했습니다(구버전 워치독의 정리 대상에서 벗어남).", StageDirName);
        }

        // ── ② 이미 같은 버전이면 교체하지 않는다 (무한 재교체 차단) ──────────────────
        //   [3-V] C3 대비. 폴더 존재만으로 판정하면 매 기동마다 워치독을 재시작하게 된다.
        var currentVer = TryGetFileVersion(Path.Combine(appRoot, "watchdog", "HitPan.Watchdog.exe"));
        var newVer = TryGetFileVersion(Path.Combine(stageDir, "HitPan.Watchdog.exe"));
        if (newVer is null)
        {
            logger.LogWarning("[W4-3-H] 신버전 워치독 버전을 읽지 못했습니다 — 교체 중단(구버전 유지).");
            return;
        }
        // ★ 재봉합 [4] 검증팀 P0-2 — 문자열 동등비교는 표기 차이에 무너진다.
        //   릴리스 표기는 3자리(1.2.55)인데 FileVersion 은 4자리(1.2.55.0)다. '1.2.55' == '1.2.55.0' 은 False.
        //   양쪽 다 FileVersion 을 읽으므로 지금은 우연히 맞지만, 한쪽이라도 표기가 달라지면
        //   "이미 같은 버전" 가드가 뚫려 매 기동마다 워치독을 다시 교체한다.
        //   ⇒ System.Version 으로 정규화해 비교한다(파싱 실패 시에만 문자열 비교로 폴백).
        if (currentVer is not null && IsSameVersion(currentVer, newVer))
        {
            logger.LogInformation("[W4-3-H] 워치독이 이미 {Ver} 입니다 — 교체 불필요. 대기 재료를 정리합니다.", newVer);
            TryDeleteDirectory(stageDir, logger);
            return;
        }

        // ── ②-b 재시도 상한 (봉합 [3-V] C3-3, P1) ────────────────────────────────
        //   교체가 구조적으로 실패하는 PC(백신 폴더 잠금 등)에서는 API 기동 때마다 재예약되고,
        //   그때마다 워치독이 30초 정지 시도를 반복한다. keepalive·Guardian 과 맞물려 계속 오르내린다.
        //   3회까지만 시도하고 그 뒤엔 재료를 치운다 — 구버전 워치독으로 조용히 사는 편이 낫다.
        //   (다음 업데이트가 새 watchdog.new 를 보내면 카운터와 함께 처음부터 다시 시작한다.)
        //   ★ 재봉합 [4] P0-2: 카운터를 stageDir **밖**에 둔다.
        //     stageDir 은 교체 시 Move-Item 으로 통째로 사라지고, 그 안의 카운터는 살아있는
        //     watchdog\ 폴더로 딸려 들어간다(검증팀 실측). 그러면 상한 판정이 리셋되고
        //     운영 폴더에 잔재가 남는다. scripts\ 는 교체 대상이 아니라 안전하다.
        var attemptFile = Path.Combine(appRoot, "scripts", ".handoff-attempts");
        try { Directory.CreateDirectory(Path.Combine(appRoot, "scripts")); }
        catch (Exception ex) { logger.LogWarning(ex, "[W4-3-H] scripts 폴더 준비 실패 — 시도 상한이 느슨해질 수 있습니다."); }
        var attempts = 0;
        try
        {
            if (File.Exists(attemptFile) && int.TryParse(File.ReadAllText(attemptFile).Trim(), out var n))
                attempts = n;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[W4-3-H] 시도 횟수 판독 실패 — 0 으로 간주합니다.");
        }

        if (attempts >= 3)
        {
            logger.LogWarning(
                "[W4-3-H] 워치독 교체를 {N}회 시도했으나 완료되지 않았습니다 — 재시도를 멈추고 재료를 치웁니다. " +
                "구버전 워치독({Cur})으로 계속 동작하며, 다음 업데이트에서 다시 시도합니다.", attempts, currentVer ?? "(판독불가)");
            TryDeleteDirectory(stageDir, logger);
            return;
        }

        try { File.WriteAllText(attemptFile, (attempts + 1).ToString(CultureInfo.InvariantCulture)); }
        catch (Exception ex) { logger.LogWarning(ex, "[W4-3-H] 시도 횟수 기록 실패 — 상한 판정이 느슨해질 수 있습니다."); }

        logger.LogInformation("[W4-3-H] 워치독 교체 대상: {Cur} → {New} (시도 {N}/3). 예약을 등록합니다.",
            currentVer ?? "(판독불가)", newVer, attempts + 1);

        // ── ③ 부팅 복구 안전망을 **먼저** 등록한다 ──────────────────────────────────
        //   이게 없는 상태에서 교체 도중 정전이 나면 그 PC 는 워치독을 영구히 잃는다.
        //   등록 실패 시 교체를 시작조차 하지 않는다 — 바닥 없이 뛰지 않는다.
        var scriptDir = Path.Combine(appRoot, "scripts");
        try { Directory.CreateDirectory(scriptDir); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[W4-3-H] scripts 폴더 생성 실패 — 교체 중단(구버전 유지).");
            return;
        }

        var recoverPs1 = Path.Combine(scriptDir, "RecoverWatchdogHandoff.ps1");
        var swapPs1 = Path.Combine(scriptDir, "SelfReplaceWatchdogHandoff.ps1");
        // ★ 정정 [4] 재검증 P2 — 종전 주석은 "순수 ASCII 라 인코딩 사고 여지가 없다"였으나 **사실이 아니었다.**
        //   실측: swap 5,572B 중 1,805B, recover 2,744B 중 912B 가 비ASCII(한글 주석)다.
        //   BOM 없는 UTF-8 을 PS 5.1 이 CP949 로 읽으면 한글이 깨지고, 깨진 바이트가 따옴표 짝을 무너뜨리면
        //   스크립트 전체가 파싱 실패한다 — 이번 세션에서만 네 번 겪은 함정이다.
        //   지금은 주석 행 안에서만 깨져 우연히 파싱이 통과하지만, 주석 한 줄만 늘어도 사고가 된다.
        //   ⇒ BOM 을 넣어 인코딩을 명시한다. "우연히 괜찮은 전제"에 기대지 않는다.
        var enc = new System.Text.UTF8Encoding(true);
        try
        {
            File.WriteAllText(recoverPs1, BuildRecoverScript(appRoot), enc);
            File.WriteAllText(swapPs1, BuildSwapScript(appRoot), enc);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[W4-3-H] 교체 스크립트 작성 실패 — 교체 중단(구버전 유지).");
            return;
        }

        var recoverCode = RunSchtasks(
            $"/Create /F /TN \"{RecoverTaskName}\" /TR \"{PsRunner(recoverPs1)}\" /SC ONSTART /RU SYSTEM /RL HIGHEST",
            logger);
        if (recoverCode != 0)
        {
            logger.LogWarning(
                "[W4-3-H] 부팅 복구 안전망 등록 실패(exit={Code}) — 교체를 시작하지 않습니다(구버전 유지). " +
                "안전망 없이 교체하면 교체 도중 정전 시 그 PC 는 워치독을 영구히 잃습니다.", recoverCode);
            return;
        }

        // ── ④ 교체 본체 예약 ──────────────────────────────────────────────────────
        //   2분 뒤. 그 사이 구버전 워치독은 검증·정리를 끝내고 조용해진다(우리 재료는 별도 폴더라 무사하다).
        //   /SD 로 날짜까지 지정한다 — /ST 만 주면 23:59+2분이 "오늘 00:01"(과거)로 등록돼 즉시 실행된다.
        //   InvariantCulture 필수: ko-KR 은 '/' 를 '-' 로 렌더해 schtasks 가 인수를 거부한다.
        var runAt = DateTime.Now.AddMinutes(2);
        var sd = runAt.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        var st = runAt.ToString("HH:mm", CultureInfo.InvariantCulture);

        var swapCode = RunSchtasks(
            $"/Create /F /TN \"{SwapTaskName}\" /TR \"{PsRunner(swapPs1)}\" /SC ONCE /SD {sd} /ST {st} /RU SYSTEM /RL HIGHEST",
            logger);
        if (swapCode != 0)
        {
            // 교체는 못 걸었지만 안전망만 남으면 매 부팅 헛돈다. 되돌린다.
            RunSchtasks($"/Delete /TN \"{RecoverTaskName}\" /F", logger);
            logger.LogWarning("[W4-3-H] 교체 예약 실패(exit={Code}) — 안전망을 되돌리고 구버전 워치독을 유지합니다.", swapCode);
            return;
        }

        logger.LogInformation(
            "[W4-3-H] ✅ 워치독 자동 교체 예약 완료 — {At} 에 {Cur} → {New} 로 교체됩니다(고객 조작 0).",
            runAt.ToString("HH:mm", CultureInfo.InvariantCulture), currentVer ?? "(판독불가)", newVer);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  교체 스크립트 — 실행 중인 워치독은 자기 자신을 못 바꾼다. 외부(예약작업)가 대신 한다.
    //  "멈춤 → 바꿈 → 켬", 실패하면 반드시 되돌린다. ASCII 전용.
    // ══════════════════════════════════════════════════════════════════════════════
    private static string BuildSwapScript(string appRoot)
    {
        var lines = new[]
        {
            "$ErrorActionPreference = 'Stop'",
            $"$app  = '{appRoot.Replace("'", "''")}'",
            $"$svc  = '{ServiceName}'",
            $"$grd  = '{GuardianTaskName}'",
            "$cur  = Join-Path $app 'watchdog'",
            $"$new  = Join-Path $app '{StageDirName}'",
            "$bak  = Join-Path $app 'watchdog.bak'",
            "",
            "# Guardian 은 5분마다 '멈춘 워치독'을 되살린다. 교체 도중 되살아나면 파일이 잠겨 교체가 깨진다.",
            "# 그래서 잠시 끄고, 어떤 경로로 끝나든 반드시 다시 켠다(3중 복원: 정상/실패/부팅복구).",
            "# ★ 재봉합 [4] 3차 P0 — PS 5.1 에서 native exe 의 stderr 를 '2>$null' 로 리디렉션하면",
            "#   각 줄이 ErrorRecord 로 승격되고, $ErrorActionPreference='Stop' 이 그걸 종료 오류로 만든다.",
            "#   그래서 '없는 예약작업 조회' 같은 정상적 실패에서도 **그 자리에서 스크립트가 죽는다**(exit 1).",
            "#   하필 죽는 지점이 'Guardian 이 없으면 만든다'는 폴백의 진입 조건문이라,",
            "#   **이 기능의 유일한 대상(Guardian 없는 구버전 PC)에서만 100% 실패**했다(검증팀 실측).",
            "#   ⇒ 모든 schtasks 호출을 이 헬퍼로 통일한다. stderr 를 파일로 빼 ErrorRecord 승격을 원천 차단하고",
            "#     종료코드만 반환한다.",
            "#   ※ 정정 [4] 4차: Start-Process 는 CMD 를 거치지 않는다. Win32 CreateProcess 의 핸들 리디렉션이라",
            "#     애초에 PowerShell 오류 파이프를 타지 않는 것이다(결과는 같으나 근거 설명이 틀렸었다).",
            "",
            "# ★ 재봉합 [4] 4차 P2-A/P2-B — $env:TEMP 가 빈 값이거나 없는 드라이브면",
            "#   New-Item 은 ParameterBindingValidationException, Join-Path 는 DriveNotFoundException 을 던진다.",
            "#   -ErrorAction SilentlyContinue 는 **바인딩·경로해석 오류를 못 막는다.** 여기는 try 블록 앞이라",
            "#   그대로 즉사하고 GuardianOn 도 안 돈다. ⇒ 유효성을 먼저 보고 폴백한다.",
            "$__tmp = $env:TEMP",
            "if ([string]::IsNullOrWhiteSpace($__tmp)) { $__tmp = $null }",
            "if ($null -ne $__tmp) { try { $null = New-Item -ItemType Directory -Force -Path $__tmp -ErrorAction SilentlyContinue } catch { $__tmp = $null } }",
            "if ($null -eq $__tmp -or -not (Test-Path -LiteralPath $__tmp)) {",
            "  # SYSTEM 계정의 진실원. 워치독·API 는 SYSTEM 으로 도므로 사실상 여기가 정답이다.",
            "  $__tmp = Join-Path $env:SystemRoot 'Temp'",
            "  try { $null = New-Item -ItemType Directory -Force -Path $__tmp -ErrorAction SilentlyContinue } catch { }",
            "}",
            "$__tsErr = $null",
            "try { $__tsErr = Join-Path $__tmp 'hitpan-handoff-schtasks.err' } catch { $__tsErr = $null }",
            "",
            "function Sch([string]$a) {",
            "  # 어떤 이유로든 stderr 파일 경로를 못 잡으면 리디렉션 없이 돈다 —",
            "  #   그때는 Out-Null 로 stdout 만 삼키고, 오류는 $ErrorActionPreference 를 잠시 완화해 흘려보낸다.",
            "  try {",
            "    if ([string]::IsNullOrWhiteSpace($__tsErr)) {",
            "      $prev = $ErrorActionPreference",
            "      $ErrorActionPreference = 'Continue'",
            "      try { & schtasks.exe @($a -split ' ') | Out-Null; return $LASTEXITCODE }",
            "      finally { $ErrorActionPreference = $prev }",
            "    }",
            "    $p = Start-Process -FilePath 'schtasks.exe' -ArgumentList $a -NoNewWindow -Wait -PassThru `",
            "          -RedirectStandardOutput 'NUL' -RedirectStandardError $__tsErr -ErrorAction SilentlyContinue",
            "    if ($null -eq $p) { return 1 }",
            "    return $p.ExitCode",
            "  } catch { return 1 }",
            "}",
            "function GuardianOn { $null = Sch \"/Change /TN `\"$grd`\" /ENABLE\" }",
            "function SwapDir($from, $to) {",
            "  # Move-Item 은 대상이 있으면 '안으로' 밀어넣는다(중첩). 반드시 먼저 비운다.",
            "  if (Test-Path $to) { Remove-Item $to -Recurse -Force }",
            "  Move-Item $from $to -Force",
            "}",
            "",
            "if (-not (Test-Path (Join-Path $new 'HitPan.Watchdog.exe'))) { exit 0 }",
            "",
            "# ★ 봉합 [3-V] C1-1 (P0) — Guardian 이 없는 PC 가 실제로 존재한다.",
            "#   1.2.52 설치본(InstallWatchdog.ps1)은 Start-Service(:39) 가 Guardian 등록(:66) 보다 앞서고",
            "#   $ErrorActionPreference='Stop'(:9) 이라, 기동이 한 번 실패하면 스크립트가 즉사해 Guardian 이",
            "#   아예 안 깔린다. 게다가 등록이 CIM 기반(Register-ScheduledTask)이라 권한 거부로도 실패한다.",
            "#   (이 둘은 2026-08-04 ae18dc0 에서야 봉합됐다 — 1.2.52 는 07-31 이다.)",
            "#   그런데 sc failure(restart/5s)는 **비정상 종료에만** 반응한다. 아래 Stop-Service 는 정상 정지라",
            "#   되살려 주지 않는다. 즉 Guardian 이 없으면 재기동 실패 = 재부팅 전까지 아무도 안 살린다.",
            "#   ⇒ 교체를 시작하기 전에 Guardian 실재를 확인하고, 없으면 우리가 만들어 둔다.",
            "$grdPs1 = Join-Path $app 'scripts\\GuardianHandoff.ps1'",
            "if ((Sch \"/Query /TN `\"$grd`\"\") -ne 0) {",
            "  # ★ 재봉합 [4] 검증팀 P0-1 — 여기 큰따옴표를 쓰면 $s·$null 이 **이 스크립트 실행 시점에**",
            "  #   확장돼 빈 문자열이 되고, 생성된 파일은 ' = Get-Service ...' 같은 죽은 코드가 된다.",
            "  #   그런데도 schtasks 는 내용을 검증하지 않아 등록이 성공(exit 0)하고, 실패가 은폐된다.",
            "  #   ⇒ 2층 안전망이 '있는 것처럼 보이지만 실제로는 없는' 최악의 상태. 반드시 단일따옴표",
            "  #     히어스트링으로 **원문 그대로** 써야 한다.",
            "  $body = @'",
            "$ErrorActionPreference = 'SilentlyContinue'",
            "$s = Get-Service -Name '__SVC__' -ErrorAction SilentlyContinue",
            "if ($null -ne $s -and $s.Status -ne 'Running') { Start-Service -Name '__SVC__' -ErrorAction SilentlyContinue }",
            "'@",
            "  $body = $body.Replace('__SVC__', $svc)",
            "  Set-Content -Path $grdPs1 -Value $body -Encoding ASCII -ErrorAction SilentlyContinue",
            "",
            "  # 만들어 놓고 끝내지 않는다 — **실제로 동작하는 파일인지** 확인한다(P0-1 재발 차단).",
            "  #   ① 파일이 있고 ② 파싱되고 ③ Start-Service 호출이 실재해야 안전망으로 인정한다.",
            "  # ★ 재봉합 [4] 재검증 P1 — PSParser 타입 참조는 ConstrainedLanguage(WDAC/AppLocker)에서",
            "  #   'Cannot create type' 로 **차단**된다. $ErrorActionPreference='Stop' 이라 그 자리에서",
            "  #   스크립트가 즉사하고, 위치가 try 블록 앞이라 catch 도 GuardianOn 도 안 돈다(exit 1).",
            "  #   ⇒ 타입 참조 없이 **문자열 검사만**으로 판정한다. 그래도 죽은 스크립트(변수가 먹힌 경우)는",
            "  #     '$s = Get-Service' 패턴이 사라지므로 확실히 걸러진다 — 이게 P0-1 의 실제 실패 모습이었다.",
            "  $grdOk = $false",
            "  try {",
            "    if (Test-Path $grdPs1) {",
            "      $txt = Get-Content $grdPs1 -Raw",
            "      if ($txt -match '\\$s\\s*=\\s*Get-Service' -and $txt -match 'Start-Service' -and $txt -match [regex]::Escape($svc)) {",
            "        $grdOk = $true",
            "      }",
            "    }",
            "  } catch { $grdOk = $false }",
            "  if (-not $grdOk) {",
            "    # 안전망 파일이 온전하지 않다 = 실패 시 되살릴 수단이 없다. 교체하지 않는다(구버전 유지).",
            "    exit 0",
            "  }",
            "",
            "  # schtasks.exe 로 등록한다 — CIM(Register-ScheduledTask)은 권한 거부 전례가 있다.",
            "  $tr = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \\\"' + $grdPs1 + '\\\"'",
            "  if ((Sch \"/Create /F /TN `\"$grd`\" /TR `\"$tr`\" /SC MINUTE /MO 5 /RU SYSTEM /RL HIGHEST\") -ne 0) {",
            "    exit 0",
            "  }",
            "}",
            "",
            "$null = Sch \"/Change /TN `\"$grd`\" /DISABLE\"",
            "$null = Sch \"/End /TN `\"$grd`\"\"",
            "",
            "try {",
            "  # ① 멈춘다. 안 멈추면 무리해서 바꾸지 않는다 — 다음 기회에 다시 한다.",
            "  Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue",
            "  $ok = $false",
            "  for ($i = 0; $i -lt 30; $i++) {",
            "    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue",
            "    if ($null -eq $s -or $s.Status -eq 'Stopped') { $ok = $true; break }",
            "    Start-Sleep -Seconds 1",
            "  }",
            "  if (-not $ok) { GuardianOn; exit 0 }",
            "",
            "  # ② 바꾼다. 구버전은 버리지 않고 보관한다 — 되돌릴 자산이다.",
            "  if (Test-Path $bak) { Remove-Item $bak -Recurse -Force }",
            "  if (Test-Path $cur) { SwapDir $cur $bak }",
            "  SwapDir $new $cur",
            "",
            "  # ③ 켠다.",
            "  Start-Service -Name $svc -ErrorAction Stop",
            "  Start-Sleep -Seconds 3",
            "  $s = Get-Service -Name $svc -ErrorAction SilentlyContinue",
            "  if ($null -eq $s -or $s.Status -ne 'Running') { throw 'service did not start' }",
            "",
            "  # 성공 — 보관본과 안전망을 치운다.",
            "  if (Test-Path $bak) { Remove-Item $bak -Recurse -Force -ErrorAction SilentlyContinue }",
            $"  $null = Sch \"/Delete /TN `\"{RecoverTaskName}`\" /F\"",
            "  GuardianOn",
            "  exit 0",
            "}",
            "catch {",
            "  # ④ 되돌린다. 새 워치독이 고장났어도 그 PC 는 구버전으로 계속 살아야 한다.",
            "  try {",
            "    if (Test-Path $bak) {",
            "      if (Test-Path $cur) { Remove-Item $cur -Recurse -Force -ErrorAction SilentlyContinue }",
            "      Move-Item $bak $cur -Force",
            "    }",
            "    Start-Service -Name $svc -ErrorAction SilentlyContinue",
            "  } catch { }",
            "  GuardianOn",
            "  exit 1",
            "}",
        };
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>
    /// 부팅 복구 — 교체 도중 정전/강제재부팅이 나면 여기서 되살린다.
    /// 폴더 존재가 아니라 **EXE 존재**로 판정한다(빈 폴더를 정상으로 오판하면 그 PC 는 워치독을 잃는다).
    /// Guardian 복원을 **가장 먼저** 한다 — 그것만 살아나도 5분 뒤 워치독이 부활한다.
    /// </summary>
    private static string BuildRecoverScript(string appRoot)
    {
        var lines = new[]
        {
            "$ErrorActionPreference = 'SilentlyContinue'",
            $"$app = '{appRoot.Replace("'", "''")}'",
            $"$svc = '{ServiceName}'",
            $"$grd = '{GuardianTaskName}'",
            "$cur = Join-Path $app 'watchdog'",
            "$bak = Join-Path $app 'watchdog.bak'",
            $"$new = Join-Path $app '{StageDirName}'",
            "",
            "# 무엇보다 먼저 Guardian 을 켠다 — 이 스크립트가 뒤에서 실패해도 5분 뒤 워치독이 살아난다.",
            "# swap 과 동일한 안전 호출. 여기는 $ErrorActionPreference='SilentlyContinue' 라 즉사하진 않지만,",
            "#   빨간 NativeCommandError 블록이 이벤트로그를 오염시키므로 같은 방식으로 통일한다.",
            "#   ※ [4] 4차 P2-A 동일 봉합: $env:TEMP 가 빈 값·없는 드라이브면 Join-Path 가 던진다.",
            "$__tmp = $env:TEMP",
            "if ([string]::IsNullOrWhiteSpace($__tmp) -or -not (Test-Path -LiteralPath $__tmp)) { $__tmp = Join-Path $env:SystemRoot 'Temp' }",
            "$__tsErr = $null",
            "try { $__tsErr = Join-Path $__tmp 'hitpan-handoff-recover.err' } catch { $__tsErr = $null }",
            "function Sch([string]$a) {",
            "  try {",
            "    if ([string]::IsNullOrWhiteSpace($__tsErr)) { & schtasks.exe @($a -split ' ') | Out-Null; return $LASTEXITCODE }",
            "    $p = Start-Process -FilePath 'schtasks.exe' -ArgumentList $a -NoNewWindow -Wait -PassThru `",
            "          -RedirectStandardOutput 'NUL' -RedirectStandardError $__tsErr -ErrorAction SilentlyContinue",
            "    if ($null -eq $p) { return 1 }",
            "    return $p.ExitCode",
            "  } catch { return 1 }",
            "}",
            "$null = Sch \"/Change /TN `\"$grd`\" /ENABLE\"",
            "",
            "$exe = Join-Path $cur 'HitPan.Watchdog.exe'",
            "if (-not (Test-Path $exe)) {",
            "  # 현행 폴더가 비었다 = 교체 도중에 끊겼다.",
            "  # ① 보관본(구버전)이 성하면 그걸로 되돌린다 — 되돌리는 쪽이 항상 안전하다.",
            "  if (Test-Path (Join-Path $bak 'HitPan.Watchdog.exe')) {",
            "    if (Test-Path $cur) { Remove-Item $cur -Recurse -Force }",
            "    Move-Item $bak $cur -Force",
            "  }",
            "  # ② 보관본이 없고 신버전 재료만 성하면 그걸로 세운다 (봉합 [3-V] C1-2).",
            "  #   'SwapDir $cur $bak' 직후 정전이면 ①이 잡지만, 'Remove-Item $bak' 와 그 사이에",
            "  #   끊긴 경우엔 되돌릴 보관본이 없다. 워치독 본체의 복구(UpdateOrchestrator.cs:1227)와",
            "  #   동등하게 신버전 분기를 둔다 — 아무것도 없는 것보다 신버전이라도 서는 게 낫다.",
            "  elseif (Test-Path (Join-Path $new 'HitPan.Watchdog.exe')) {",
            "    # ★ 재봉합 [4] P1-1 — 이 스크립트는 $ErrorActionPreference='SilentlyContinue' 라",
            "    #   Remove-Item 실패가 조용히 무시된다. 그 상태로 Copy-Item -Recurse 를 돌리면",
            "    #   대상 '안으로' 중첩돼(watchdog\\watchdog.handoff\\...) 워치독이 영영 안 선다.",
            "    #   swap 쪽 SwapDir 와 동일하게 '지우고 → 없어졌는지 확인하고 → 복사' 로 통일한다.",
            "    if (Test-Path $cur) { Remove-Item $cur -Recurse -Force -ErrorAction SilentlyContinue }",
            "    if (-not (Test-Path $cur)) {",
            "      Copy-Item $new $cur -Recurse -Force -ErrorAction SilentlyContinue",
            "      # 복사가 실제로 성사됐는지 재확인 — 안 됐으면 다음 부팅에 다시 시도한다.",
            "      if (-not (Test-Path (Join-Path $cur 'HitPan.Watchdog.exe'))) {",
            "        if (Test-Path $cur) { Remove-Item $cur -Recurse -Force -ErrorAction SilentlyContinue }",
            "      }",
            "    }",
            "  }",
            "}",
            "",
            "$s = Get-Service -Name $svc -ErrorAction SilentlyContinue",
            "if ($null -ne $s -and $s.Status -ne 'Running') { Start-Service -Name $svc -ErrorAction SilentlyContinue }",
            "",
            "# 정상화됐으면 이 안전망 자신을 치운다(매 부팅 헛돌지 않게).",
            "$s2 = Get-Service -Name $svc -ErrorAction SilentlyContinue",
            "if ($null -ne $s2 -and $s2.Status -eq 'Running' -and (Test-Path $exe)) {",
            $"  $null = Sch \"/Delete /TN `\"{RecoverTaskName}`\" /F\"",
            "}",
            "exit 0",
        };
        return string.Join("\r\n", lines) + "\r\n";
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  헬퍼
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 설치 루트 판정. 실행 파일은 {app}\api 에 있으므로 부모가 {app} 이다.
    /// 개발 환경(bin\Debug\net8.0 …)에서 오판하지 않도록 **형제 폴더 watchdog 존재**로 교차 확인한다.
    /// </summary>
    private static string? ResolveAppRoot()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var parent = Directory.GetParent(baseDir)?.FullName;
            if (parent is null) return null;

            // {app}\watchdog 또는 {app}\watchdog.new 가 있어야 진짜 설치 루트다.
            if (Directory.Exists(Path.Combine(parent, "watchdog"))
                || Directory.Exists(Path.Combine(parent, SourceDirName))
                || Directory.Exists(Path.Combine(parent, StageDirName)))
            {
                return parent;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasWatchdogExe(string dir)
    {
        try { return File.Exists(Path.Combine(dir, "HitPan.Watchdog.exe")); }
        catch { return false; }
    }

    /// <summary>
    /// W4-3(워치독 자기교체)이 내장된 버전인가. 1.2.54 부터 내장(커밋 7cc39ac, PR#62).
    /// 이 버전 이상이면 워치독이 스스로 교체하므로 핸드오프는 개입하지 않는다 —
    /// 개입하면 같은 서비스를 두 체계가 동시에 stop/start 한다([4] 재검증 P2-2).
    /// 판독 불가 시 false = 개입한다(구버전으로 간주). 구버전을 못 바꾸는 쪽이
    /// 이중 교체보다 안전하다 — 못 바꾸면 다음 기회가 있지만, 동시 교체는 그 PC 를 망가뜨린다.
    /// </summary>
    private static readonly Version SelfReplaceSinceVersion = new(1, 2, 54, 0);

    private static bool HasSelfReplaceCapability(string fileVersion)
    {
        if (!Version.TryParse(fileVersion.Trim(), out var v)) return false;
        var norm = new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        return norm >= SelfReplaceSinceVersion;
    }

    /// <summary>
    /// 버전 동일 판정 (재봉합 [4] P0-2). "1.2.55" 와 "1.2.55.0" 은 같은 버전이다.
    /// System.Version 으로 정규화해 비교하고, 파싱 불가한 형식일 때만 문자열 비교로 폴백한다.
    /// </summary>
    private static bool IsSameVersion(string a, string b)
    {
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
        {
            // Version 은 미지정 필드를 -1 로 두므로 4자리로 정규화해 비교한다.
            static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
            return Norm(va) == Norm(vb);
        }
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetFileVersion(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return null;
            var v = FileVersionInfo.GetVersionInfo(exePath).FileVersion;
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch { return null; }
    }

    private static bool TryCopyDirectory(string from, string to, ILogger logger)
    {
        try
        {
            if (Directory.Exists(to)) Directory.Delete(to, recursive: true);
            Directory.CreateDirectory(to);
            foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(from, to, StringComparison.OrdinalIgnoreCase));
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(from, to, StringComparison.OrdinalIgnoreCase), overwrite: true);

            // 복사가 끝났어도 EXE 가 없으면 실패로 본다(부분 복사 방지).
            if (!HasWatchdogExe(to))
            {
                logger.LogWarning("[W4-3-H] 복사본에 실행 파일이 없습니다 — 재료 확보 실패로 처리합니다.");
                TryDeleteDirectory(to, logger);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[W4-3-H] 신버전 워치독 복사 실패");
            TryDeleteDirectory(to, logger);
            return false;
        }
    }

    /// <summary>
    /// 정리용 삭제. 실패해도 흐름을 끊지 않지만 침묵하지 않는다(헌법 #15).
    /// 남은 폴더는 다음 기동에서 재판정되므로 치명이 아니다.
    /// </summary>
    private static void TryDeleteDirectory(string dir, ILogger? logger = null)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[W4-3-H] 폴더 정리 실패({Dir}) — 다음 기동에서 재판정됩니다.", dir);
        }
    }

    private static string PsRunner(string ps1Path)
        => $"powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \\\"{ps1Path}\\\"";

    private static int RunSchtasks(string args, ILogger logger)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return -1;

            // 데드락 방지: 출력을 비동기로 먼저 읽고 나서 대기한다.
            var so = p.StandardOutput.ReadToEndAsync();
            var se = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30_000))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch (Exception killEx)
                {
                    // 이미 죽었거나 권한이 없는 경우. 어느 쪽이든 아래에서 실패(-2)로 반환하므로
                    //   교체는 시작되지 않는다. 침묵은 금지(헌법 #15).
                    logger.LogWarning(killEx, "[W4-3-H] schtasks 강제 종료 실패 — 교체를 시작하지 않습니다.");
                }
                logger.LogWarning("[W4-3-H] schtasks 시간 초과 — 강제 종료");
                return -2;
            }
            Task.WaitAll([so, se], 5_000);
            if (p.ExitCode != 0)
            {
                logger.LogWarning("[W4-3-H] schtasks 실패(exit={Code}): {Err}", p.ExitCode, se.Result.Trim());
            }
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[W4-3-H] schtasks 실행 실패");
            return -3;
        }
    }
}
