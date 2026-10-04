namespace HitPan.API.Services.ManualUpdate;

/// <summary>
/// 2026-09-30 작1 갈래 U — 수동 업데이트가 쓰는 폴더(받는 곳 · 사용 기록)의 위치와 쓰기 전 검사.
///
/// ■ 위치 = <c>{app}\</c> 아래 (병렬이슈 [3-V] 01 P0 반영 · PM 지시 9/30)
///   <c>%ProgramData%\HitPan</c> 은 일반 사용자가 하위 폴더를 '먼저' 만들 수 있다(Users 쓰기·추가 상속).
///   그러면 받은 zip·기록을 남이 바꿔치기할 자리가 생긴다. 설치 폴더 <c>{app}</c>(Program Files 아래)는
///   관리자·SYSTEM 만 쓴다 ⇒ 받는 곳·사용 기록을 그 아래에 둔다. {app} = API 실행 폴더(<c>{app}\api\</c>)의 위.
///   · 받는 곳 = <c>{app}\manual\staging</c> · 사용 기록 = <c>{app}\rollback\usage.jsonl</c>(일꾼과 한 파일 · 계약 §2·§7)
///
/// ■ 쓰기 직전 검사 = <c>BackupService.EnsureRestrictedSystemFolder</c>(C-8·C-12 · 교체 런처와 같은 공용 입구 · 한 벌)
///   20260930작1 I-API — 초판(U)은 같은 규칙을 이 파일 안에 따로 두었다(두 벌). 공용 입구로 바꾸면서 판정이
///   달라진 두 칸은 개발명세서 I-API §2 규칙 대조표에 적었다(개별 사용자 쓰기 줄 · 그룹 읽기 줄).
/// </summary>
public sealed class ManualFolders
{
    /// <summary>설치 폴더 {app}.</summary>
    public string AppRoot { get; }

    /// <summary>{app}\manual — 수동 모듈 전용.</summary>
    public string ManualDir => Path.Combine(AppRoot, "manual");

    /// <summary>{app}\manual\staging — 수동 업데이트가 받는 곳(워치독 staging 과 따로).</summary>
    public string StagingDir => Path.Combine(ManualDir, "staging");

    // ⬛ 초판 UsageFile = {app}\manual\usage.jsonl — 일꾼(local-swap.ps1)은 {app}\rollback\usage.jsonl 에 적어
    //    한 번의 사용이 두 파일로 갈렸다. 20260930작1 I-API 가 한 곳으로 모았다(계약 §2·§7).

    /// <summary>{app}\rollback — 교체 일꾼 작업 폴더(런처·일꾼과 같은 곳 · <c>LocalSwapLauncher.WorkFolderName</c>).</summary>
    public string UsageDir => Path.Combine(AppRoot, HitPan.API.Services.LocalSwap.LocalSwapLauncher.WorkFolderName);

    /// <summary>{app}\rollback\usage.jsonl — 수동 사용 기록 한 파일(덧붙이기만 · 넘기기 전 거부는 API · 넘긴 뒤 끝 상태는 일꾼).</summary>
    public string UsageFile => Path.Combine(UsageDir, "usage.jsonl");

    /// <summary>운영: API 실행 폴더의 위를 {app} 로 본다.</summary>
    public ManualFolders()
        : this(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")))
    {
    }

    /// <summary>시험: {app} 를 직접 준다.</summary>
    public ManualFolders(string appRoot)
    {
        AppRoot = Path.GetFullPath(appRoot);
    }

    /// <summary>
    /// 폴더를 쓰기 전에 검사하고(없으면 제한 권한으로 만든다) 실패하면 <see cref="InvalidOperationException"/>.
    /// 없는 부모 칸도 같은 판정으로 먼저 만든다 — 공용 판정은 없는 부모를 상속 권한으로 만들기 때문이다
    /// (<c>{app}\manual</c> 이 Program Files 상속 그대로 남지 않게).
    /// </summary>
    public static void EnsureRestricted(string dirPath)
    {
        var full = Path.GetFullPath(dirPath);
        var parent = Path.GetDirectoryName(full);
        if (parent is not null && !Directory.Exists(parent)) EnsureRestricted(parent);
        HitPan.Application.Services.BackupService.EnsureRestrictedSystemFolder(full);
    }

    /// <summary>파일 자체가 재분석 지점(연결)이면 true — 받은 파일을 믿기 전에 본다.</summary>
    public static bool IsReparsePoint(string filePath)
    {
        var info = new FileInfo(filePath);
        return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }
}
