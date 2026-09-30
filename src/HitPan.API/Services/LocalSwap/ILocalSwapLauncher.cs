namespace HitPan.API.Services.LocalSwap;

/// <summary>
/// 교체 일꾼 런처 — 설계 §13-7 이음매 셋 가운데 하나. 수동 되돌리기(갈래 A)와 수동 업데이트(갈래 U)가 같은 런처를 쓴다.
/// 하는 일: 바쁨 판정 → 한 번에 하나 잠금 → <c>request.json</c> → 일꾼 사본 → 1회용 SYSTEM 작업 <c>HitPan-LocalSwap</c> 등록·실행.
/// </summary>
/// <remarks>
/// 🔴 런처는 자료(업무 데이터)에 닿지 않는다 — 생성자에 자료 연결·저장소 주입 0(G-D1). 자료 백업은 U 가 <b>부르기 전에</b> 끝낸다.
/// </remarks>
public interface ILocalSwapLauncher
{
    /// <summary>
    /// 지금 교체를 시작할 수 없는 이유. 없으면 null. 두 모드 공통(계약 §4 · G-U5 · 병렬이슈 03 쿨다운 포함).
    /// </summary>
    string? CheckBusy();

    /// <summary>마지막 요청서(끝 상태 알림용 · 읽기만). 없거나 못 읽으면 null.</summary>
    SwapRequest? ReadLast();

    /// <summary>
    /// 교체를 건다. 판·재료는 호출부가 <b>서버에서 계산한 값</b>만 넘긴다(병렬이슈 04).
    /// 실패하면 작업 등록 0 · 잠금 풀림.
    /// </summary>
    SwapLaunchResult Launch(SwapLaunchInput input);
}

/// <summary>설치 환경 읽기 — 시험에서 대역으로 바꾼다.</summary>
public interface ILocalSwapEnvironment
{
    /// <summary>윈도인가. 아니면 교체를 걸지 않는다.</summary>
    bool IsWindows { get; }

    /// <summary><c>{app}</c> — API 실행 폴더의 상위에 <c>watchdog</c> 폴더가 있을 때만. 못 찾으면 null.</summary>
    string? AppRoot { get; }

    /// <summary>API 출력 폴더 안의 일꾼 원본(<c>{api}\Rollback\local-swap.ps1</c>).</summary>
    string ScriptSourcePath { get; }

    /// <summary>지금 판 <c>M.m.b</c>(<c>VersionInfo.Current</c>).</summary>
    string CurrentVersion { get; }

    /// <summary>작업 이름 번호(설치 설정의 SLOT_INDEX). 못 읽으면 null.</summary>
    int? Slot { get; }

    /// <summary>로컬 API 포트(설치 설정의 API_PORT · 기본 5257).</summary>
    int ApiPort { get; }

    /// <summary>워치독이 받아 둔 zip 폴더(워치독 <c>UpdateOrchestrator</c> 와 같은 규칙 · 읽기만).</summary>
    string WatchdogStagingDir { get; }

    /// <summary>지금 UTC(시험에서 바꾼다).</summary>
    DateTime UtcNow { get; }
}

/// <summary>schtasks 실행 — 시험에서 대역으로 바꾼다. 반환 = 종료 코드.</summary>
public interface ISchtasksRunner
{
    int Run(string arguments);
}

/// <summary>
/// 작업 폴더 안전 판정(병렬이슈 01) — 소유자·넓은 그룹 권한 줄·재분석 지점.
/// 안전하지 않으면 예외. 기본 구현은 <c>BackupService.EnsureRestrictedSystemFolder</c>(C-8·C-12) 를 그대로 부른다.
/// </summary>
public interface ISwapFolderGuard
{
    void EnsureSafe(string path);
}
