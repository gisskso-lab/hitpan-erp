using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 업데이트 중 안내화면이 "히트판이 다시 떴는가"를 확인하는 전용 경로 (20260806작7).
///
/// ── 왜 /health 를 못 쓰는가 ([3-V] 병렬검증 P0-V2-A 적발) ──────────────────────────
/// `/health` 는 `[AllowAnonymous]` 지만 **브라우저에서 부를 수 없다.** 인증보다 앞에 있는
/// HealthIpWhitelistMiddleware(:15) 가 `/health` 로 시작하는 모든 경로를 가로채 루프백 외에는
/// 403 을 준다. `[AllowAnonymous]` 는 인증만 면제할 뿐 IP 화이트리스트를 통과시키지 않는다
/// (HealthController.cs:10-22 가 "이 주석에 속아 공개 엔드포인트로 오판한 사례가 실제로 있었다"고
/// 이미 경고하고 있다 — 같은 함정을 또 밟을 뻔했다).
///
/// 터널이 로컬 프록시라 RemoteIpAddress 가 우연히 127.0.0.1 이 될 수는 있으나, 그건 보장이 아니라
/// **배포 형태에 기댄 우연**이다. `HealthCheck:AllowedIps` 설정이 바뀌거나 UseForwardedHeaders 가
/// 배선되는 순간 전 고객의 안내화면이 조용히 무한 대기에 빠진다.
///
/// ⇒ 화이트리스트를 **건드리지 않고**(보안 축소 0) 별도 경로를 둔다.
///
/// ── 무엇을 노출하는가 ────────────────────────────────────────────────────────────
/// 버전 문자열 하나뿐이다. DB 도 안 보고 내부 상태도 안 준다. `/health` 처럼 db·환경·가동시간을
/// 노출하지 않으므로 공개해도 안전하다(헌법 #18·#22 — 최소한만).
/// </summary>
[ApiController]
[Route("api/app-version")]
[AllowAnonymous]
public sealed class AppVersionController : ControllerBase
{
    /// <summary>
    /// 지금 살아서 응답하는 히트판의 버전.
    /// 이 경로가 응답한다는 것 자체가 "히트판이 떴다"는 뜻이고, 버전이 새 것과 같으면 교체 완료다.
    ///
    /// ★ 200 만으로 완료를 판정하면 안 된다 — 교체 직후 옛 프로세스가 아직 살아 옛 버전 200 을
    ///   주는 창이 있다(UpdateOrchestrator.cs:613 "구버전도 200 을 준다"가 명시). 그래서 호출부는
    ///   반드시 version 값까지 대조한다. 여기서는 사실만 정확히 돌려준다.
    /// </summary>
    [HttpGet]
    public IActionResult Get() => Ok(new { version = VersionInfo.Current });
}
