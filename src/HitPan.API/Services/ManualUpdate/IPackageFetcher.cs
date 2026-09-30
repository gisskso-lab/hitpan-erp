namespace HitPan.API.Services.ManualUpdate;

// 2026-09-30 작1 갈래 U — 수동 업데이트 모듈의 이음매 ② (설계 §13-7).
//   패키지 받기 · 해시 대조 · 디스크 여유 판정. 지금 구현은 워치독 원본(UpdateClient·UpdateDiskSpaceGuard) 링크 어댑터.
//   자동 경로와 '같은 한 벌'을 쓴다 — 디스크 기준·해시 판정이 수동에서만 느슨해지는 일이 없게(설계 §13-3).

/// <summary>수동 업데이트 — 패키지 받기·해시·디스크.</summary>
public interface IPackageFetcher
{
    /// <summary>받을 패키지 크기로 저장 공간이 충분한가(워치독 <c>UpdateDiskSpaceGuard</c> 기준 그대로).</summary>
    bool HasEnoughSpace(long packageSizeBytes, string targetDir);

    /// <summary>패키지를 <paramref name="targetDir"/> 에 받고 파일 경로를 돌려준다. 실패하면 예외.</summary>
    Task<string> DownloadAsync(FeedPackage package, string targetDir, CancellationToken ct);

    /// <summary>파일 해시가 서명된 매니페스트의 sha256 과 같은가. 판정 불능이면 false.</summary>
    Task<bool> VerifySha256Async(string filePath, string expectedSha256, CancellationToken ct);
}

/// <summary>
/// 워치독 자동 업데이트가 지금 진행 중인가(워치독 <c>UpdateLockFile</c> 규칙 그대로 · 읽기만).
/// 잡혀 있으면 수동 업데이트는 시작하지 않는다(G-U5). 워치독 프로세스를 부르지 않는다(G-U3).
/// </summary>
public interface IAutoUpdateLockProbe
{
    bool IsAutoUpdateInProgress();
}
