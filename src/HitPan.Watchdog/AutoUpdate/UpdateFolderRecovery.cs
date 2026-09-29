namespace HitPan.Watchdog.AutoUpdate;

/// <summary>
/// 20260929작3 절W6 — 교체 중 전원 종료 뒤 폴더 복원 (설계 §6 · 표 R1~R4).
///
/// ■ 무엇을 푸나
///   교체(UpdateOrchestrator.TrySwapFilesAsync)는 web → web.old · 새 web → web · api → api.old · 새 api → api
///   순서로 폴더를 옮긴다. 그 사이에 전원이 꺼지면 ERP 가 뜰 폴더가 없거나(R1·R2) 새 web + 옛 api 가 섞여(R3)
///   ERP 가 못 뜬다 — 그러면 「업데이트 미완료」 안내 자체를 띄울 수 없다(2차 선행검증 §2).
///   워치독이 기동할 때 폴더 **모양만** 보고 마저 되돌린다(DB 무관).
///
/// ■ 🔴 [3-V] 병렬이슈 01 봉합 (PM 결재 9/29) — **교체 표식이 있을 때만 복원한다**
///   R3 모양(web.old 만 남고 web·api 있음)은 **성공한 업데이트의 정리 잔재**와 똑같다
///   (CleanupAfterSuccess 가 api.old 를 먼저 지우고 web.old 삭제가 잠김·전원 종료로 실패한 경우).
///   모양만 보면 정상 새 web 을 치우고 옛 web 을 되돌려 **멀쩡한 PC 를 섞인 상태로 만든다.**
///   ⇒ 교체 시작 직전에 표식 파일({app}\update-swap.marker)을 쓰고, 교체·검증이 끝나거나(성공)
///      폴더가 제자리로 돌아오면(실패·롤백) 지운다. 기동 복원은 **표식이 있을 때만** R1~R3 를 한다.
///   표식을 못 썼으면(디스크·권한) 복원도 안 한다 — 종전과 같아질 뿐 더 나빠지지 않는다.
///
/// ■ 하지 않는 것
///   · R4(web.old·api.old 둘 다 + web·api) — 교체 끝 · 검증 중 중단. 손대지 않는다(로그만 · 설계 §0 범위 밖).
///   · 프로세스 kill — 옮기다 잠겨 실패하면 로그 남기고 계속한다(새 kill 경로 0 · 설계 §6).
///   · 예외를 밖으로 던지지 않는다 — 워치독 기동을 막으면 안 된다(헌법 #15·#20).
///
/// ■ 경로는 인자로 받는다 — 시험은 임시 폴더로만 돈다(헌법 #39 · 설치본 접촉 0).
/// </summary>
public static class UpdateFolderRecovery
{
    /// <summary>교체 표식 파일 이름. {app} 바로 아래.</summary>
    public const string MarkerFileName = "update-swap.marker";

    public static string MarkerPath(string appRoot) => Path.Combine(appRoot, MarkerFileName);

    /// <summary>교체 시작 직전에 쓴다. 실패해도 교체는 막지 않는다(로그만) — 그 경우 기동 복원이 안 돌 뿐이다.</summary>
    public static bool TryWriteMarker(string appRoot, string version, ILogger logger)
    {
        try
        {
            File.WriteAllText(MarkerPath(appRoot), $"{DateTime.UtcNow:o}|{version}");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Update/Recovery] 교체 표식을 쓰지 못했습니다 — 이번 교체가 중단되면 기동 복원이 돌지 않습니다: {Path}", MarkerPath(appRoot));
            return false;
        }
    }

    /// <summary>교체·검증이 끝났을 때(성공) 지운다.</summary>
    public static void DeleteMarker(string appRoot, ILogger logger)
    {
        try
        {
            var path = MarkerPath(appRoot);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Update/Recovery] 교체 표식을 지우지 못했습니다: {Path}", MarkerPath(appRoot));
        }
    }

    /// <summary>
    /// 폴더가 제자리(web·api 있음 · .old 없음)면 표식을 지운다. 실패·롤백 경로 끝에서 부른다.
    ///   제자리가 아니면(롤백 실패 · 검증 중 예외 등) 표식을 남긴다 — 다음 기동 복원이 볼 수 있게.
    /// </summary>
    public static void ClearMarkerIfSettled(string appRoot, ILogger logger)
    {
        if (!File.Exists(MarkerPath(appRoot))) return;
        if (Classify(appRoot) == FolderShape.Settled)
            DeleteMarker(appRoot, logger);
        else
            logger.LogWarning("[Update/Recovery] 교체 뒤 폴더가 제자리가 아닙니다({Shape}) — 교체 표식을 남겨 다음 기동에서 복원합니다.", Classify(appRoot));
    }

    /// <summary>폴더 모양 판정(순수 · 파일시스템 읽기만).</summary>
    public static FolderShape Classify(string appRoot)
    {
        var web = Directory.Exists(Path.Combine(appRoot, "web"));
        var webOld = Directory.Exists(Path.Combine(appRoot, "web.old"));
        var api = Directory.Exists(Path.Combine(appRoot, "api"));
        var apiOld = Directory.Exists(Path.Combine(appRoot, "api.old"));

        if (!web && webOld) return FolderShape.R1;
        if (!api && apiOld) return FolderShape.R2;
        if (web && api && webOld && !apiOld) return FolderShape.R3;
        if (web && api && webOld && apiOld) return FolderShape.R4;
        if (web && api && !webOld && !apiOld) return FolderShape.Settled;
        return FolderShape.Other;
    }

    /// <summary>
    /// 워치독 기동 때 한 번. 표식이 있을 때만 R1 → R2 → R3 순서로 마저 되돌린다.
    ///   반환 Restored = 실제로 옛 버전을 제자리에 되돌렸는가(→ in_progress 행을 rolled_back 으로 닫는다).
    /// </summary>
    public static FolderRecoveryResult RecoverAtStartup(string appRoot, ILogger logger)
    {
        var before = Classify(appRoot);
        try
        {
            if (!File.Exists(MarkerPath(appRoot)))
            {
                if (before is FolderShape.R1 or FolderShape.R2 or FolderShape.R3 or FolderShape.R4)
                    logger.LogInformation("[Update/Recovery] 폴더 모양 {Shape} 이지만 교체 표식이 없습니다 — 교체 중 중단이 아니라고 보고 손대지 않습니다(성공 뒤 정리 잔재일 수 있음).", before);
                return new FolderRecoveryResult(before, before, Restored: false, Failed: false);
            }

            var restored = false;
            var failed = false;

            // R1 — web 밀어낸 직후 중단: web.old → web
            if (!RestoreIfMissing(appRoot, "web", logger, ref restored)) failed = true;
            // R2 — api 밀어낸 직후 중단: api.old → api (이 뒤 모양이 R3 가 될 수 있다 — 아래가 이어 받는다)
            if (!RestoreIfMissing(appRoot, "api", logger, ref restored)) failed = true;

            // R3 — web 만 신버전(혼합): web → web.failed, web.old → web
            if (!failed && Classify(appRoot) == FolderShape.R3)
            {
                try
                {
                    var web = Path.Combine(appRoot, "web");
                    var webFailed = Path.Combine(appRoot, "web.failed");
                    if (Directory.Exists(webFailed)) Directory.Delete(webFailed, recursive: true);
                    Directory.Move(web, webFailed);
                    Directory.Move(Path.Combine(appRoot, "web.old"), web);
                    restored = true;
                    logger.LogWarning("[Update/Recovery] R3 — 새 web 과 옛 api 가 섞여 있어 web 을 이전 버전으로 되돌렸습니다(새 web 은 web.failed).");
                }
                catch (Exception ex)
                {
                    failed = true;
                    logger.LogWarning(ex, "[Update/Recovery] R3 되돌림 실패(파일 잠김 등) — 워치독 기동은 계속합니다.");
                }
            }

            var after = Classify(appRoot);
            if (after == FolderShape.R4)
                logger.LogWarning("[Update/Recovery] R4 — 교체는 끝났고 검증 중 중단된 모양입니다. 손대지 않습니다(범위 밖 · 로그만).");

            if (after == FolderShape.Settled)
                DeleteMarker(appRoot, logger);

            if (restored)
                logger.LogWarning("[Update/Recovery] 교체 중 중단을 발견해 이전 버전으로 되돌렸습니다({Before} → {After}).", before, after);
            return new FolderRecoveryResult(before, after, restored, failed);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Update/Recovery] 기동 폴더 복원 중 예외 — 워치독 기동은 계속합니다.");
            return new FolderRecoveryResult(before, before, Restored: false, Failed: true);
        }
    }

    /// <summary>
    /// UpdateOrchestrator.TrySwapFilesAsync :351 가드 — 교체 시작 전 .old 를 지우기 **앞**에서
    ///   R1·R2 모양(현재 폴더 없음 + .old 있음)이면 지우지 말고 먼저 되돌린다(유일한 구버전 보호).
    ///   반환 false = 되돌리지 못했다 ⇒ 호출부는 .old 를 지우면 안 되고 교체를 멈춰야 한다.
    ///   표식은 보지 않는다 — 「현재 폴더가 없다」는 어떤 정상 경로에서도 생기지 않는 모양이다.
    /// </summary>
    public static bool RestoreMissingFromOld(string appRoot, ILogger logger)
    {
        var restored = false;
        var okWeb = RestoreIfMissing(appRoot, "web", logger, ref restored);
        var okApi = RestoreIfMissing(appRoot, "api", logger, ref restored);
        return okWeb && okApi;
    }

    private static bool RestoreIfMissing(string appRoot, string name, ILogger logger, ref bool restored)
    {
        var current = Path.Combine(appRoot, name);
        var old = Path.Combine(appRoot, name + ".old");
        if (Directory.Exists(current) || !Directory.Exists(old)) return true;   // R 모양 아님 — 할 일 없음
        try
        {
            Directory.Move(old, current);
            restored = true;
            logger.LogWarning("[Update/Recovery] {Name} 폴더가 없고 {Name}.old 만 있어 이전 버전을 제자리로 되돌렸습니다.", name, name);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Update/Recovery] {Name}.old → {Name} 되돌림 실패 — 워치독 기동은 계속합니다.", name, name);
            return false;
        }
    }
}

/// <summary>{app} 폴더 모양(설계 §6 표).</summary>
public enum FolderShape
{
    /// <summary>web·api 있음 · .old 없음 — 정상.</summary>
    Settled,
    /// <summary>web 없음 · web.old 있음.</summary>
    R1,
    /// <summary>api 없음 · api.old 있음.</summary>
    R2,
    /// <summary>web.old 만 · web·api 있음(혼합 — 또는 성공 뒤 정리 잔재 · 표식으로 가른다).</summary>
    R3,
    /// <summary>web.old·api.old 둘 다 · web·api 있음(검증 중 중단 — 손대지 않음).</summary>
    R4,
    /// <summary>그 밖(api.old 만 남은 정리 잔재 등) — 손대지 않음.</summary>
    Other
}

/// <summary>기동 복원 결과.</summary>
public sealed record FolderRecoveryResult(FolderShape Before, FolderShape After, bool Restored, bool Failed);
