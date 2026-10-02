namespace HitPan.API.Services.ManualUpdate;

// 2026-09-30 작1 갈래 U — 수동 업데이트 모듈의 이음매 ① (설계 §13-7).
//   「최신 판이 무엇인가」를 묻는 창구. 지금 구현은 워치독 원본(UpdateClient·UpdateSignatureVerifier)을 링크 컴파일해
//   감싼 WatchdogUpdateCoreAdapter 하나다. 나중에 링크 파일을 공용 라이브러리로 옮겨도 이 인터페이스는 그대로다.
//   🔴 서명 검증은 이 창구 안에서 '가장 먼저' 끝난다 — 서명이 틀리면 판·주소·해시 어느 것도 바깥으로 나오지 않는다.

/// <summary>피드 조회 결과 종류.</summary>
public enum FeedCheckStatus
{
    /// <summary>서명이 맞고, 지금 판보다 높은 판이 있다.</summary>
    Newer,

    /// <summary>서명은 맞으나 지금 판과 같거나 낮다(다운그레이드 · 최신 유지).</summary>
    NotNewer,

    /// <summary>피드에 닿지 못했다(네트워크·시간 초과·HTTP 오류).</summary>
    Unreachable,

    /// <summary>피드에는 닿았으나 서명이 없거나 틀렸다 · 매니페스트가 비었다 · 판 형식을 해석할 수 없다.</summary>
    Invalid,
}

/// <summary>서명 검증을 통과한 매니페스트에서 수동 모듈이 쓰는 값만 옮긴 것(워치독 형식을 바깥에 새지 않게).</summary>
public sealed record FeedPackage(
    string Version,
    string Channel,
    string DownloadUrl,
    string Sha256,
    long SizeBytes,
    string? ReleaseNotes);

/// <summary>피드 조회 결과. <see cref="Package"/> 는 <see cref="FeedCheckStatus.Newer"/> 일 때만 채워진다.</summary>
public sealed record FeedCheckResult(FeedCheckStatus Status, FeedPackage? Package, string? Detail);

/// <summary>수동 업데이트 — 최신 판 확인(피드 조회 + 서명 검증 + 판 비교).</summary>
public interface IUpdateFeed
{
    Task<FeedCheckResult> CheckAsync(string currentVersion, CancellationToken ct);
}
